using SkiaSharp;

namespace Bookletter.Rendering;

/// <summary>
/// Composes two source-page bitmaps (or blanks) side by side onto a single sheet
/// bitmap, ready to become one output page.
/// </summary>
public sealed class SheetComposer
{
    private readonly double _dpi;
    private readonly double _marginPt;
    private readonly double _gapPt;
    private readonly SKColor _background;
    private readonly bool _allowUpscale;
    private readonly CutMarkOptions _cutMarks;

    public SheetComposer(double dpi, double marginPt, double gapPt, SKColor background, bool allowUpscale, CutMarkOptions cutMarks)
    {
        _dpi = dpi;
        _marginPt = marginPt;
        _gapPt = gapPt;
        _background = background;
        _allowUpscale = allowUpscale;
        _cutMarks = cutMarks;
    }

    /// <param name="nativePageWidthPt">Reference source page size, used to position cut marks on blank pages that have no rendered bitmap.</param>
    /// <param name="leftPageNumber">Source page number for the left cell, used only to label the cut-marks diagnostic log.</param>
    /// <param name="rightPageNumber">Source page number for the right cell, used only to label the cut-marks diagnostic log.</param>
    public SKBitmap ComposeSheet(double sheetWidthPt, double sheetHeightPt, SKBitmap? leftPage, SKBitmap? rightPage, double nativePageWidthPt, double nativePageHeightPt, int? leftPageNumber = null, int? rightPageNumber = null)
    {
        int pxW = ToPx(sheetWidthPt);
        int pxH = ToPx(sheetHeightPt);

        var bmp = new SKBitmap(pxW, pxH, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(_background);

        double marginPx = _marginPt / 72.0 * _dpi;
        double gapPx = _gapPt / 72.0 * _dpi;
        double cellW = (pxW - 2 * marginPx - gapPx) / 2.0;
        double cellH = pxH - 2 * marginPx;
        double nativeWpx = nativePageWidthPt / 72.0 * _dpi;
        double nativeHpx = nativePageHeightPt / 72.0 * _dpi;

        // The overshoot fill bleeds all the way to the sheet's own edges (and to the
        // midpoint of the inter-page gap), not just to the cell boundary - otherwise,
        // whenever margin/gap is smaller than the cut-mark gap+length, the fill would
        // stop short of the marks themselves. Each half exactly tiles its half of the
        // full sheet, split down the middle of the gap.
        double midX = marginPx + cellW + gapPx / 2.0;
        var leftFillRect = SKRect.Create(0, 0, (float)midX, pxH);
        var rightFillRect = SKRect.Create((float)midX, 0, (float)(pxW - midX), pxH);

        ComposeCell(canvas, leftPage, marginPx, marginPx, cellW, cellH, nativeWpx, nativeHpx, leftPageNumber, leftFillRect);
        ComposeCell(canvas, rightPage, marginPx + cellW + gapPx, marginPx, cellW, cellH, nativeWpx, nativeHpx, rightPageNumber, rightFillRect);

        return bmp;
    }

    private int ToPx(double pt) => (int)Math.Round(pt / 72.0 * _dpi);

    private void ComposeCell(SKCanvas canvas, SKBitmap? image, double cellX, double cellY, double cellW, double cellH, double nativeWpx, double nativeHpx, int? pageNumber, SKRect fillRect)
    {
        if (cellW <= 0 || cellH <= 0)
            return;

        double sourceW = image?.Width ?? nativeWpx;
        double sourceH = image?.Height ?? nativeHpx;
        double maxScale = _allowUpscale ? double.PositiveInfinity : 1.0;
        double scale = Math.Min(maxScale, Math.Min(cellW / sourceW, cellH / sourceH));
        double dw = sourceW * scale;
        double dh = sourceH * scale;
        double dx = cellX + (cellW - dw) / 2.0;
        double dy = cellY + (cellH - dh) / 2.0;
        var rect = SKRect.Create((float)dx, (float)dy, (float)dw, (float)dh);

        if (_cutMarks.Enabled && image is not null)
        {
            // Assume a (near-)solid page background. Sample it a little way in from the
            // corner (not the corner pixel itself, which is prone to edge anti-aliasing
            // and compression artifacts) and average a small block for stability, then
            // extend it all the way to the sheet's edge on this page's half, so a
            // slightly imprecise trim doesn't reveal a white gap.
            var pageBackground = SampleBackgroundColor(image);
            Console.WriteLine($"  cut-marks: page {(pageNumber?.ToString() ?? "?")} background sampled as #{pageBackground.Red:X2}{pageBackground.Green:X2}{pageBackground.Blue:X2}");
            using var fillPaint = new SKPaint { Color = pageBackground, Style = SKPaintStyle.Fill };
            canvas.DrawRect(fillRect, fillPaint);
        }

        if (image is not null)
            canvas.DrawBitmap(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        if (_cutMarks.Enabled)
            DrawCutMarks(canvas, rect);
    }

    /// <summary>
    /// Estimates the page's background color by averaging a small block of pixels
    /// inset from the top-left corner, rather than trusting a single corner pixel.
    /// </summary>
    private static SKColor SampleBackgroundColor(SKBitmap image)
    {
        int w = image.Width, h = image.Height;
        int minDim = Math.Min(w, h);
        int insetX = Math.Min(Math.Clamp((int)Math.Round(minDim * 0.02), 3, 40), w - 1);
        int insetY = Math.Min(Math.Clamp((int)Math.Round(minDim * 0.02), 3, 40), h - 1);
        int boxRadius = Math.Clamp((int)Math.Round(minDim * 0.01), 2, 20);

        long rSum = 0, gSum = 0, bSum = 0;
        int count = 0;
        for (int dy = -boxRadius; dy <= boxRadius; dy++)
        {
            int y = Math.Clamp(insetY + dy, 0, h - 1);
            for (int dx = -boxRadius; dx <= boxRadius; dx++)
            {
                int x = Math.Clamp(insetX + dx, 0, w - 1);
                var px = image.GetPixel(x, y);
                rSum += px.Red;
                gSum += px.Green;
                bSum += px.Blue;
                count++;
            }
        }

        return new SKColor((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
    }

    private void DrawCutMarks(SKCanvas canvas, SKRect rect)
    {
        double gap = _cutMarks.GapPt / 72.0 * _dpi;
        double len = _cutMarks.LengthPt / 72.0 * _dpi;
        float stroke = (float)Math.Max(_cutMarks.StrokePt / 72.0 * _dpi, 1.0);

        using var paint = new SKPaint
        {
            Color = _cutMarks.Color,
            StrokeWidth = stroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        void Corner(float cx, float cy, int dirX, int dirY)
        {
            canvas.DrawLine(cx + dirX * (float)gap, cy, cx + dirX * (float)(gap + len), cy, paint);
            canvas.DrawLine(cx, cy + dirY * (float)gap, cx, cy + dirY * (float)(gap + len), paint);
        }

        Corner(rect.Left, rect.Top, -1, -1);
        Corner(rect.Right, rect.Top, 1, -1);
        Corner(rect.Left, rect.Bottom, -1, 1);
        Corner(rect.Right, rect.Bottom, 1, 1);
    }
}
