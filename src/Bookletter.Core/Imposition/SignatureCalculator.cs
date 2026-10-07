namespace Bookletter.Imposition;

/// <summary>
/// Computes saddle-stitch imposition order: splits a document into fixed-size
/// signatures and works out, for every physical sheet in every signature, which
/// source pages land on its front-left/front-right/back-left/back-right.
/// </summary>
public static class SignatureCalculator
{
    public static IReadOnlyList<SheetSpread> Calculate(int totalPages, int signatureSize, PageOrderConvention convention)
    {
        if (totalPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalPages), "Document must have at least one page.");
        if (signatureSize <= 0 || signatureSize % 4 != 0)
            throw new ArgumentOutOfRangeException(nameof(signatureSize), "Signature size must be a positive multiple of 4.");

        var result = new List<SheetSpread>();
        int numSignatures = (int)Math.Ceiling(totalPages / (double)signatureSize);
        int globalSheetIndex = 0;

        for (int sigIndex = 0; sigIndex < numSignatures; sigIndex++)
        {
            int baseOffset = sigIndex * signatureSize;
            int sheetsInSignature = signatureSize / 4;

            for (int s = 0; s < sheetsInSignature; s++)
            {
                int posFrontRight = 1 + 2 * s;
                int posFrontLeft = signatureSize - 2 * s;
                int posBackLeft = 2 + 2 * s;
                int posBackRight = signatureSize - 1 - 2 * s;

                int? Abs(int posInSignature)
                {
                    int abs = baseOffset + posInSignature;
                    return abs <= totalPages ? abs : null;
                }

                int? frontLeft, frontRight, backLeft, backRight;
                if (convention == PageOrderConvention.Standard)
                {
                    frontLeft = Abs(posFrontLeft);
                    frontRight = Abs(posFrontRight);
                    backLeft = Abs(posBackLeft);
                    backRight = Abs(posBackRight);
                }
                else
                {
                    frontLeft = Abs(posFrontRight);
                    frontRight = Abs(posFrontLeft);
                    backLeft = Abs(posBackRight);
                    backRight = Abs(posBackLeft);
                }

                result.Add(new SheetSpread(sigIndex, s, globalSheetIndex++, frontLeft, frontRight, backLeft, backRight));
            }
        }

        return result;
    }
}
