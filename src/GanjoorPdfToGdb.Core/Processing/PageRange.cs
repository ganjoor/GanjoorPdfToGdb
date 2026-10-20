using System.Text;

namespace GanjoorPdfToGdb;

public static class PageRange
{
    /// <summary>
    /// Accepts Persian/Arabic digits, Persian separators (، ؛ –) and the words «همه»/«all».
    /// "74-1130" or "۷۴–۱۱۳۰" or "9,76,600-601" → pages.
    /// </summary>
    public static string Normalize(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return "all";
        var sb = new StringBuilder();
        foreach (char c in spec.Trim())
        {
            if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
            else if (c == '،' || c == '؛' || c == ';') sb.Append(',');
            else if (c == '–' || c == '—' || c == '‑' || c == '−') sb.Append('-');
            else sb.Append(c);
        }
        string s = sb.ToString().Trim();
        if (s.Equals("all", StringComparison.OrdinalIgnoreCase) || s == "همه") return "all";
        return s;
    }

    /// <summary>"all", "74-1130", "9,76,600-601" → sorted distinct 1-based page numbers.</summary>
    public static List<int> Parse(string? spec, int pageCount)
    {
        string s = Normalize(spec);
        if (s == "all")
            return Enumerable.Range(1, pageCount).ToList();

        var set = new SortedSet<int>();
        foreach (string part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length == 1 && int.TryParse(ends[0], out int single)) set.Add(single);
            else if (ends.Length == 2 && int.TryParse(ends[0], out int a) && int.TryParse(ends[1], out int b) && a <= b)
                for (int p = a; p <= b; p++) set.Add(p);
            else throw new FormatException($"Bad page range «{part}». Use e.g. 74-1130 or 9,76,600-601.");
        }
        if (set.Count == 0) throw new FormatException("The page range is empty.");
        return set.ToList();
    }
}
