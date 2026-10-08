using Bookletter.Cli;
using Bookletter.Imposition;
using Bookletter.Output;
using Bookletter.Rendering;
using SkiaSharp;

namespace Bookletter;

public sealed class BookletGenerator
{
    private readonly CliOptions _opts;

    public BookletGenerator(CliOptions opts) => _opts = opts;

    /// <summary>
    /// Imposes the document and writes the configured output file(s).
    /// </summary>
    /// <param name="onProgress">
    /// Called once with (0, totalSheets) as soon as the sheet count is known, then once
    /// more after each sheet is composed, as (sheetsComposed, totalSheets) - covers the
    /// rasterization-heavy part of the work. Writing the output file(s) afterward is
    /// comparatively fast and isn't tracked separately.
    /// </param>
    /// <param name="cancellationToken">
    /// Checked once per sheet, so cancelling stops generation after the sheet currently
    /// in progress rather than mid-sheet. Not checked during the file-writing phase
    /// after the loop - that part is fast enough it isn't worth interrupting partway.
    /// </param>
    public void Run(Action<int, int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_opts.OutputDir);

        var imposition = PrepareImposition();
        var (rasterizer, spreads, sheetW, sheetH, nativeW, nativeH, sheetsPerSignature, selectedPages) = imposition;
        var composer = new SheetComposer(_opts.Dpi, _opts.MarginPt, _opts.GapPt, _opts.Background, _opts.AllowUpscale, _opts.ToCutMarkOptions());

        int numSignatures = (int)Math.Ceiling(selectedPages.Count / (double)_opts.SignatureSize);
        Console.WriteLine($"Input:      {_opts.InputPath}");
        if (!string.IsNullOrWhiteSpace(_opts.Pages))
            Console.WriteLine($"Selected:   {_opts.Pages}");
        Console.WriteLine($"Pages:      {selectedPages.Count} (padded to {numSignatures * _opts.SignatureSize} across {numSignatures} signature(s) of {_opts.SignatureSize})");
        Console.WriteLine($"Sheets:     {spreads.Count}");
        Console.WriteLine($"Sheet size: {sheetW:0.##} x {sheetH:0.##} pt ({sheetW / 72.0:0.##} x {sheetH / 72.0:0.##} in) @ {_opts.Dpi} dpi");
        Console.WriteLine($"Native page: {nativeW:0.##} x {nativeH:0.##} pt ({nativeW / 72.0:0.##} x {nativeH / 72.0:0.##} in)");
        if (_opts.CutMarks)
            Console.WriteLine($"Cut marks:  gap {_opts.CutMarkGapPt / 72.0 * 25.4:0.##}mm, length {_opts.CutMarkLengthPt / 72.0 * 25.4:0.##}mm");
        Console.WriteLine($"Output:     {Path.GetFullPath(_opts.OutputDir)}");

        var frontPages = new List<byte[]>(spreads.Count);
        var backPages = new List<byte[]>(spreads.Count);

        // Page numbers from SignatureCalculator are 1-indexed positions within the
        // selected-pages list; look up the actual 0-indexed source page they refer to.
        SKBitmap? RenderOrNull(int? pageNumber) =>
            pageNumber.HasValue ? rasterizer.RenderPage(selectedPages[pageNumber.Value - 1], _opts.Dpi) : null;

        byte[] ComposeSide(int? leftPage, int? rightPage)
        {
            using var left = RenderOrNull(leftPage);
            using var right = RenderOrNull(rightPage);
            using var sheet = composer.ComposeSheet(sheetW, sheetH, left, right, nativeW, nativeH, leftPage, rightPage);
            return ImageFileWriter.EncodePng(sheet);
        }

        // Logged as the real source page number (1-based), not the position within
        // the selection - what a reader of the log wants to know is which page of
        // their actual PDF ended up where.
        string Fmt(int? pageNumber) => pageNumber.HasValue ? (selectedPages[pageNumber.Value - 1] + 1).ToString() : "blank";

        onProgress?.Invoke(0, spreads.Count);
        for (int i = 0; i < spreads.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var spread = spreads[i];
            frontPages.Add(ComposeSide(spread.FrontLeftPage, spread.FrontRightPage));
            backPages.Add(ComposeSide(spread.BackLeftPage, spread.BackRightPage));

            Console.WriteLine($"  signature {spread.SignatureIndex + 1}, sheet {spread.SheetIndexInSignature + 1} " +
                               $"(global {spread.GlobalSheetIndex + 1}/{spreads.Count}): " +
                               $"front[{Fmt(spread.FrontLeftPage)}|{Fmt(spread.FrontRightPage)}] " +
                               $"back[{Fmt(spread.BackLeftPage)}|{Fmt(spread.BackRightPage)}]");

            onProgress?.Invoke(i + 1, spreads.Count);
        }

