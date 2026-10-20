namespace GanjoorPdfToGdb;

/// <summary>
/// Consumes page results in book order and builds the category → poem → verse tree.
/// It is deterministic: the same page results always give the same book, so it can be re-run
/// (the "assemble" command) after changing settings without calling the model again.
/// </summary>
public sealed class BookAssembler
{
    private readonly AppConfig _cfg;
    private readonly Book _book;
    private readonly HashSet<string> _knownSections;

    private Category? _cat;
    private Poem? _poem;
    private int _untitledCounter;

    // verse counter validation
    private int _baytIndex;
    private int? _counterOffset;
    private bool _centeredHalfOpen;
    private string? _lastMargin;
    private Suspect? _suspect;

    /// <summary>A margin number that disagrees with the sequence so far; it is confirmed or dismissed by the next one.</summary>
    private sealed record Suspect(int Offset, int Printed, int Diff, int PdfPage, Poem Poem, Row Row);

    public BookAssembler(AppConfig cfg, string poetName)
    {
        _cfg = cfg;
        _book = new Book { PoetName = poetName };
        _knownSections = new HashSet<string>(cfg.KnownSections.Select(TextNormalizer.LooseKey));
    }

    public Book Book => _book;

    /// <summary>What the model should be told about the previous page.</summary>
    public PageContext Snapshot()
    {
        var ctx = new PageContext
        {
            CurrentCategory = _cat?.Title,
            CurrentPoemTitle = _poem?.Title,
            CurrentPoemNumber = _poem?.PrintedNumber,
            LastMarginNumber = _lastMargin
        };
        if (_poem != null)
            foreach (var r in _poem.Rows.TakeLast(2))
                ctx.LastRows.Add(r.Layout == "two_column" ? $"{r.Right} | {r.Left}" : $"[{r.Layout}] {r.Text}");
        return ctx;
    }

    public void AddPage(PageResult page)
    {
        _book.PagesSeen++;

        if (page.PageKind == "error")
        {
            _book.Issues.Add($"page {page.PdfPage}: analysis failed ({page.Notes})");
            _poem?.Flags.Add("page-missing");
            return;
        }
        if (page.PageKind != "poetry")
        {
            _book.SkippedPages++;
            return;
        }
        _book.PoetryPages++;

        // A running header that names a known section switches the category if the model missed the heading.
        bool hasCategoryBlock = page.Blocks.Any(b => b.Type == "category_heading");
        if (!hasCategoryBlock && !string.IsNullOrWhiteSpace(page.RunningHeader))
        {
            string key = TextNormalizer.LooseKey(page.RunningHeader);
            if (_knownSections.Contains(key) && (_cat == null || TextNormalizer.LooseKey(_cat.Title) != key))
            {
                string headerTitle = TextNormalizer.Normalize(page.RunningHeader, _cfg);
                StartCategory(headerTitle, page.PdfPage);
                _book.Issues.Add($"page {page.PdfPage}: category «{headerTitle}» taken from the running header (no heading block)");
            }
        }

        foreach (var b in page.Blocks)
        {
            switch (b.Type)
            {
                case "category_heading":
                {
                    string title = TextNormalizer.Normalize(b.Text, _cfg);
                    if (title.Length == 0) break;
                    _knownSections.Add(TextNormalizer.LooseKey(title));
                    if (_cat != null && TextNormalizer.LooseKey(_cat.Title) == TextNormalizer.LooseKey(title)) break; // repeated heading
                    StartCategory(title, page.PdfPage);
                    break;
                }
                case "poem_heading":
                    StartOrUpdatePoem(b, page.PdfPage);
                    break;
                case "verse_row":
                    AddRow(b, page);
                    break;
                case "footnote":
                    _book.DroppedFootnotes++;
                    if (!string.IsNullOrWhiteSpace(b.Text))
                        _book.Footnotes.Add(new FootnoteInfo(page.PdfPage, TextNormalizer.Normalize(b.Text, _cfg)));
                    break;
                // prose and ornament on a poetry page are ignored.
            }
        }
    }

    private void StartCategory(string title, int page)
    {
        CloseCenteredPair();
        _cat = new Category { Title = title, PdfPageStart = page };
        _book.Categories.Add(_cat);
        _poem = null;
    }

