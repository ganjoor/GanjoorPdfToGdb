using System.Text;

namespace GanjoorPdfToGdb;

public enum JobMode
{
    /// <summary>Use cached page results and call the model for pages that are not cached.</summary>
    Run,
    /// <summary>Only use cached page results; never call the model.</summary>
    Assemble
}

public sealed class JobOptions
{
    public string? PdfPath { get; set; }
    /// <summary>Folder of page images; used instead of the PDF when set.</summary>
    public string? ImagesDir { get; set; }
    public string PoetName { get; set; } = "";
    public string OutDir { get; set; } = "";
    /// <summary>all | 74-1130 | 9,76,600-601</summary>
    public string? PagesSpec { get; set; }
    public JobMode Mode { get; set; } = JobMode.Run;
    /// <summary>Process only the first N pages of the selection (for a quick trial).</summary>
    public int? MaxPages { get; set; }
    /// <summary>Folder with pdftoppm/pdfinfo; null = look next to the application, then PATH.</summary>
    public string? PopplerDir { get; set; }
    /// <summary>Output file name without extension (default: the poet name).</summary>
    public string? FileStem { get; set; }
}

public sealed class JobResult
{
    public string GdbPath { get; set; } = "";
    public string SidecarPath { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public int Categories { get; set; }
    public int Poems { get; set; }
    public int VerseRows { get; set; }
    public int PagesProcessed { get; set; }
    public int PoetryPages { get; set; }
    public List<string> Issues { get; set; } = new();
    public RunStats Stats { get; set; } = new();
    public bool HasFailedPages => Stats.PagesFailed > 0;
}

/// <summary>
/// One conversion from start to finish. The command line tool and the Windows Forms app both use it,
/// so they behave identically.
/// </summary>
public sealed class ConversionJob
{
    private readonly AppConfig _cfg;
    private readonly JobOptions _opt;
    private readonly Action<string> _log;
    private readonly Action<int, int>? _progress;

    public ConversionJob(AppConfig cfg, JobOptions options, Action<string> log, Action<int, int>? progress = null)
    {
        _cfg = cfg;
        _opt = options;
        _log = log;
        _progress = progress;
    }

    /// <summary>Checks the inputs and returns a list of problems (empty = ok). Cheap, no network.</summary>
    public static List<string> Validate(AppConfig cfg, JobOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.PoetName)) errors.Add("Poet name is empty.");
        if (string.IsNullOrWhiteSpace(o.OutDir)) errors.Add("Output folder is empty.");
        if (!string.IsNullOrWhiteSpace(o.ImagesDir))
        {
            if (!Directory.Exists(o.ImagesDir)) errors.Add("The images folder does not exist.");
        }
        else if (string.IsNullOrWhiteSpace(o.PdfPath) || !File.Exists(o.PdfPath))
            errors.Add("The PDF file does not exist.");

