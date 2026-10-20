using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GanjoorPdfToGdb;

/// <summary>
/// Writes the JSON that links every published poem and verse to the page of the source PDF
/// (1-based PDF page index), for the "show the original page" feature on ganjoor.net.
/// </summary>
public static class SidecarWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Write(Book book, string sidecarPath, string? pdfPath, int pdfPageCount, AppConfig cfg)
    {
        var doc = new
        {
            schema = 1,
            source = new
            {
                file = pdfPath == null ? null : Path.GetFileName(pdfPath),
                sha256 = pdfPath != null && File.Exists(pdfPath) ? Sha256(pdfPath) : null,
                pageCount = pdfPageCount,
                renderDpi = cfg.RenderDpi,
                pageIndexBase = 1
            },
            generator = new { model = cfg.Model, baseUrl = cfg.BaseUrl, runDate = DateTime.UtcNow.ToString("yyyy-MM-dd") },
            poet = new { name = book.PoetName, gdbPoetId = book.PoetId, gdbCatId = book.PoetCatId },
            stats = new
            {
                pagesSeen = book.PagesSeen,
                poetryPages = book.PoetryPages,
                skippedPages = book.SkippedPages,
                droppedFootnotes = book.DroppedFootnotes
            },
            categories = book.Categories.Select(c => new
            {
                gdbCatId = c.Id,
                title = c.Title,
                parentGdbCatId = book.PoetCatId,
                pdfPageStart = c.PdfPageStart,
                poems = c.Poems.Select(p => new
                {
                    gdbPoemId = p.Id,
                    key = $"{c.Id}/{(p.PrintedNumber ?? p.IndexInCategory.ToString())}",
                    title = p.Title,
                    printedNumber = p.PrintedNumber,
                    pdfPages = p.Verses.Select(v => v.PdfPage).Distinct().OrderBy(x => x).ToList(),
                    firstVerseHash = p.Verses.Count > 0 ? Sha256Text(p.Verses[0].Text)[..16] : null,
                    verses = p.Verses.Select(v => new
                    {
                        order = v.Order,
                        position = (int)v.Position,
                        text = v.Text,
                        pdfPage = v.PdfPage,
                        marginNumber = v.MarginNumber,
                        flags = v.Flags.Count > 0 ? v.Flags : null
                    }).ToList(),
                    flags = p.Flags.Count > 0 ? p.Flags.ToList() : null,
                    review = "unreviewed"
                }).ToList()
            }).ToList(),
            footnotes = book.Footnotes.Count > 0 ? book.Footnotes.Select(f => new { pdfPage = f.PdfPage, text = f.Text }).ToList() : null,
            issues = book.Issues
        };

        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(doc, Options), new UTF8Encoding(false));
    }

    private static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static string Sha256Text(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
