namespace Bookletter.Imposition;

/// <summary>
/// One physical sheet of paper within a signature. Each side (front/back) carries two
/// source page numbers (1-indexed, absolute across the whole document). A null page
/// number means that slot is beyond the last real page and should be printed blank.
/// </summary>
public sealed record SheetSpread(
    int SignatureIndex,
    int SheetIndexInSignature,
    int GlobalSheetIndex,
    int? FrontLeftPage,
    int? FrontRightPage,
    int? BackLeftPage,
    int? BackRightPage);