        if (o.Mode == JobMode.Run)
        {
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl)) errors.Add("Service address (base URL) is empty.");
            else if (!Uri.TryCreate(cfg.BaseUrl, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
                errors.Add("Service address (base URL) is not a valid http(s) address.");
            if (string.IsNullOrWhiteSpace(cfg.Model)) errors.Add("Model name is empty.");
        }
        if (!string.IsNullOrWhiteSpace(o.PagesSpec) && !o.PagesSpec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            try { PageRange.Parse(o.PagesSpec, 1); } // syntax check only; explicit ranges do not depend on the page count
            catch (FormatException ex) { errors.Add(ex.Message); }
        }
        return errors;
    }

    public async Task<JobResult> RunAsync(CancellationToken ct = default)
    {
        var errors = Validate(_cfg, _opt);
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));

        Directory.CreateDirectory(_opt.OutDir);
        string workDir = Path.Combine(_opt.OutDir, "work");

        IPageRenderer renderer;
        if (!string.IsNullOrWhiteSpace(_opt.ImagesDir))
            renderer = new ImageFolderRenderer(_opt.ImagesDir);
        else
        {
            string? tools = PopplerLocator.Find(_opt.PopplerDir);
            renderer = new PdftoppmRenderer(_opt.PdfPath!, _cfg.RenderDpi, tools);
        }

        var pages = PageRange.Parse(_opt.PagesSpec, renderer.PageCount);
        if (_opt.MaxPages is > 0) pages = pages.Take(_opt.MaxPages.Value).ToList();

        OpenAiCompatibleClient? client = _opt.Mode == JobMode.Run ? new OpenAiCompatibleClient(_cfg) : null;
        try
        {
            _log($"{_opt.Mode}: {pages.Count} page(s) of {renderer.PageCount}; model={_cfg.Model}; endpoint={_cfg.BaseUrl}");

            var pipeline = new ConversionPipeline(_cfg, renderer, client, workDir, _log)
            {
                OnPage = (r, done, total) =>
                {
                    _log($"  page {r.PdfPage}: {Describe(r)}");
                    _progress?.Invoke(done, total);
                }
            };
            Book book = await pipeline.RunAsync(_opt.PoetName, pages, ct);

            string stem = Sanitize(_opt.FileStem ?? _opt.PoetName);
            var result = new JobResult
            {
                GdbPath = Path.Combine(_opt.OutDir, stem + ".gdb"),
                SidecarPath = Path.Combine(_opt.OutDir, stem + ".pages.json"),
                ReportPath = Path.Combine(_opt.OutDir, stem + ".report.txt"),
                Stats = pipeline.Stats,
                Issues = book.Issues,
                Categories = book.Categories.Count,
                Poems = book.Categories.Sum(c => c.Poems.Count),
                VerseRows = book.Categories.Sum(c => c.Poems.Sum(p => p.Verses.Count)),
                PagesProcessed = book.PagesSeen,
                PoetryPages = book.PoetryPages
            };

            GdbWriter.Write(book, result.GdbPath);
            SidecarWriter.Write(book, result.SidecarPath, _opt.PdfPath, renderer.PageCount, _cfg);
            File.WriteAllText(result.ReportPath, BuildReport(book, pipeline.Stats), new UTF8Encoding(false));
            return result;
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static string Describe(PageResult r)
    {
        if (r.PageKind == "error") return "FAILED - " + r.Notes;
        int rows = r.Blocks.Count(b => b.Type == "verse_row");
        int heads = r.Blocks.Count(b => b.Type is "poem_heading" or "category_heading");
        return r.PageKind == "poetry" ? $"poetry, {rows} rows, {heads} heading(s)" : r.PageKind + " (skipped)";
    }

    public static string BuildReport(Book book, RunStats s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Poet: {book.PoetName}");
        sb.AppendLine($"Pages: {book.PagesSeen} seen, {book.PoetryPages} poetry, {book.SkippedPages} skipped as non-poetry");
        sb.AppendLine($"Model calls: {s.PagesFromModel} (cached: {s.PagesFromCache}, failed: {s.PagesFailed})");
        sb.AppendLine($"Tokens this run: prompt {s.PromptTokens}, completion {s.CompletionTokens}");
        sb.AppendLine($"Footnote blocks dropped: {book.DroppedFootnotes}");
        sb.AppendLine();
        foreach (var c in book.Categories)
        {
            sb.AppendLine($"[{c.Id}] {c.Title}  (from page {c.PdfPageStart}, {c.Poems.Count} poems)");
            foreach (var p in c.Poems.Where(p => p.Flags.Count > 0))
                sb.AppendLine($"    poem {p.Id} «{p.Title}» p.{p.PdfPageStart}: {string.Join(", ", p.Flags)}");
        }
        sb.AppendLine();
        sb.AppendLine("Issues:");
        foreach (var i in book.Issues) sb.AppendLine("  - " + i);
        if (book.Issues.Count == 0) sb.AppendLine("  (none)");
        return sb.ToString();
    }

    public static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();
        return s.Length == 0 ? "book" : s;
    }
}
