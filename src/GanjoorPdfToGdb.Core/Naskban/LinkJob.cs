using System.Text;
using System.Text.Json;

namespace GanjoorPdfToGdb;

public sealed class LinkOptions
{
    public string GdbPath { get; set; } = "";
    public string SidecarPath { get; set; } = "";
    public int PdfBookId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ApiBaseUrl { get; set; }
    /// <summary>Link a poem to every page it spans (default) or only to its first page.</summary>
    public bool AllPages { get; set; } = true;
    public bool SuggestedByMachine { get; set; }
    public bool IsTextOriginalSource { get; set; } = true;
    /// <summary>Poems whose text is found in the source less than this are not linked (0..1).</summary>
    public double MinScore { get; set; } = 0.5;
    /// <summary>Pause between two API calls.</summary>
    public int DelayMs { get; set; } = 120;
}

public sealed class LinkJobResult
{
    public LinkPlan Plan { get; set; } = new();
    public int Created { get; set; }
    public int AlreadyDone { get; set; }
    public int Failed { get; set; }
    public bool Stopped { get; set; }
    public string ReportPath { get; set; } = "";
}

/// <summary>Matches the site poems with the converter's JSON and sends the link suggestions to Naskban (resumable).</summary>
public static class LinkJob
{
    public static List<string> Validate(LinkOptions o, bool sending)
    {
        var e = new List<string>();
        if (!File.Exists(o.GdbPath)) e.Add("The .gdb file does not exist.");
        if (!File.Exists(o.SidecarPath)) e.Add("The .pages.json file does not exist.");
        if (o.PdfBookId <= 0) e.Add("The Naskban book id must be a positive number.");
        if (sending && (string.IsNullOrWhiteSpace(o.Username) || string.IsNullOrEmpty(o.Password))) e.Add("Naskban user name and password are required.");
        return e;
    }

