using System.Text.Json;

namespace GanjoorPdfToGdb;

/// <summary>One cached model answer for a page (work/pages/0600.json).</summary>
public sealed class PageCacheEntry
{
    public int PdfPage { get; set; }
    public string Model { get; set; } = "";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public string AnalyzedAt { get; set; } = "";
    public PageResult Result { get; set; } = new();
}

public sealed class RunStats
{
    public int PagesFromCache { get; set; }
    public int PagesFromModel { get; set; }
    public int PagesFailed { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
}

/// <summary>
/// Pages are processed strictly in order because each page's context (current section, open poem)
/// comes from the previous one. Every model answer is cached on disk, so an interrupted run resumes
/// where it stopped and "assemble" can rebuild the output without any API calls.
/// </summary>
public sealed class ConversionPipeline
{
    private readonly AppConfig _cfg;
    private readonly IPageRenderer _renderer;
    private readonly OpenAiCompatibleClient? _client;
    private readonly string _workDir;
    private readonly Action<string> _log;

    public RunStats Stats { get; } = new();

    /// <summary>Called after every page: (result, pages done, pages total). Used for progress bars.</summary>
    public Action<PageResult, int, int>? OnPage { get; set; }

    /// <param name="client">null = cache-only mode (no API calls).</param>
    public ConversionPipeline(AppConfig cfg, IPageRenderer renderer, OpenAiCompatibleClient? client, string workDir, Action<string> log)
    {
        _cfg = cfg;
        _renderer = renderer;
        _client = client;
        _workDir = workDir;
        _log = log;
    }

    // prose mode has its own cache: the same page gives a different answer there
    private string PagesDir => Path.Combine(_workDir, _cfg.IncludeProse ? "pages-prose" : "pages");
    private string ImagesDir => Path.Combine(_workDir, "images");

    public async Task<Book> RunAsync(string poetName, IReadOnlyList<int> pages, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PagesDir);
        var assembler = new BookAssembler(_cfg, poetName);

        int done = 0;
        foreach (int page in pages)
        {
            ct.ThrowIfCancellationRequested();
            if (page < 1 || page > _renderer.PageCount)
            {
                assembler.Book.Issues.Add($"page {page} is outside the document (1-{_renderer.PageCount})");
                continue;
            }

            PageResult result = await GetPageResultAsync(page, assembler.Snapshot(), ct);
            assembler.AddPage(result);
            done++;
            OnPage?.Invoke(result, done, pages.Count);
            if (done % 10 == 0 || done == pages.Count)
                _log($"  {done}/{pages.Count} pages (cache {Stats.PagesFromCache}, model {Stats.PagesFromModel}, failed {Stats.PagesFailed})");
        }
        return assembler.Finish();
    }

    private async Task<PageResult> GetPageResultAsync(int page, PageContext ctx, CancellationToken ct)
    {
        string cachePath = Path.Combine(PagesDir, page.ToString("D4") + ".json");
        if (File.Exists(cachePath))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<PageCacheEntry>(File.ReadAllText(cachePath), PageResult.Json);
                if (entry?.Result != null)
                {
                    entry.Result.PdfPage = page;
                    Stats.PagesFromCache++;
                    return entry.Result;
                }
            }
            catch (JsonException) { /* corrupt cache file: fall through and ask the model again */ }
        }

        if (_client == null)
        {
            Stats.PagesFailed++;
            return new PageResult { PdfPage = page, PageKind = "error", Notes = "no cached result and no API access (assemble mode)" };
        }

        string image = await _renderer.GetPageImageAsync(page, ImagesDir, ct);
        byte[] png = await File.ReadAllBytesAsync(image, ct);
        string userText = PagePrompts.BuildUserText(page, ctx);

        string lastError = "unknown";
        for (int attempt = 0; attempt <= 1; attempt++) // one extra try if the JSON is unusable
        {
            try
            {
                ChatResult chat = await _client.CompleteAsync(PagePrompts.For(_cfg), userText, png, ct);
                Stats.PromptTokens += chat.PromptTokens;
                Stats.CompletionTokens += chat.CompletionTokens;

                PageResult? parsed = ParsePage(chat.Content, out lastError);
                if (parsed == null) continue;

                parsed.PdfPage = page;
                var entry = new PageCacheEntry
                {
                    PdfPage = page,
                    Model = _cfg.Model,
                    PromptTokens = chat.PromptTokens,
                    CompletionTokens = chat.CompletionTokens,
                    AnalyzedAt = DateTime.UtcNow.ToString("o"),
                    Result = parsed
                };
                await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(entry, PageResult.Json), ct);
                Stats.PagesFromModel++;
                return parsed;
            }
            catch (InvalidOperationException ex) // retries exhausted inside the client
            {
                lastError = ex.Message;
                break;
            }
        }

        Stats.PagesFailed++;
        _log($"  page {page} failed: {lastError}");
        return new PageResult { PdfPage = page, PageKind = "error", Notes = lastError };
    }

    private static readonly HashSet<string> Kinds = new() { "poetry", "prose", "contents", "index", "title", "blank", "other" };

    public static PageResult? ParsePage(string content, out string error)
    {
        error = "";
        string json = ExtractJsonObject(content);
        if (json.Length == 0) { error = "response contained no JSON object"; return null; }
        try
        {
            var r = JsonSerializer.Deserialize<PageResult>(json, PageResult.Json);
            if (r == null) { error = "empty JSON"; return null; }
            r.Blocks ??= new List<Block>();
            r.PageKind = (r.PageKind ?? "other").Trim().ToLowerInvariant();
            if (!Kinds.Contains(r.PageKind)) r.PageKind = "other";
            foreach (var b in r.Blocks) b.Type = (b.Type ?? "").Trim().ToLowerInvariant();
            return r;
        }
        catch (JsonException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return null;
        }
    }

    /// <summary>Strips markdown fences / chatter and returns the outermost {...}.</summary>
    public static string ExtractJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        int a = content.IndexOf('{');
        int b = content.LastIndexOf('}');
        return a >= 0 && b > a ? content[a..(b + 1)] : "";
    }
}
