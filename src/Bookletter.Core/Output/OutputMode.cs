namespace Bookletter.Output;

[Flags]
public enum OutputMode
{
    None = 0,
    /// <summary>booklet-front.pdf + booklet-back.pdf, for manual duplex printing.</summary>
    FrontBack = 1,
    /// <summary>booklet-duplex.pdf, front/back interleaved for auto-duplex printers.</summary>
    Duplex = 2,
    /// <summary>One image file per sheet side.</summary>
    Images = 4,
    All = FrontBack | Duplex | Images
}