    /// <summary>Builds the plan only (no network) and writes the report.</summary>
    public static LinkJobResult Preview(LinkOptions o, Action<string> log)
    {
        var errors = Validate(o, sending: false);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));

        log("Reading the gdb file…");
        var site = GdbReader.ReadPoems(o.GdbPath);
        log($"  {site.Count} poems on the site side.");
        log("Reading the JSON file…");
        var src = SidecarData.Load(o.SidecarPath);
        log($"  {src.Poems.Count} poems in the JSON.");
        if (src.PageCount > 0) log($"  The PDF has {src.PageCount} pages.");

        log("Matching poems by text…");
        var plan = PoemMatcher.Match(site, src, o.MinScore);
        if (!o.AllPages)
            foreach (var it in plan.Items) it.Pages = it.Pages.Take(1).ToList();
        if (src.PageCount > 0)
            foreach (var it in plan.Items) it.Pages = it.Pages.Where(p => p >= 1 && p <= src.PageCount).ToList();

        var res = new LinkJobResult { Plan = plan, ReportPath = ReportPathFor(o) };
        log($"  matched {plan.Items.Count} of {site.Count} poems; {plan.TotalLinks} link(s) to send.");
        log($"  not matched: {plan.UnmatchedSitePoems.Count} site poem(s); {plan.UnusedSourcePoems.Count} source poem(s) not used.");
        WriteReport(res, o, null);
        log("Report: " + res.ReportPath);
        return res;
    }

    public static async Task<LinkJobResult> SendAsync(LinkOptions o, Action<string> log, Action<int, int>? progress, CancellationToken ct, LinkJobResult? preview = null)
    {
        var errors = Validate(o, sending: true);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));

        var res = preview ?? await Task.Run(() => Preview(o, log), ct);
        var plan = res.Plan;

        string logPath = DoneLogPath(o);
        var done = LoadDone(logPath);
        var work = plan.Items.SelectMany(i => i.Pages.Select(p => (Item: i, Page: p))).ToList();
        log($"{work.Count} link(s) in total, {work.Count(w => done.Contains(Key(w.Item.Poem.Id, w.Page)))} already sent in an earlier run.");

        using var client = new NaskbanClient(o.Username, o.Password, o.ApiBaseUrl);
        string? loginError = await client.LoginAsync(ct);
        if (loginError != null) throw new InvalidOperationException("Login to Naskban failed: " + loginError);
        log("Logged in" + (client.UserName != null ? " as " + client.UserName : "") + ".");

        var failures = new List<string>();
        int n = 0, consecutiveFailures = 0;
        foreach (var (item, page) in work)
        {
            ct.ThrowIfCancellationRequested();
            n++;
            progress?.Invoke(n, work.Count);
            string key = Key(item.Poem.Id, page);
            if (done.Contains(key)) { res.AlreadyDone++; continue; }

            LinkResult r;
            try
            {
                r = await client.SuggestLinkAsync(item.Poem.Id, item.Poem.RelativeUrl, item.Poem.FullTitle, o.PdfBookId, page,
                    o.IsTextOriginalSource, o.SuggestedByMachine, ct);
            }
            catch (HttpRequestException ex) { r = new LinkResult(LinkOutcome.Failed, ex.Message); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { r = new LinkResult(LinkOutcome.Failed, "timeout"); }

            switch (r.Outcome)
            {
                case LinkOutcome.Created: res.Created++; consecutiveFailures = 0; done.Add(key); AppendDone(logPath, key); break;
                case LinkOutcome.AlreadySuggested: res.AlreadyDone++; consecutiveFailures = 0; done.Add(key); AppendDone(logPath, key); break;
                default:
                    res.Failed++; consecutiveFailures++;
                    string line = $"poem {item.Poem.Id} page {page}: {r.Message}";
                    failures.Add(line);
                    log("  FAILED " + line);
                    break;
            }
            if (n % 25 == 0) log($"  {n}/{work.Count}: sent {res.Created}, already there {res.AlreadyDone}, failed {res.Failed}");
            if (consecutiveFailures >= 8)
            {
                res.Stopped = true;
                log("Stopping after 8 failures in a row (the server or the network may be down). Run again later; finished links are remembered.");
                break;
            }
            if (o.DelayMs > 0) await Task.Delay(o.DelayMs, ct);
        }

        WriteReport(res, o, failures);
        return res;
    }

    // ---- files next to the gdb ----

    private static string ReportPathFor(LinkOptions o) => Path.ChangeExtension(o.GdbPath, null) + $".naskban-{o.PdfBookId}.report.txt";
    private static string DoneLogPath(LinkOptions o) => Path.ChangeExtension(o.GdbPath, null) + $".naskban-{o.PdfBookId}.done.txt";
    private static string Key(int poemId, int page) => poemId + ":" + page;

    private static HashSet<string> LoadDone(string path)
        => File.Exists(path) ? new HashSet<string>(File.ReadAllLines(path).Where(l => l.Length > 0)) : new HashSet<string>();

    private static void AppendDone(string path, string key) => File.AppendAllText(path, key + Environment.NewLine);

    private static void WriteReport(LinkJobResult r, LinkOptions o, List<string>? failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Naskban book id: {o.PdfBookId}");
        sb.AppendLine($"Matched poems: {r.Plan.Items.Count}; links: {r.Plan.TotalLinks}; pages per poem: {(o.AllPages ? "all" : "first only")}");
        if (failures != null)
            sb.AppendLine($"Sent now: {r.Created}; already there: {r.AlreadyDone}; failed: {r.Failed}{(r.Stopped ? "; STOPPED EARLY" : "")}");
        sb.AppendLine();

        var weak = r.Plan.Items.Where(i => i.Score < 0.9 || i.Ambiguous).ToList();
        sb.AppendLine($"Matches worth a look ({weak.Count}): similarity below 90% or several identical source poems");
        foreach (var i in weak)
            sb.AppendLine($"  {i.Poem.Id} «{i.Poem.Title}» ({i.Poem.RelativeUrl}) -> source «{i.SourceTitle}», {i.Score:P0}{(i.Ambiguous ? ", ambiguous" : "")}, pages {string.Join(",", i.Pages)}");
        sb.AppendLine();
        sb.AppendLine($"Site poems with no match ({r.Plan.UnmatchedSitePoems.Count}):");
        foreach (var p in r.Plan.UnmatchedSitePoems) sb.AppendLine($"  {p.Id} «{p.Title}» {p.RelativeUrl}");
        sb.AppendLine();
        sb.AppendLine($"Source poems not matched to any site poem ({r.Plan.UnusedSourcePoems.Count}):");
        foreach (var t in r.Plan.UnusedSourcePoems) sb.AppendLine("  " + t);
        if (failures is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Failed links:");
            foreach (var f in failures) sb.AppendLine("  " + f);
        }
        File.WriteAllText(r.ReportPath, sb.ToString(), new UTF8Encoding(false));
    }
}
