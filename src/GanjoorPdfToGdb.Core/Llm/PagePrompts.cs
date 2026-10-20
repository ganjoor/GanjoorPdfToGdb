using System.Text;

namespace GanjoorPdfToGdb;

/// <summary>State carried from the previous page so the model can tell where it is in the book.</summary>
public sealed class PageContext
{
    public string? CurrentCategory { get; set; }
    public string? CurrentPoemTitle { get; set; }
    public string? CurrentPoemNumber { get; set; }
    public string? LastMarginNumber { get; set; }
    public List<string> LastRows { get; set; } = new();
}

public static class PagePrompts
{
    public const string System = """
You are a careful digitizer of printed Persian poetry books (divans). You receive ONE scanned page.
Return ONLY a single JSON object, no commentary, no markdown fences.

JSON schema:
{
  "pageKind": "poetry | prose | contents | index | title | blank | other",
  "printedPageLabel": "page number or label printed on the page, or null",
  "runningHeader": "text of the running header (e.g. the section name), or null",
  "startsMidPoem": true/false,
  "blocks": [ ... in reading order, top to bottom ... ],
  "notes": "only if something is unusual, else null"
}

Block types:
  {"type":"category_heading","text":"..."}      a large heading where a NEW section of the book visibly begins
                                                (e.g. قصاید، غزلیات، قطعات، رباعیات، مثنویات). NOT the running header.
  {"type":"poem_heading","text":"...|null","number":"...|null"}
                                                a title, a poem number, or a label such as «قطعه» that starts a new poem.
  {"type":"verse_row","layout":"two_column","right":"...","left":"..."}
                                                one printed line made of two hemistichs side by side.
                                                "right" is the hemistich on the RIGHT side (read first), "left" the one on the left.
  {"type":"verse_row","layout":"centered","text":"..."}
                                                one printed line that is centered on the page (a single hemistich or line).
  {"type":"verse_row","layout":"single","text":"..."}
                                                one full-width line that is not centered and not split in two (e.g. free verse).
  {"type":"footnote","text":"..."}              notes printed at the bottom of the page.
  {"type":"prose","text":"..."}                 paragraph text that is not verse.
  {"type":"ornament","text":"..."}              decorative separators such as ✲ ✲ ✲.

Any verse_row may also carry "marginNumber": the number printed in the margin at the height of that line
(verse counters such as 3840, 3845), exactly as printed, and "uncertainWords": a list of the specific words
(copied exactly as you transcribed them) that you are genuinely unsure about.

Rules:
1. Transcribe EXACTLY what is printed. Never correct, modernize, complete from memory, or substitute a verse you know.
   If a specific word is unclear, give your best reading and list that word in "uncertainWords" on that row.
   Use it sparingly: most lines have none. Dense print, small type or words printed close together are NOT
   a reason to flag a line when you can read it; leave "uncertainWords" out for such lines.
2. One verse_row per printed line. A centered couplet printed on two stacked lines is TWO centered rows.
3. Margin numbers, running headers, page numbers and footnote markers such as (۱) must NOT appear inside verse text.
4. Use normal Persian orthography: a space between words even if the print runs them together, and a
   zero-width non-joiner (U+200C) where Persian requires it (می‌روم، کتاب‌ها، خانه‌اش). Use Persian ی and ک.
   Keep the ezafe hamza on words ending in ه as printed (e.g. «پردهٔ»).
5. pageKind is "poetry" ONLY when the main content of the page is the book's own verses. A page of prose
   that merely quotes verses (a preface, commentary) is "prose". Title pages, tables of contents and
   indexes get their own kinds. Do not transcribe pages that are not poetry: for them return an empty
   blocks array.
6. A poem that began on a previous page continues without a heading: set "startsMidPoem": true and emit
   its rows first. Use the provided context to decide.
7. Poem numbers printed as headings (e.g. 782) go into poem_heading.number; keep the digits as printed.
8. Do not invent headings, rows or numbers that are not visible.
""";

    public static string BuildUserText(int pdfPage, PageContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PDF page index: {pdfPage}");
        sb.AppendLine("Context from the previous page (may be empty):");
        sb.AppendLine($"- current section: {ctx.CurrentCategory ?? "(none yet)"}");
        sb.AppendLine($"- last poem: {ctx.CurrentPoemTitle ?? "(none)"}  number: {ctx.CurrentPoemNumber ?? "(none)"}");
        if (ctx.LastMarginNumber != null) sb.AppendLine($"- last margin number seen: {ctx.LastMarginNumber}");
        if (ctx.LastRows.Count > 0)
        {
            sb.AppendLine("- last rows of the previous page:");
            foreach (var r in ctx.LastRows) sb.AppendLine("    " + r);
        }
        sb.AppendLine("Now return the JSON for the attached page.");
        return sb.ToString();
    }
}
