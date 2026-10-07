using Bookletter.Imposition;
using Bookletter.Output;
using Bookletter.Rendering;
using SkiaSharp;

namespace Bookletter.Cli;

public sealed class CliOptions
{
    public required string InputPath { get; set; }
    public string OutputDir { get; set; } = "output";
    public int SignatureSize { get; set; } = 16;
    public OutputMode Mode { get; set; } = OutputMode.FrontBack;
    public PageOrderConvention PageOrder { get; set; } = PageOrderConvention.Standard;
    public string SheetSize { get; set; } = "auto";
    public int Dpi { get; set; } = 300;
    public double MarginPt { get; set; } = 0;
    public double GapPt { get; set; } = 0;
    public SKColor Background { get; set; } = SKColors.White;
    public string ImageFormat { get; set; } = "png";
    public int JpegQuality { get; set; } = 90;
    public bool ReverseBackOrder { get; set; } = false;
    public int SkipPages { get; set; } = 0;
    public bool SplitSignatures { get; set; } = false;
    public bool AllowUpscale { get; set; } = false;
    public bool CutMarks { get; set; } = false;
    public double CutMarkGapPt { get; set; } = 8.5;    // ~3mm
    public double CutMarkLengthPt { get; set; } = 14.2; // ~5mm
    public double CutMarkStrokePt { get; set; } = 0.25;
    public SKColor CutMarkColor { get; set; } = SKColors.Black;

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            PrintUsage();
            return null;
        }

        string? input = null;
        var opts = new Dictionary<string, string>();
        var flags = new HashSet<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--input":
                case "-i":
                    input = RequireValue(args, ref i, a);
                    break;
                case "--reverse-back-order":
                case "--split-signatures":
                case "--allow-upscale":
                case "--cut-marks":
                    flags.Add(a);
                    break;
                default:
                    if (a.StartsWith("--"))
                        opts[a] = RequireValue(args, ref i, a);
                    else if (input is null)
                        input = a;
                    else
                        throw new ArgumentException($"Unrecognized argument '{a}'.");
                    break;
            }
        }

        if (input is null)
            throw new ArgumentException("Missing required --input <path-to-pdf>.");

        var result = new CliOptions { InputPath = input };

        if (opts.TryGetValue("--output", out var v)) result.OutputDir = v;
        if (opts.TryGetValue("-o", out v)) result.OutputDir = v;
        if (opts.TryGetValue("--signature-size", out v)) result.SignatureSize = int.Parse(v);
        if (opts.TryGetValue("--mode", out v)) result.Mode = ParseMode(v);
        if (opts.TryGetValue("--page-order", out v)) result.PageOrder = ParsePageOrder(v);
        if (opts.TryGetValue("--sheet-size", out v)) result.SheetSize = v;
        if (opts.TryGetValue("--dpi", out v)) result.Dpi = int.Parse(v);
        if (opts.TryGetValue("--margin", out v)) result.MarginPt = ParseLength(v);
        if (opts.TryGetValue("--gap", out v)) result.GapPt = ParseLength(v);
        if (opts.TryGetValue("--background", out v)) result.Background = ParseColor(v);
        if (opts.TryGetValue("--image-format", out v)) result.ImageFormat = v;
        if (opts.TryGetValue("--jpeg-quality", out v)) result.JpegQuality = int.Parse(v);
        if (opts.TryGetValue("--skip-pages", out v)) result.SkipPages = int.Parse(v);
        if (opts.TryGetValue("--cut-mark-gap", out v)) result.CutMarkGapPt = ParseLength(v);
        if (opts.TryGetValue("--cut-mark-length", out v)) result.CutMarkLengthPt = ParseLength(v);
        if (opts.TryGetValue("--cut-mark-stroke", out v)) result.CutMarkStrokePt = ParseLength(v);
        if (opts.TryGetValue("--cut-mark-color", out v)) result.CutMarkColor = ParseColor(v);
        result.ReverseBackOrder = flags.Contains("--reverse-back-order");
        result.SplitSignatures = flags.Contains("--split-signatures");
        result.AllowUpscale = flags.Contains("--allow-upscale");
        result.CutMarks = flags.Contains("--cut-marks");

        return result;
    }

    public CutMarkOptions ToCutMarkOptions() =>
        new(CutMarks, CutMarkGapPt, CutMarkLengthPt, CutMarkStrokePt, CutMarkColor);

    private static string RequireValue(string[] args, ref int i, string optName)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for '{optName}'.");
        return args[++i];
    }

    private static OutputMode ParseMode(string v)
    {
        OutputMode mode = OutputMode.None;
        foreach (var part in v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            mode |= part.ToLowerInvariant() switch
            {
                "front-back" or "frontback" => OutputMode.FrontBack,
                "duplex" => OutputMode.Duplex,
                "images" or "image" => OutputMode.Images,
                "all" => OutputMode.All,
                _ => throw new ArgumentException($"Unknown --mode value '{part}'. Use front-back, duplex, images, all (comma-separated combinations allowed).")
            };
        }
        return mode == OutputMode.None ? OutputMode.FrontBack : mode;
    }

    private static PageOrderConvention ParsePageOrder(string v) => v.ToLowerInvariant() switch
    {
        "standard" => PageOrderConvention.Standard,
        "reversed" => PageOrderConvention.Reversed,
        _ => throw new ArgumentException($"Unknown --page-order value '{v}'. Use standard or reversed.")
    };

    public static double ParseLength(string v)
    {
        v = v.Trim();
        string unit = "pt";
        int i = v.Length;
        while (i > 0 && !char.IsDigit(v[i - 1]) && v[i - 1] != '.') i--;
        if (i < v.Length) unit = v[i..].Trim();
        double num = double.Parse(v[..i]);
        return unit.ToLowerInvariant() switch
        {
            "pt" => num,
            "mm" => num / 25.4 * 72.0,
            "cm" => num / 2.54 * 72.0,
            "in" => num * 72.0,
            _ => throw new ArgumentException($"Unknown unit '{unit}'. Use pt, mm, cm or in.")
        };
    }

    public static SKColor ParseColor(string v)
    {
        v = v.TrimStart('#');
        if (SKColor.TryParse(v.Length == 6 || v.Length == 8 ? "#" + v : v, out var color))
            return color;
        throw new ArgumentException($"Unrecognized color '{v}'. Use a hex value like FFFFFF.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Bookletter - impose a PDF into saddle-stitch signatures for booklet printing.

        Usage:
          bookletter --input <file.pdf> [options]

        Options:
          -i, --input <path>        Source PDF (required).
          -o, --output <dir>        Output directory (default: output).
          --signature-size <n>      Pages per signature; must be a multiple of 4 (default: 16).
          --skip-pages <n>          Drop the first n pages of the source PDF before imposition (default: 0).
                                       Use this when a cover or title page is handled separately and shouldn't
                                       be counted as page 1 of the booklet.
          --split-signatures        Write one front/back/duplex file set per signature instead of one combined
                                       set for the whole document, e.g. booklet-front-sig01.pdf, -sig02.pdf, ...
                                       Use this when each signature will be folded and stitched as its own
                                       physical booklet (e.g. for perfect-bound or multi-signature books).
          --mode <list>             Comma-separated: front-back, duplex, images, all (default: front-back).
                                       front-back : booklet-front.pdf + booklet-back.pdf (manual duplex).
                                       duplex     : booklet-duplex.pdf, front/back interleaved (auto-duplex printers).
                                       images     : one image file per sheet side.
          --page-order <mode>       standard or reversed (default: standard). Swaps left/right of every pair;
                                       use for right-to-left books or to match your printer's duplex flip direction.
          --reverse-back-order      Reverse page order within booklet-back.pdf only. Try this if a manual
                                       duplex print comes out with fronts/backs mismatched after flipping the stack.
          --sheet-size <spec>       auto, A3, A4, A5, Letter, Legal, or WIDTHxHEIGHT with unit mm/cm/in/pt
                                       (default: auto - derived from the source PDF's own page size).
          --dpi <n>                 Rasterization resolution in DPI (default: 300). Also flattens fonts/vector
                                       content to pixels, which can help with printers that mishandle mapped fonts.
          --margin <n[unit]>        Outer margin per page cell, e.g. 5mm (default: 0). Needs to be larger than 0
                                       (or use a --sheet-size bigger than the source page) for --cut-marks to have
                                       room to draw.
          --gap <n[unit]>           Gap between the two pages on a sheet, e.g. 3mm (default: 0).
          --background <hex>       Sheet background color (default: FFFFFF).
          --allow-upscale           Allow source pages smaller than their cell to be scaled up to fill it
                                       (default: off - pages are printed at their native size and centered,
                                       never enlarged, which is what --cut-marks relies on).
          --cut-marks               Draw trim marks at each page's true corners, and extend its (assumed solid)
                                       background color to fill the gap between the page and its cell edge, so an
                                       imprecise trim doesn't reveal a white border. Useful when the source page
                                       is slightly smaller than a standard sheet size you're printing on.
          --cut-mark-gap <n[unit]>  Gap between the trim corner and the start of each mark line (default: 3mm).
                                       Use 0 for simple marks that touch the corner directly; a few mm gives the
                                       standard print-shop "overshoot" style that keeps marks out of the trim zone.
          --cut-mark-length <n[unit]> Length of each mark line (default: 5mm).
          --cut-mark-stroke <n[unit]> Mark line thickness (default: 0.25pt).
          --cut-mark-color <hex>   Mark line color (default: 000000).
          --image-format <fmt>      png or jpg, for --mode images (default: png).
          --jpeg-quality <n>        JPEG quality 1-100, for --mode images (default: 90).
          -h, --help                Show this help.

        Example:
          bookletter --input book.pdf --output ./out --signature-size 16 --mode all --dpi 300
        """);
    }
}
