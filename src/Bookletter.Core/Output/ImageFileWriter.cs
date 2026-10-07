using SkiaSharp;

namespace Bookletter.Output;

public static class ImageFileWriter
{
    public static void Write(SKBitmap bitmap, string path, string imageFormat, int jpegQuality)
    {
        bool jpeg = imageFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                 || imageFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase);

        var format = jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
        int quality = jpeg ? jpegQuality : 100;

        using var fs = File.Create(path);
        bitmap.Encode(fs, format, quality);
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