    private void StartOrUpdatePoem(Block b, int page)
    {
        CloseCenteredPair();
        EnsureCategory(page);

        string? text = string.IsNullOrWhiteSpace(b.Text) ? null : TextNormalizer.Normalize(b.Text, _cfg);
        string? number = string.IsNullOrWhiteSpace(b.Number) ? null : TextNormalizer.Normalize(b.Number, _cfg);
        if (text == null && number == null) return;

        // A heading directly after another heading (title + number) describes the same poem.
        if (_poem != null && _poem.Rows.Count == 0)
        {
            if (text != null) _poem.Title = text;
            if (number != null) _poem.PrintedNumber = number;
            if (text == null) _poem.Title = ApplyTemplate(_cfg.PoemTitleTemplate, number!, _cat!.Title);
            return;
        }

        var poem = new Poem
        {
            PdfPageStart = page,
            PrintedNumber = number,
            Category = _cat!,
            IndexInCategory = _cat!.Poems.Count + 1,
            Title = text ?? ApplyTemplate(_cfg.PoemTitleTemplate, number!, _cat!.Title)
        };
        _cat.Poems.Add(poem);
        _poem = poem;
    }

    private void AddRow(Block b, PageResult page)
    {
        EnsureCategory(page.PdfPage);
        if (_poem == null)
        {
            _poem = new Poem
            {
                PdfPageStart = page.PdfPage,
                Category = _cat!,
                IndexInCategory = _cat!.Poems.Count + 1,
                Title = _cfg.UntitledPoemTemplate.Replace("{n}", TextNormalizer.ToPersianDigits(++_untitledCounter))
            };
            _poem.Flags.Add("no-heading");
            _cat.Poems.Add(_poem);
        }

        string layout = (b.Layout ?? "two_column").Trim().ToLowerInvariant();
        if (layout != "two_column" && layout != "centered" && layout != "single") layout = "two_column";
        // A "two_column" row that arrived with a single text field is really a full-width line.
        if (layout == "two_column" && string.IsNullOrWhiteSpace(b.Right) && string.IsNullOrWhiteSpace(b.Left) && !string.IsNullOrWhiteSpace(b.Text))
            layout = "single";

        var row = new Row { Layout = layout, PdfPage = page.PdfPage, Uncertain = b.Uncertain, MarginNumber = b.MarginNumber };
        if (b.UncertainWords != null)
            foreach (string w in b.UncertainWords.Where(w => !string.IsNullOrWhiteSpace(w)))
                row.UncertainWords.Add(w.Trim());
        bool fn1 = false, fn2 = false, fn3 = false;
        if (layout == "two_column")
        {
            row.Right = TextNormalizer.Normalize(b.Right, _cfg, out fn1);
            row.Left = TextNormalizer.Normalize(b.Left, _cfg, out fn2);
        }
        else
        {
            row.Text = TextNormalizer.Normalize(b.Text ?? b.Right ?? b.Left, _cfg, out fn3);
        }
        if (fn1 || fn2 || fn3) row.Flags.Add("footnote-marker-removed");

        _poem.Rows.Add(row);
        TrackCounter(row);
    }

    // ---- verse counter validation (the numbers printed in the margin every 5 bayts) ----
    //
    // Rules learned from real pages:
    //  * a centered pair (two stacked lines) counts as ONE bayt;
    //  * a lone centered line (e.g. a motto line above the first bayt) is not counted;
    //  * one disagreeing number that is followed by numbers matching the old sequence again is a misprint /
    //    misread digit, not a missing line, so it is reported on its own and does not flag the poem as damaged.

