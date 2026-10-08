namespace Bookletter.Imposition;

/// <summary>
/// Parses a page-selector string like a print dialog's "Pages" field - comma-separated
/// single pages and ascending ranges, 1-based, e.g. "3-20,25,30-35" - into an ordered
/// list of 0-indexed source page numbers. A blank spec means every page. An
/// open-ended range ("13-") means "page 13 through the last page", which is what
/// replaces the old, separate --skip-pages option: "--pages 13-" skips the first 12
/// pages without needing to know the document's total page count.
/// </summary>
public static class PageSelector
{
    public static IReadOnlyList<int> Parse(string? spec, int sourcePageCount)
    {
        if (sourcePageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourcePageCount), "Document must have at least one page.");

        if (string.IsNullOrWhiteSpace(spec))
            return Enumerable.Range(0, sourcePageCount).ToArray();

        var result = new List<int>();
        foreach (string token in spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int dash = token.IndexOf('-');
            int start, end;
            if (dash < 0)
            {
                start = end = ParsePageNumber(token, spec, sourcePageCount);
            }
            else
            {
                string startPart = token[..dash].Trim();
                string endPart = token[(dash + 1)..].Trim();
                start = startPart.Length == 0 ? 1 : ParsePageNumber(startPart, spec, sourcePageCount);
                end = endPart.Length == 0 ? sourcePageCount : ParsePageNumber(endPart, spec, sourcePageCount);
                if (start > end)
                {
                    throw new ArgumentException(
                        $"Invalid page range '{token}' in --pages \"{spec}\": {start} is after {end}. " +
                        $"Ranges must be ascending (e.g. {end}-{start}).");
                }
            }

            for (int p = start; p <= end; p++)
                result.Add(p - 1);
        }

        if (result.Count == 0)
            throw new ArgumentException($"--pages \"{spec}\" didn't select any pages.");

        return result;
    }

    private static int ParsePageNumber(string token, string spec, int sourcePageCount)
    {
        if (!int.TryParse(token, out int page) || page < 1)
        {
            throw new ArgumentException(
                $"Invalid page number '{token}' in --pages \"{spec}\". Use a 1-based page number, a range " +
                "like 3-20, or an open-ended range like 5- (page 5 through the end).");
        }
        if (page > sourcePageCount)
            throw new ArgumentException($"Page {page} in --pages \"{spec}\" is beyond the source document's {sourcePageCount} page(s).");
        return page;
    }
}
