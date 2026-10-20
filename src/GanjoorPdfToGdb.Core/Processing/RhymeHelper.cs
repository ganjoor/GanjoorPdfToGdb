namespace GanjoorPdfToGdb;

public sealed record RhymeInfo(string Suffix, double Support, int Rows)
{
    /// <summary>A one-letter suffix says very little (many words end in ا or ی).</summary>
    public bool Weak => Suffix.Length < 2;
}

/// <summary>
/// Simple text-based rhyme detection: the longest common ending of the last word of the second
/// hemistichs. It is only used to decide things like "is this centered couplet a normal bayt?" and
/// to flag lines worth proofreading, so a deliberately plain method is enough.
/// </summary>
public static class RhymeHelper
{
    private const int MaxSuffix = 10;

    /// <summary>Letters of the last word, without diacritics, punctuation or ZWNJ.</summary>
    public static string LastWordKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string[] words = text.Split(new[] { ' ', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = words.Length - 1; i >= 0; i--)
        {
            // آ and ا count as the same rhyme letter (e.g. «آلست» / «الست»).
            string k = TextNormalizer.LooseKey(words[i]).Replace('آ', 'ا');
            if (k.Length > 0) return k;
        }
        return "";
    }

    public static RhymeInfo? FindRhyme(IEnumerable<string> secondHemistichs, double minSupport = 0.6)
    {
        var keys = secondHemistichs.Select(LastWordKey).Where(k => k.Length > 0).ToList();
        if (keys.Count < 3) return null;
        int need = (int)Math.Ceiling(keys.Count * minSupport);

        for (int k = MaxSuffix; k >= 1; k--)
        {
            var best = keys.Where(t => t.Length >= k)
                           .GroupBy(t => t[^k..])
                           .Select(g => (Suffix: g.Key, Count: g.Count()))
                           .OrderByDescending(x => x.Count)
                           .FirstOrDefault();
            if (best.Count >= need && best.Suffix != null)
                return new RhymeInfo(best.Suffix, (double)best.Count / keys.Count, keys.Count);
        }
        return null;
    }

    /// <summary>Number of identical letters at the end of the last words of two lines.</summary>
    public static int CommonEnding(string? a, string? b)
    {
        string x = LastWordKey(a), y = LastWordKey(b);
        int n = 0;
        while (n < x.Length && n < y.Length && x[x.Length - 1 - n] == y[y.Length - 1 - n]) n++;
        return n;
    }

    public static bool EndsWithRhyme(string? text, RhymeInfo rhyme)
        => LastWordKey(text).EndsWith(rhyme.Suffix, StringComparison.Ordinal);
}
