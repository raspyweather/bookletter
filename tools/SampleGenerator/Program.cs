// Generates a numbered, color-coded sample PDF for exercising Bookletter's imposition
// logic. Each page shows a large page number so imposed sheets can be checked visually.
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

GlobalFontSettings.FontResolver = new SimpleFontResolver();

int pageCount = args.Length > 0 ? int.Parse(args[0]) : 32;
string outPath = args.Length > 1 ? args[1] : "sample.pdf";
double pageWidthMm = args.Length > 2 ? double.Parse(args[2]) : 148;
double pageHeightMm = args.Length > 3 ? double.Parse(args[3]) : 210;

var doc = new PdfDocument();
var font = new XFont("Arial", 48);

for (int i = 1; i <= pageCount; i++)
{
    var page = doc.AddPage();
    page.Width = XUnit.FromMillimeter(pageWidthMm);
    page.Height = XUnit.FromMillimeter(pageHeightMm);
    using var gfx = XGraphics.FromPdfPage(page);

    var hue = (i * 37) % 360;
    gfx.DrawRectangle(new XSolidBrush(HsvToColor(hue)), 0, 0, page.Width, page.Height);
    gfx.DrawString(i.ToString(), font, XBrushes.Black,
        new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
}

doc.Save(outPath);
Console.WriteLine($"Wrote {outPath} with {pageCount} pages");

static XColor HsvToColor(double h)
{
    double s = 0.35, v = 0.95;
    double c = v * s, x = c * (1 - Math.Abs((h / 60.0 % 2) - 1)), m = v - c;
    double r, g, b;
    if (h < 60) { r = c; g = x; b = 0; }
    else if (h < 120) { r = x; g = c; b = 0; }
    else if (h < 180) { r = 0; g = c; b = x; }
    else if (h < 240) { r = 0; g = x; b = c; }
    else if (h < 300) { r = x; g = 0; b = c; }
    else { r = c; g = 0; b = x; }
    return XColor.FromArgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
}

class SimpleFontResolver : IFontResolver
{
    public byte[] GetFont(string faceName) => File.ReadAllBytes(@"C:\Windows\Fonts\arial.ttf");
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("arial");
}
