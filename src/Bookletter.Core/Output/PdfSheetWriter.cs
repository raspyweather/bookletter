using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Bookletter.Output;

/// <summary>
/// Assembles a sequence of already-composed sheet images into a single output PDF.
/// </summary>
public sealed class PdfSheetWriter : IDisposable
{
    private readonly PdfDocument _doc = new();
    private readonly List<MemoryStream> _streams = new();

    public void AddPage(byte[] pngBytes, double widthPt, double heightPt)
    {
        var page = _doc.AddPage();
        page.Width = XUnit.FromPoint(widthPt);
        page.Height = XUnit.FromPoint(heightPt);

        var ms = new MemoryStream(pngBytes);
        _streams.Add(ms);

        using var gfx = XGraphics.FromPdfPage(page);
        using var image = XImage.FromStream(ms);
        gfx.DrawImage(image, 0, 0, widthPt, heightPt);
    }

    public void Save(string path) => _doc.Save(path);

    public void Dispose()
    {
        foreach (var s in _streams)
            s.Dispose();
        _doc.Dispose();
    }
}
