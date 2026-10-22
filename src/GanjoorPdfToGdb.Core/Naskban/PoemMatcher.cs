using System.Text;
using System.Text.Json;

namespace GanjoorPdfToGdb;

/// <summary>A poem of the converter's JSON file (only what the matcher needs).</summary>
public sealed class SidecarPoem
{
    public string Title { get; set; } = "";
    public List<(string Text, int Page)> Verses { get; } = new();
}

public sealed class SidecarData
{
    public string? SourceFile { get; set; }
    public int PageCount { get; set; }
    public List<SidecarPoem> Poems { get; } = new();

    public static SidecarData Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var d = new SidecarData();
        if (root.TryGetProperty("source", out var src))
        {
            if (src.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String) d.SourceFile = f.GetString();
            if (src.TryGetProperty("pageCount", out var pc) && pc.TryGetInt32(out int n)) d.PageCount = n;
        }
        if (!root.TryGetProperty("categories", out var cats)) throw new InvalidDataException("This is not a GanjoorPdfToGdb .pages.json file (no \"categories\").");
        foreach (var c in cats.EnumerateArray())
        {
            if (!c.TryGetProperty("poems", out var ps)) continue;
            foreach (var p in ps.EnumerateArray())
            {
                var sp = new SidecarPoem { Title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "" };
                if (p.TryGetProperty("verses", out var vs))
                    foreach (var v in vs.EnumerateArray())
                        sp.Verses.Add((v.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "",
                                       v.TryGetProperty("pdfPage", out var pg) && pg.TryGetInt32(out int pn) ? pn : 0));
                d.Poems.Add(sp);
            }
        }
        return d;
    }

    /// <summary>The Naskban book id when the PDF file name starts with it (e.g. 00024017-soha.pdf), else null.</summary>
    public int? GuessBookId()
    {
        if (string.IsNullOrEmpty(SourceFile)) return null;
        int i = 0;
        while (i < SourceFile.Length && char.IsAsciiDigit(SourceFile[i])) i++;
        return i > 0 && i < SourceFile.Length && SourceFile[i] == '-' && int.TryParse(SourceFile[..i], out int id) && id > 0 ? id : null;
    }
}

public sealed class LinkPlanItem
{
    public SitePoem Poem { get; set; } = null!;
    public List<int> Pages { get; set; } = new();
    /// <summary>Share of the site poem's text found in the matched source poem (0..1).</summary>
    public double Score { get; set; }
    public string SourceTitle { get; set; } = "";
    public bool Ambiguous { get; set; }
}

public sealed class LinkPlan
{
    public List<LinkPlanItem> Items { get; } = new();
    public List<SitePoem> UnmatchedSitePoems { get; } = new();
    public List<string> UnusedSourcePoems { get; } = new();
    public int TotalLinks => Items.Sum(i => i.Pages.Count);
}

/// <summary>
/// Finds, for every poem of the published (and possibly hand-corrected) .gdb, the poem of the converter's JSON it came
/// from, by text similarity - the ids differ, titles and categories were edited on the site, a few poems were merged or
/// split. Pages come from the verse-level page numbers of the JSON, so a poem is linked to exactly the pages it spans.
/// </summary>
public static class PoemMatcher
{
    public static LinkPlan Match(IReadOnlyList<SitePoem> site, SidecarData src, double minScore = 0.5)
    {
        var plan = new LinkPlan();

        // source poems: word lists per verse and shingle sets
        var sNorm = src.Poems.Select(p => p.Verses.Select(v => Words(v.Text)).ToList()).ToList();
        var sSet = sNorm.Select(Shingles).ToList();
        var index = new Dictionary<ulong, List<int>>();
        for (int i = 0; i < sSet.Count; i++)
            foreach (ulong h in sSet[i])
            {
                if (!index.TryGetValue(h, out var l)) index[h] = l = new List<int>();
                l.Add(i);
            }

        var used = new HashSet<int>();
        int lastMatched = -1;

        foreach (var poem in site)
        {
            var vWords = poem.Verses.Select(v => Words(v.Text)).ToList();
            var set = Shingles(vWords);
            if (set.Count == 0) { plan.UnmatchedSitePoems.Add(poem); continue; }

            var counts = new Dictionary<int, int>();
            foreach (ulong h in set)
                if (index.TryGetValue(h, out var l) && l.Count <= 300)
                    foreach (int i in l) counts[i] = counts.GetValueOrDefault(i) + 1;

            var cands = counts.OrderByDescending(kv => kv.Value).Take(8)
                .Select(kv =>
                {
                    int inter = set.Count(h => sSet[kv.Key].Contains(h));
                    double contain = (double)inter / set.Count;
                    double jac = (double)inter / (set.Count + sSet[kv.Key].Count - inter);
                    return (Idx: kv.Key, Contain: contain, Jac: jac);
                })
                .OrderByDescending(c => Math.Round(c.Contain, 3)).ThenByDescending(c => c.Jac).ToList();

            if (cands.Count == 0 || cands[0].Contain < minScore) { plan.UnmatchedSitePoems.Add(poem); continue; }

            // identical texts (a repeated refrain poem): take the unused one nearest to the previous match
            var top = cands.Where(c => Math.Abs(c.Contain - cands[0].Contain) < 0.001 && Math.Abs(c.Jac - cands[0].Jac) < 0.001).ToList();
            var pick = top[0];
            bool ambiguous = top.Count > 1;
            if (ambiguous)
            {
                pick = top.OrderBy(c => used.Contains(c.Idx) ? 1 : 0)
                          .ThenBy(c => c.Idx >= lastMatched ? 0 : 1)
                          .ThenBy(c => Math.Abs(c.Idx - (lastMatched + 1))).First();
            }
            used.Add(pick.Idx);
            lastMatched = pick.Idx;

            var item = new LinkPlanItem
            {
                Poem = poem,
                Score = pick.Contain,
                SourceTitle = src.Poems[pick.Idx].Title,
                Ambiguous = ambiguous,
                Pages = PagesOf(vWords, View(sNorm[pick.Idx], src.Poems[pick.Idx]), out var missing)
            };
            // verses that are not in the best source poem: the site poem may merge two source poems (e.g. a poem
            // the model split at a page break), so look in the other candidates too
            if (missing.Count > 0)
            {
                foreach (var other in cands.Where(c => c.Idx != pick.Idx && !used.Contains(c.Idx)).Take(3))
                {
                    var ov = View(sNorm[other.Idx], src.Poems[other.Idx]);
                    var extra = new SortedSet<int>();
                    var still = new List<string>();
                    foreach (string key in missing)
                    {
                        int j = Find(ov, key, 0);
                        if (j >= 0) extra.Add(ov.Poem.Verses[j].Page); else still.Add(key);
                    }
                    if (extra.Count > 0 && missing.Count - still.Count >= 2)
                    {
                        used.Add(other.Idx);
                        item.Pages = item.Pages.Concat(extra).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
                        missing = still;
                    }
                }
            }
            if (item.Pages.Count == 0) { plan.UnmatchedSitePoems.Add(poem); continue; }
            plan.Items.Add(item);
        }

        for (int i = 0; i < src.Poems.Count; i++)
            if (!used.Contains(i)) plan.UnusedSourcePoems.Add($"{src.Poems[i].Title} (p.{src.Poems[i].Verses.FirstOrDefault().Page})");
        return plan;
    }

