using System.Text;
using GanjoorPdfToGdb;

return await Cli.MainAsync(args);

internal static class Cli
{
    private const string Usage = """
pdf2gdb - convert a scanned Persian poetry book (PDF) into a Desktop Ganjoor .gdb + JSON sidecar

Usage:
  pdf2gdb run      --pdf book.pdf --poet "name" --out outdir [--pages 74-1130] [--config appsettings.json]
  pdf2gdb run      --images-dir pages/ --poet "name" --out outdir ...      (pre-rendered page images)
  pdf2gdb assemble --pdf book.pdf --poet "name" --out outdir [--pages ...]  (rebuild from cached pages, no API calls)

Options:
  --pdf <file>          source PDF (needs poppler's pdftoppm/pdfinfo: next to the app in a "poppler" folder, --poppler-dir, or PATH)
  --images-dir <dir>    folder of page images instead of a PDF (Nth file = page N)
  --poet <name>         poet name written into the GDB
  --out <dir>           output directory (gdb, sidecar json, report, work/ cache)
  --pages <spec>        all | 74-1130 | 9,76,600-601      (default: all)
  --include-prose       keep the prose of mixed books (Golestan, tazkiras) as paragraph verses
  --max-pages <n>       only the first n pages of the selection (quick trial)
  --config <file>       JSON settings (BaseUrl, ApiKey, Model, ...). OpenAIBaseUrl/OpenAIAPIKey/OpenAITitleModel also work
  --model <id> --base-url <url>   override model / endpoint
  --poppler-dir <dir>   folder containing pdftoppm / pdfinfo
  --name <file-stem>    output file name (default: poet name)

The API key is read from GDB_API_KEY or OPENAI_API_KEY (preferred) or the config file.
""";

    public static async Task<int> MainAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return 0; }

        string command = args[0];
        if (command is not ("run" or "assemble")) { Console.Error.WriteLine("Unknown command: " + command + "\n\n" + Usage); return 2; }

        try
        {
            var opt = ParseOptions(args.Skip(1).ToArray());

            var cfg = AppConfig.Load(opt.GetValueOrDefault("config"));
            if (opt.TryGetValue("model", out var m)) cfg.Model = m;
            if (opt.TryGetValue("base-url", out var bu)) cfg.BaseUrl = bu;
            if (opt.ContainsKey("include-prose")) cfg.IncludeProse = true;

            var job = new JobOptions
            {
                PdfPath = opt.GetValueOrDefault("pdf"),
                ImagesDir = opt.GetValueOrDefault("images-dir"),
                PoetName = Req(opt, "poet"),
                OutDir = Req(opt, "out"),
                PagesSpec = opt.GetValueOrDefault("pages"),
                PopplerDir = opt.GetValueOrDefault("poppler-dir"),
                FileStem = opt.GetValueOrDefault("name"),
                Mode = command == "run" ? JobMode.Run : JobMode.Assemble,
                MaxPages = opt.TryGetValue("max-pages", out var mp) && int.TryParse(mp, out int n) ? n : null
            };

            if (job.Mode == JobMode.Run && string.IsNullOrWhiteSpace(cfg.ApiKey))
                Console.Error.WriteLine("warning: no API key set (GDB_API_KEY); continuing without an Authorization header.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.Error.WriteLine("Cancelling... (finished pages are kept in the cache)"); };

            JobResult r = await new ConversionJob(cfg, job, Console.WriteLine).RunAsync(cts.Token);

            Console.WriteLine();
            Console.WriteLine($"Done: {r.Categories} categories, {r.Poems} poems, {r.VerseRows} verse rows; {r.Issues.Count} issue(s).");
            Console.WriteLine($"  {r.GdbPath}\n  {r.SidecarPath}\n  {r.ReportPath}");
            return r.HasFailedPages ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or IOException)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] a)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) throw new ArgumentException("Unexpected argument: " + a[i]);
            if (a[i].Equals("--include-prose", StringComparison.OrdinalIgnoreCase)) { d["include-prose"] = "true"; continue; }
            if (i + 1 >= a.Length) throw new ArgumentException("Missing value for " + a[i]);
            d[a[i][2..]] = a[++i];
        }
        return d;
    }

    private static string Req(Dictionary<string, string> o, string k)
        => o.TryGetValue(k, out var v) && v.Length > 0 ? v : throw new ArgumentException($"--{k} is required");
}
