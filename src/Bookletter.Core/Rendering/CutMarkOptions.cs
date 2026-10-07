using SkiaSharp;

namespace Bookletter.Rendering;

/// <summary>
/// Trim/crop mark settings, drawn at the true corners of each source page once placed
/// on its sheet. <see cref="GapPt"/> of 0 gives "simple" marks that touch the trim
/// corner directly; a non-zero gap gives the standard print-shop "overshoot" style,
/// where marks are offset from the corner so none of the mark itself falls in the
/// bleed/trim zone.
/// </summary>
public sealed record CutMarkOptions(bool Enabled, double GapPt, double LengthPt, double StrokePt, SKColor Color)
{
    public static readonly CutMarkOptions Disabled = new(false, 0, 0, 0, SKColors.Black);
}
