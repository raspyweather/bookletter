using PDFtoImage;
using SkiaSharp;

namespace Bookletter.Rendering;

/// <summary>
/// Rasterizes pages of a source PDF into bitmaps at a chosen DPI, using PDFium
/// (via PDFtoImage) under the hood. No Ghostscript or ImageMagick involved.
/// </summary>
public sealed class PdfRasterizer
{
    private readonly byte[] _pdfBytes;

    public PdfRasterizer(string pdfPath)
    {
        _pdfBytes = File.ReadAllBytes(pdfPath);
    }

    public int PageCount => Conversion.GetPageCount(_pdfBytes);

    /// <summary>Native page size in points (1/72 inch), as reported by PDFium.</summary>
    public (double WidthPt, double HeightPt) GetPageSizePt(int pageIndex)
    {
        var size = Conversion.GetPageSize(_pdfBytes, pageIndex);
        return (size.Width, size.Height);
    }

    public SKBitmap RenderPage(int pageIndex, int dpi)
    {
        var options = new RenderOptions(Dpi: dpi, WithAnnotations: true);
        return Conversion.ToImage(_pdfBytes, pageIndex, options: options);
    }
}
