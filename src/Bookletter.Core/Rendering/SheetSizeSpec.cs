using System.Globalization;

namespace Bookletter.Rendering;

/// <summary>
/// The size of the printed sheet (the spread holding two source pages side by side).
/// Either derived automatically from the source PDF's own page size, or a fixed
/// landscape paper size / custom dimension.
/// </summary>
public sealed class SheetSizeSpec
{
    public bool IsAuto { get; private init; }
    public double FixedWidthPt { get; private init; }
    public double FixedHeightPt { get; private init; }

    private static readonly SheetSizeSpec Auto = new() { IsAuto = true };

    // Named paper sizes in points, portrait orientation (width, height); flipped to
    // landscape below since a sheet holds two pages side by side.
    private static readonly Dictionary<string, (double W, double H)> NamedSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a3"] = (841.89, 1190.55),
        ["a4"] = (595.28, 841.89),
        ["a5"] = (419.53, 595.28),
        ["letter"] = (612, 792),
        ["legal"] = (612, 1008),
    };

    public static SheetSizeSpec Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            return Auto;

        spec = spec.Trim();

        if (NamedSizes.TryGetValue(spec, out var named))
        {
            // Portrait -> landscape (the sheet is wider than tall).
            double w = Math.Max(named.W, named.H);
            double h = Math.Min(named.W, named.H);
            return new SheetSizeSpec { FixedWidthPt = w, FixedHeightPt = h };
        }

        // Custom "WIDTHxHEIGHT[unit]", e.g. "420x297mm", "16.5x11.7in", "842x595".
        var parts = spec.Split('x', 'X');
        if (parts.Length != 2)
            throw new FormatException($"Unrecognized sheet size '{spec}'. Use 'auto', a named size (A3/A4/A5/Letter/Legal), or 'WIDTHxHEIGHT' with an optional unit suffix (mm, cm, in, pt).");

        (double value, string unit) ParseNumberWithUnit(string s)
        {
            s = s.Trim();
            int i = s.Length;
            while (i > 0 && !char.IsDigit(s[i - 1]) && s[i - 1] != '.') i--;
            string numberPart = s[..i];
            string unitPart = s[i..].Trim();
            if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"Unrecognized sheet size component '{s}'.");
            return (value, unitPart);
        }

        var (w1, unit1) = ParseNumberWithUnit(parts[0]);
        var (h1, unit2) = ParseNumberWithUnit(parts[1]);
        string unit = !string.IsNullOrEmpty(unit1) ? unit1 : (!string.IsNullOrEmpty(unit2) ? unit2 : "pt");

        double ToPt(double v) => unit.ToLowerInvariant() switch
        {
            "pt" => v,
            "mm" => v / 25.4 * 72.0,
            "cm" => v / 2.54 * 72.0,
            "in" => v * 72.0,
            _ => throw new FormatException($"Unknown unit '{unit}'. Use pt, mm, cm or in.")
        };

        return new SheetSizeSpec { FixedWidthPt = ToPt(w1), FixedHeightPt = ToPt(h1) };
    }

    public (double WidthPt, double HeightPt) Resolve(double autoPageWidthPt, double autoPageHeightPt, double marginPt, double gapPt)
    {
        if (IsAuto)
            return (2 * autoPageWidthPt + gapPt + 2 * marginPt, autoPageHeightPt + 2 * marginPt);

        return (FixedWidthPt, FixedHeightPt);
    }
}
