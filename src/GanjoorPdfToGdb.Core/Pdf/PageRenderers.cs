using System.Diagnostics;

namespace GanjoorPdfToGdb;

/// <summary>Turns a 1-based page index into a PNG file.</summary>
public interface IPageRenderer
{
    int PageCount { get; }
    /// <summary>Returns the path of a PNG for the page (rendering it into outDir if needed).</summary>
    Task<string> GetPageImageAsync(int page, string outDir, CancellationToken ct = default);
}

/// <summary>
/// Finds poppler's command-line tools (pdftoppm, pdfinfo). Order: the folder given explicitly, a "poppler"
/// folder next to the application (any depth up to 5, e.g. poppler\Library\bin), then PATH (null result).
/// </summary>
public static class PopplerLocator
{
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>Returns the directory containing pdftoppm, or null to rely on PATH.</summary>
    public static string? Find(string? configuredDir, string? appBaseDir = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredDir) && File.Exists(Path.Combine(configuredDir, Exe("pdftoppm"))))
            return configuredDir;

        string bundled = Path.Combine(appBaseDir ?? AppContext.BaseDirectory, "poppler");
        if (Directory.Exists(bundled))
        {
            try
            {
                var hit = Directory.EnumerateFiles(bundled, Exe("pdftoppm"), new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 5,
                    IgnoreInaccessible = true
                }).FirstOrDefault();
                if (hit != null) return Path.GetDirectoryName(hit);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>True if the tools can be started (found by Find or on PATH).</summary>
    public static bool IsAvailable(string? toolsDir)
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(toolsDir ?? "", Exe("pdftoppm")), "-v")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return true; // it started; pdftoppm -v exits with 0 or 99 depending on the build
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public static string ToolPath(string? toolsDir, string tool)
        => string.IsNullOrEmpty(toolsDir) ? Exe(tool) : Path.Combine(toolsDir, Exe(tool));
}

/// <summary>Renders PDF pages with poppler's pdftoppm. Works on Windows, macOS and Linux.</summary>
public sealed class PdftoppmRenderer : IPageRenderer
{
    private readonly string _pdf;
    private readonly int _dpi;
    private readonly string? _toolsDir;
    public int PageCount { get; }

    /// <param name="toolsDir">Folder with pdftoppm/pdfinfo, or null to use PATH.</param>
    public PdftoppmRenderer(string pdfPath, int dpi, string? toolsDir = null)
    {
        _pdf = pdfPath;
        _dpi = dpi;
        _toolsDir = toolsDir;
        PageCount = ReadPageCount(pdfPath, toolsDir);
    }

    public async Task<string> GetPageImageAsync(int page, string outDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);
        string prefix = Path.Combine(outDir, page.ToString("D4"));
        string target = prefix + ".png";
        if (File.Exists(target)) return target;

        var psi = new ProcessStartInfo(PopplerLocator.ToolPath(_toolsDir, "pdftoppm"))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-r", _dpi.ToString(), "-gray", "-png", "-singlefile", "-f", page.ToString(), "-l", page.ToString(), _pdf, prefix })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start pdftoppm.");
        string err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0 || !File.Exists(target))
            throw new InvalidOperationException($"pdftoppm failed for page {page}: {err}");
        return target;
    }

    private static int ReadPageCount(string pdf, string? toolsDir)
    {
        try
        {
            var psi = new ProcessStartInfo(PopplerLocator.ToolPath(toolsDir, "pdfinfo"))
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(pdf);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in o.Split('\n'))
                if (line.StartsWith("Pages:") && int.TryParse(line[6..].Trim(), out int n)) return n;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("poppler (pdftoppm/pdfinfo) was not found. Put the poppler folder next to the " +
                "application (a folder named «poppler»), set the poppler folder in the settings, or add it to PATH. " +
                "Alternatively use a folder of pre-rendered page images.");
        }
        throw new InvalidOperationException("Could not read the page count from pdfinfo (is the file a valid PDF?).");
    }
}

/// <summary>Uses a folder of page images (png/jpg); the Nth file in name order is page N.</summary>
public sealed class ImageFolderRenderer : IPageRenderer
{
    private readonly string[] _files;
    public int PageCount => _files.Length;

    public ImageFolderRenderer(string dir)
    {
        _files = Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_files.Length == 0) throw new InvalidOperationException("No images found in " + dir);
    }

    public Task<string> GetPageImageAsync(int page, string outDir, CancellationToken ct = default)
        => page >= 1 && page <= _files.Length
            ? Task.FromResult(_files[page - 1])
            : throw new ArgumentOutOfRangeException(nameof(page));
}
