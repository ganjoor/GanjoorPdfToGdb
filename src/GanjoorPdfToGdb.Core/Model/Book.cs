namespace GanjoorPdfToGdb;

/// <summary>Same numeric values as DesktopGanjoorDbBrowser.VersePosition.</summary>
public enum VersePosition
{
    Right = 0,
    Left = 1,
    CenteredVerse1 = 2,
    CenteredVerse2 = 3,
    Single = 4,
    Comment = 5,
    Paragraph = -1
}

public sealed class Book
{
    public string PoetName { get; set; } = "";
    public int PoetId { get; set; }
    public int PoetCatId { get; set; }
    public List<Category> Categories { get; } = new();
    /// <summary>Book-level issues (failed pages, counter jumps outside any poem, ...).</summary>
    public List<string> Issues { get; } = new();
    public int PagesSeen { get; set; }
    public int PoetryPages { get; set; }
    public int SkippedPages { get; set; }
    public int DroppedFootnotes { get; set; }
    /// <summary>Text of the blocks the model reported as footnotes (kept in the sidecar so they can be reviewed).</summary>
    public List<FootnoteInfo> Footnotes { get; } = new();
}

public sealed record FootnoteInfo(int PdfPage, string Text);

public sealed class Category
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int PdfPageStart { get; set; }
    /// <summary>Enclosing category (e.g. «غزلیات» for a «حرف الف» sub-section); null = directly under the poet.</summary>
    public Category? Parent { get; set; }
    public List<Poem> Poems { get; } = new();
}

/// <summary>One printed line, before positions are decided.</summary>
public sealed class Row
{
    public string Layout { get; set; } = "two_column"; // two_column | centered | single
    public string? Right { get; set; }
    public string? Left { get; set; }
    public string? Text { get; set; }
    public int PdfPage { get; set; }
    public string? MarginNumber { get; set; }
    public bool Uncertain { get; set; }
    public List<string> UncertainWords { get; } = new();
    public List<string> Flags { get; } = new();
}

public sealed class Poem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? PrintedNumber { get; set; }
    /// <summary>The number as the model read it, when it was corrected from the numbering sequence.</summary>
    public string? RawPrintedNumber { get; set; }
    /// <summary>True when the title was generated from the number (so it follows a number correction).</summary>
    public bool TitleFromNumber { get; set; }
    /// <summary>Creation order across the whole book.</summary>
    public int Seq { get; set; }
    public int PdfPageStart { get; set; }
    public List<Row> Rows { get; } = new();
    public List<Verse> Verses { get; } = new();
    public SortedSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public Category Category { get; set; } = null!;
    public int IndexInCategory { get; set; }
}

public sealed class Verse
{
    public int Order { get; set; }
    public VersePosition Position { get; set; }
    public string Text { get; set; } = "";
    public int PdfPage { get; set; }
    /// <summary>The verse counter printed in the margin at this line, if any (kept for debugging the counter check).</summary>
    public string? MarginNumber { get; set; }
    public List<string> Flags { get; } = new();
}