        if (_opts.Mode.HasFlag(OutputMode.Images))
            WriteImages(spreads, frontPages, backPages);

        if (_opts.Mode.HasFlag(OutputMode.FrontBack))
            WriteFrontBackPdfs(frontPages, backPages, sheetW, sheetH, sheetsPerSignature);

        if (_opts.Mode.HasFlag(OutputMode.Duplex))
            WriteDuplexPdf(frontPages, backPages, sheetW, sheetH, sheetsPerSignature);

        Console.WriteLine("Done.");
    }

    /// <summary>
    /// Cheap lookup for the GUI's preview: page/signature/sheet counts and sheet
    /// dimensions, without rasterizing any page content. Just reads the source PDF's
    /// page count and native page size - fast even for a very large PDF.
    /// </summary>
    public PreviewInfo GetPreviewInfo()
    {
        var (_, spreads, sheetW, sheetH, _, _, _, selectedPages) = PrepareImposition();
        int signatureCount = (int)Math.Ceiling(selectedPages.Count / (double)_opts.SignatureSize);
        return new PreviewInfo(selectedPages.Count, signatureCount, spreads.Count, sheetW, sheetH);
    }

    /// <summary>
    /// The source PDF's raw page count, independent of any --pages selection. Used by
    /// the GUI to validate the Pages field against the actual document on its own,
    /// without going through the rest of PrepareImposition (which itself depends on
    /// Pages already being valid, so it can't answer "is Pages valid for this file").
    /// </summary>
    public int GetSourcePageCount() => new PdfRasterizer(_opts.InputPath).PageCount;

    /// <summary>
    /// Renders a single sheet side for the current options without writing any output
    /// files or creating the output directory - used for a quick settings preview.
    /// <paramref name="globalSheetIndex"/> is the same 0-indexed "global sheet" numbering
    /// shown in the GUI's signature map, clamped to the valid range. An explicit
    /// <paramref name="dpiOverride"/> lets the caller ask for a cheaper/faster render
    /// than the real <see cref="CliOptions.Dpi"/> (e.g. a quick low-res pass).
    /// </summary>
    public byte[] RenderPreviewSheet(bool front = true, int globalSheetIndex = 0, int? dpiOverride = null)
    {
        var (rasterizer, spreads, sheetW, sheetH, nativeW, nativeH, _, selectedPages) = PrepareImposition();
        if (spreads.Count == 0)
            throw new InvalidOperationException("Nothing to preview - the document has no pages.");

        var spread = spreads[Math.Clamp(globalSheetIndex, 0, spreads.Count - 1)];
        int dpi = dpiOverride ?? _opts.Dpi;
        var composer = new SheetComposer(dpi, _opts.MarginPt, _opts.GapPt, _opts.Background, _opts.AllowUpscale, _opts.ToCutMarkOptions());

        SKBitmap? RenderOrNull(int? pageNumber) =>
            pageNumber.HasValue ? rasterizer.RenderPage(selectedPages[pageNumber.Value - 1], dpi) : null;

        var (leftPage, rightPage) = front ? (spread.FrontLeftPage, spread.FrontRightPage) : (spread.BackLeftPage, spread.BackRightPage);
        using var left = RenderOrNull(leftPage);
        using var right = RenderOrNull(rightPage);
        using var sheet = composer.ComposeSheet(sheetW, sheetH, left, right, nativeW, nativeH, leftPage, rightPage);
        return ImageFileWriter.EncodePng(sheet);
    }

    private (PdfRasterizer Rasterizer, IReadOnlyList<SheetSpread> Spreads, double SheetW, double SheetH, double NativeW, double NativeH, int SheetsPerSignature, IReadOnlyList<int> SelectedPages) PrepareImposition()
    {
        var rasterizer = new PdfRasterizer(_opts.InputPath);
        int sourcePageCount = rasterizer.PageCount;

        var selectedPages = PageSelector.Parse(_opts.Pages, sourcePageCount);
        var spreads = SignatureCalculator.Calculate(selectedPages.Count, _opts.SignatureSize, _opts.PageOrder);
        int sheetsPerSignature = _opts.SignatureSize / 4;

        var sizeSpec = SheetSizeSpec.Parse(_opts.SheetSize);
        var (nativeW, nativeH) = rasterizer.GetPageSizePt(selectedPages[0]);
        var (sheetW, sheetH) = sizeSpec.Resolve(nativeW, nativeH, _opts.MarginPt, _opts.GapPt);

        return (rasterizer, spreads, sheetW, sheetH, nativeW, nativeH, sheetsPerSignature, selectedPages);
    }

    private void WriteFrontBackPdfs(List<byte[]> frontPages, List<byte[]> backPages, double sheetW, double sheetH, int sheetsPerSignature)
    {
        if (!_opts.SplitSignatures)
        {
            WriteFrontBackPair(frontPages, backPages, 0, frontPages.Count, sheetW, sheetH, suffix: "");
            return;
        }

        int numSignatures = frontPages.Count / sheetsPerSignature;
        for (int sig = 0; sig < numSignatures; sig++)
            WriteFrontBackPair(frontPages, backPages, sig * sheetsPerSignature, sheetsPerSignature, sheetW, sheetH, $"-sig{sig + 1:D2}");
    }

    private void WriteFrontBackPair(List<byte[]> frontPages, List<byte[]> backPages, int start, int count, double sheetW, double sheetH, string suffix)
    {
        using var frontWriter = new PdfSheetWriter();
        for (int i = start; i < start + count; i++)
            frontWriter.AddPage(frontPages[i], sheetW, sheetH);
        frontWriter.Save(Path.Combine(_opts.OutputDir, $"booklet-front{suffix}.pdf"));

        var backChunk = backPages.GetRange(start, count);
        var backOrdered = _opts.ReverseBackOrder ? Enumerable.Reverse(backChunk) : backChunk;
        using var backWriter = new PdfSheetWriter();
        foreach (var png in backOrdered)
            backWriter.AddPage(png, sheetW, sheetH);
        backWriter.Save(Path.Combine(_opts.OutputDir, $"booklet-back{suffix}.pdf"));

        Console.WriteLine($"Wrote booklet-front{suffix}.pdf and booklet-back{suffix}.pdf");
    }

    private void WriteDuplexPdf(List<byte[]> frontPages, List<byte[]> backPages, double sheetW, double sheetH, int sheetsPerSignature)
    {
        if (!_opts.SplitSignatures)
        {
            WriteDuplexChunk(frontPages, backPages, 0, frontPages.Count, sheetW, sheetH, suffix: "");
            return;
        }

        int numSignatures = frontPages.Count / sheetsPerSignature;
        for (int sig = 0; sig < numSignatures; sig++)
            WriteDuplexChunk(frontPages, backPages, sig * sheetsPerSignature, sheetsPerSignature, sheetW, sheetH, $"-sig{sig + 1:D2}");
    }

    private void WriteDuplexChunk(List<byte[]> frontPages, List<byte[]> backPages, int start, int count, double sheetW, double sheetH, string suffix)
    {
        using var duplexWriter = new PdfSheetWriter();
        for (int i = start; i < start + count; i++)
        {
            duplexWriter.AddPage(frontPages[i], sheetW, sheetH);
            duplexWriter.AddPage(backPages[i], sheetW, sheetH);
        }
        duplexWriter.Save(Path.Combine(_opts.OutputDir, $"booklet-duplex{suffix}.pdf"));

        Console.WriteLine($"Wrote booklet-duplex{suffix}.pdf");
    }

    private void WriteImages(IReadOnlyList<SheetSpread> spreads, List<byte[]> frontPages, List<byte[]> backPages)
    {
        bool jpeg = _opts.ImageFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                    _opts.ImageFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
        string ext = jpeg ? "jpg" : "png";

        void WriteOne(byte[] pngBytes, SheetSpread spread, string side)
        {
            string name = $"sig{spread.SignatureIndex + 1:D2}_sheet{spread.SheetIndexInSignature + 1:D2}_{side}.{ext}";
            string path = Path.Combine(_opts.OutputDir, name);

            if (!jpeg)
            {
                File.WriteAllBytes(path, pngBytes);
                return;
            }

            using var bmp = SKBitmap.Decode(pngBytes);
            ImageFileWriter.Write(bmp, path, "jpg", _opts.JpegQuality);
        }

        for (int i = 0; i < spreads.Count; i++)
        {
            WriteOne(frontPages[i], spreads[i], "front");
            WriteOne(backPages[i], spreads[i], "back");
        }

        Console.WriteLine($"Wrote {spreads.Count * 2} image file(s).");
    }
}