    // ---- pages: align the site verses to the source verses ----

    private sealed class SrcView
    {
        public SidecarPoem Poem = null!;
        public List<string> Keys = new();
        public Dictionary<string, List<int>> Exact = new();
    }

    private static SrcView View(List<string[]> words, SidecarPoem sp)
    {
        var v = new SrcView { Poem = sp, Keys = words.Select(w => string.Join(' ', w)).ToList() };
        for (int i = 0; i < v.Keys.Count; i++)
        {
            if (v.Keys[i].Length == 0) continue;
            if (!v.Exact.TryGetValue(v.Keys[i], out var l)) v.Exact[v.Keys[i]] = l = new List<int>();
            l.Add(i);
        }
        return v;
    }

    private static int Find(SrcView v, string key, int from)
    {
        if (v.Exact.TryGetValue(key, out var l)) return l.FirstOrDefault(x => x >= from, l[0]);
        int j = -1;
        double best = 0.74;
        for (int i = 0; i < v.Keys.Count; i++)
        {
            if (v.Keys[i].Length == 0 || Math.Abs(v.Keys[i].Length - key.Length) > key.Length * 0.3 + 3) continue;
            double sim = Similarity(key, v.Keys[i]) - (i < from ? 0.02 : 0); // prefer going forward
            if (sim > best) { best = sim; j = i; }
        }
        return j;
    }

    /// <summary>Pages of the site verses found in the source poem; the verses that were not found are returned in <paramref name="missing"/>.</summary>
    private static List<int> PagesOf(List<string[]> siteVerses, SrcView v, out List<string> missing)
    {
        missing = new List<string>();
        var pages = new SortedSet<int>();
        int p = 0;
        foreach (var w in siteVerses)
        {
            string key = string.Join(' ', w);
            if (key.Length == 0) continue;
            int j = Find(v, key, p);
            if (j >= 0) { pages.Add(v.Poem.Verses[j].Page); p = j + 1; }
            else missing.Add(key);
        }
        pages.Remove(0);
        return pages.ToList();
    }

    private static double Similarity(string a, string b)
    {
        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[m] / Math.Max(n, m);
    }

    // ---- text normalization ----

    public static string[] Words(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            char c = ch;
            if (c == '‌' || c == '‍' || c == 'ـ' || (c >= 'ً' && c <= 'ٟ') || c == 'ٰ') continue; // ZWNJ, tatweel, diacritics
            c = c switch { 'ي' => 'ی', 'ى' => 'ی', 'ك' => 'ک', 'ۀ' => 'ه', 'ة' => 'ه', 'آ' => 'ا', 'أ' => 'ا', 'إ' => 'ا', 'ؤ' => 'و', 'ئ' => 'ی', _ => c };
            if (c >= '۰' && c <= '۹') c = (char)('0' + (c - '۰'));
            else if (c >= '٠' && c <= '٩') c = (char)('0' + (c - '٠'));
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static HashSet<ulong> Shingles(List<string[]> verses)
    {
        var set = new HashSet<ulong>();
        foreach (var w in verses)
        {
            if (w.Length == 1) set.Add(Hash(w[0], ""));
            for (int i = 0; i + 1 < w.Length; i++) set.Add(Hash(w[i], w[i + 1]));
        }
        return set;
    }

    private static ulong Hash(string a, string b)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in a) { h ^= c; h *= 1099511628211UL; }
        h ^= 0x20; h *= 1099511628211UL;
        foreach (char c in b) { h ^= c; h *= 1099511628211UL; }
        return h;
    }
}
