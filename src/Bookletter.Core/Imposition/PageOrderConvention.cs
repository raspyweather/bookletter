namespace Bookletter.Imposition;

/// <summary>
/// Controls which of each imposed pair is placed on the left vs. the right of a sheet.
/// </summary>
public enum PageOrderConvention
{
    /// <summary>
    /// Physical saddle-stitch convention: on the outermost sheet of a signature the
    /// higher page number sits on the left and the lower one on the right (so page 1
    /// ends up on the right once the sheet is folded into the book).
    /// </summary>
    Standard,

    /// <summary>
    /// Left/right swapped relative to <see cref="Standard"/> on every pair (useful for
    /// right-to-left books, or to match a printer's duplex flip direction).
    /// </summary>
    Reversed
}