    private void TrackCounter(Row row)
    {
        if (row.Layout == "centered")
        {
            if (!_centeredHalfOpen) { _baytIndex++; _centeredHalfOpen = true; }
            else _centeredHalfOpen = false;
        }
        else
        {
            if (_centeredHalfOpen) { _baytIndex--; _centeredHalfOpen = false; } // the previous centered line was alone
            if (row.Layout == "two_column") _baytIndex++;
        }

        int? n = TextNormalizer.ParseNumber(row.MarginNumber);
        if (n == null) return;
        _lastMargin = row.MarginNumber;

        int offset = n.Value - _baytIndex;
        if (_counterOffset == null) { _counterOffset = offset; return; }

        if (offset == _counterOffset)
        {
            if (_suspect != null) { ReportIsolated(_suspect); _suspect = null; }
            return;
        }

        if (_suspect == null)
        {
            _suspect = new Suspect(offset, n.Value, offset - _counterOffset.Value, row.PdfPage, _poem!, row);
            return;
        }

        if (offset == _suspect.Offset)
        {
            // two numbers in a row agree on the new offset: lines really are missing or duplicated
            ReportJump(_suspect);
            _counterOffset = offset;
            _suspect = null;
        }
        else
        {
            ReportIsolated(_suspect);
            _suspect = new Suspect(offset, n.Value, offset - _counterOffset.Value, row.PdfPage, _poem!, row);
        }
    }

    private void ReportJump(Suspect s, bool confirmed = true)
    {
        s.Row.Flags.Add("counter-jump");
        s.Poem.Flags.Add("counter-jump");
        _book.Issues.Add($"page {s.PdfPage}, poem «{s.Poem.Title}»: verse counter {s.Printed} is {(s.Diff > 0 ? s.Diff + " ahead of" : -s.Diff + " behind")} the bayts counted so far{(confirmed ? " and the following numbers agree with it" : " (no later number to confirm it)")} (lines may be missing or duplicated, or the book's numbering restarts here)");
    }

    private void ReportIsolated(Suspect s)
    {
        s.Row.Flags.Add("counter-misread");
        s.Poem.Flags.Add("counter-misread");
        _book.Issues.Add($"page {s.PdfPage}, poem «{s.Poem.Title}»: one margin number ({s.Printed}) is off by {s.Diff:+#;-#} but the numbers around it fit the sequence (a misprint or a misread digit; the text is probably fine)");
    }

    private void CloseCenteredPair()
    {
        if (_centeredHalfOpen) { _baytIndex--; _centeredHalfOpen = false; } // a lone centered line is not counted
    }

    private void EnsureCategory(int page)
    {
        if (_cat != null) return;
        _cat = new Category { Title = _cfg.UnknownCategoryTitle, PdfPageStart = page };
        _book.Categories.Add(_cat);
        _book.Issues.Add($"page {page}: verses found before any section heading; placed in category «{_cat.Title}»");
    }

    private static string ApplyTemplate(string template, string number, string category)
        => template.Replace("{number}", number).Replace("{category}", category);

    /// <summary>Finalizes verses, titles and ids. Call once after the last page.</summary>
    public Book Finish()
    {
        if (_suspect != null) { ReportJump(_suspect, confirmed: false); _suspect = null; } // never confirmed: report it as it is

        // drop empty poems and categories
        foreach (var c in _book.Categories.ToList())
        {
            foreach (var p in c.Poems.Where(p => p.Rows.Count == 0).ToList())
            {
                c.Poems.Remove(p);
                _book.Issues.Add($"poem «{p.Title}» (page {p.PdfPageStart}) had no verses and was dropped");
            }
            if (c.Poems.Count == 0)
            {
                _book.Categories.Remove(c);
                _book.Issues.Add($"category «{c.Title}» (page {c.PdfPageStart}) had no poems and was dropped");
            }
        }

        _book.PoetId = _cfg.FirstPoetId;
        _book.PoetCatId = _cfg.FirstCatId;
        int catId = _cfg.FirstCatId + 1;
        int poemId = _cfg.FirstPoemId;

        foreach (var c in _book.Categories)
        {
            c.Id = catId++;
            var seen = new Dictionary<string, int>();
            int idx = 0;
            foreach (var p in c.Poems)
            {
                p.Id = poemId++;
                p.IndexInCategory = ++idx;

                if (_cfg.DisambiguateDuplicateTitles)
                {
                    string key = TextNormalizer.LooseKey(p.Title);
                    seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (seen[key] > 1)
                    {
                        p.Title = $"{p.Title} {TextNormalizer.ToPersianDigits(seen[key])}";
                        p.Flags.Add("duplicate-title-renamed");
                    }
                }
                PoemBuilder.Build(p);
            }
        }
        return _book;
    }
}
