namespace Bookletter;

/// <summary>
/// Cheap, rasterization-free facts about the current options, used by the GUI to
/// drive its sheet preview (how many sheets exist, how big one is) and its idle status
/// summary (how many pages/signatures) without doing any actual page rendering.
/// </summary>
public sealed record PreviewInfo(int TotalPages, int SignatureCount, int TotalSheets, double SheetWidthPt, double SheetHeightPt);
