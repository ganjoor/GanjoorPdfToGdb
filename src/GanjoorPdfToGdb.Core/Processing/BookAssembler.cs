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

    private Category? _cat;   // where new poems go (a sub-section when one is open)
    private Category? _top;   // the current main section
    private Poem? _poem;
    private int _poemSeq;
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
        bool wanted = page.PageKind == "poetry" || (_cfg.IncludeProse && page.PageKind == "prose");
        if (!wanted)
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
            if (_knownSections.Contains(key) && (_cat == null || TextNormalizer.LooseKey(_cat.Title) != key)
                && (_top == null || TextNormalizer.LooseKey(_top.Title) != key)) // the header of the enclosing section is not a switch
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
                    if (_cat != null && TextNormalizer.LooseKey(_cat.Title) == TextNormalizer.LooseKey(title)) break; // repeated heading
                    if (_top != null && IsSubSection(title, b.Level))
                    {
                        StartCategory(title, page.PdfPage, _top);
                        break;
                    }
                    _knownSections.Add(TextNormalizer.LooseKey(title));
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
                case "prose":
                    if (_cfg.IncludeProse) AddParagraph(b, page);
                    break;
                // ornaments (and prose when IncludeProse is off) are ignored.
            }
        }
    }

    private void StartCategory(string title, int page, Category? parent = null)
    {
        CloseCenteredPair();
        _cat = new Category { Title = title, PdfPageStart = page, Parent = parent };
        if (parent == null) _top = _cat;
        _book.Categories.Add(_cat);
        _poem = null;
    }

    /// <summary>A heading is a sub-section when the model says level 2 or it starts with a configured word such as «حرف».</summary>
    private bool IsSubSection(string title, int? level)
    {
        if (level == 2) return true;
        if (level == 1) return false;
        string letters = new string(title.Where(char.IsLetterOrDigit).ToArray());
        return _cfg.SubSectionPrefixes.Any(p =>
        {
            string pk = new string(p.Where(char.IsLetterOrDigit).ToArray());
            return pk.Length > 0 && letters.StartsWith(pk, StringComparison.Ordinal);
        });
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
            if (text == null) { _poem.Title = ApplyTemplate(_cfg.PoemTitleTemplate, number!, _cat!.Title); _poem.TitleFromNumber = true; }
            else _poem.TitleFromNumber = false;
            return;
        }

        var poem = new Poem
        {
            PdfPageStart = page,
            PrintedNumber = number,
            Category = _cat!,
            Seq = ++_poemSeq,
            IndexInCategory = _cat!.Poems.Count + 1,
            TitleFromNumber = text == null,
            Title = text ?? ApplyTemplate(_cfg.PoemTitleTemplate, number!, _cat!.Title)
        };
        _cat.Poems.Add(poem);
        _poem = poem;
    }

    private void EnsurePoem(int pdfPage)
    {
        EnsureCategory(pdfPage);
        if (_poem != null) return;
        _poem = new Poem
        {
            PdfPageStart = pdfPage,
            Category = _cat!,
            Seq = ++_poemSeq,
            IndexInCategory = _cat!.Poems.Count + 1,
            Title = _cfg.UntitledPoemTemplate.Replace("{n}", TextNormalizer.ToPersianDigits(++_untitledCounter))
        };
        _poem.Flags.Add("no-heading");
        _cat.Poems.Add(_poem);
    }

    /// <summary>One printed paragraph becomes one paragraph verse (no line breaks).</summary>
    private void AddParagraph(Block b, PageResult page)
    {
        string text = TextNormalizer.Normalize(b.Text, _cfg, out bool fn);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return;
        CloseCenteredPair();
        EnsurePoem(page.PdfPage);

        var last = _poem!.Rows.Count > 0 ? _poem.Rows[^1] : null;
        if (b.ContinuesPreviousPage && last is { Layout: "paragraph" })
        {
            last.Text = last.Text + " " + text;
            if (fn) last.Flags.Add("footnote-marker-removed");
            return;
        }
        var row = new Row { Layout = "paragraph", PdfPage = page.PdfPage, Text = text, Uncertain = b.Uncertain };
        if (b.UncertainWords != null)
            foreach (string w in b.UncertainWords.Where(w => !string.IsNullOrWhiteSpace(w))) row.UncertainWords.Add(w.Trim());
        if (fn) row.Flags.Add("footnote-marker-removed");
        _poem.Rows.Add(row);
    }

    private void AddRow(Block b, PageResult page)
    {
        EnsurePoem(page.PdfPage);

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

        _poem!.Rows.Add(row);
        if (!_cfg.IncludeProse) TrackCounter(row); // in prose books the margin numbers count printed lines, not bayts
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
        _top = _cat;
        _book.Categories.Add(_cat);
        _book.Issues.Add($"page {page}: verses found before any section heading; placed in category «{_cat.Title}»");
    }

    // ---- printed poem numbers: find misreads from the sequence ----
    //
    // Poems are numbered 1, 2, 3 ... in many books. The model sometimes misreads one digit (250 -> 350) or a whole
    // block shares the same slip. The numbers are split into runs of consecutive values; a run that sits between two
    // runs and whose length exactly fills the gap between them is a misread block and is renumbered to fit.
    // Everything else that breaks the sequence is only reported.

    private void CheckPoemNumbers()
    {
        var list = _book.Categories.SelectMany(c => c.Poems)
            .Select(p => (Poem: p, N: TextNormalizer.ParseNumber(p.PrintedNumber)))
            .Where(x => x.N != null).OrderBy(x => x.Poem.Seq)
            .Select(x => (x.Poem, N: x.N!.Value)).ToList();
        if (list.Count < 4) return;
        var nums = list.Select(x => x.N).ToArray();

        if (_cfg.AutoCorrectPoemNumbers)
        {
            for (int pass = 0; pass < 5; pass++)
            {
                bool changed = false;
                var runs = Runs(nums);
                for (int k = 1; k < runs.Count - 1; k++)
                {
                    var (s0, e0) = runs[k];
                    int p = nums[runs[k - 1].End], q = nums[runs[k + 1].Start];
                    int len = e0 - s0 + 1;
                    if (q - p - 1 != len || nums[s0] == p + 1) continue;
                    var oldFirst = nums[s0]; var oldLast = nums[e0];
                    for (int i = s0; i <= e0; i++)
                    {
                        var poem = list[i].Poem;
                        poem.RawPrintedNumber ??= poem.PrintedNumber;
                        nums[i] = p + 1 + (i - s0);
                        string txt = TextNormalizer.ToPersianDigits(nums[i]);
                        poem.PrintedNumber = txt;
                        if (poem.TitleFromNumber) poem.Title = ApplyTemplate(_cfg.PoemTitleTemplate, txt, poem.Category.Title);
                        poem.Flags.Add("number-corrected");
                    }
                    _book.Issues.Add($"pages {list[s0].Poem.PdfPageStart}–{list[e0].Poem.PdfPageStart}: poem numbers {Fa(oldFirst)}–{Fa(oldLast)} changed to {Fa(p + 1)}–{Fa(q - 1)} to fit the sequence between {Fa(p)} and {Fa(q)} (probably misread digits; check the scan)");
                    changed = true;
                    break; // runs changed: recompute
                }
                if (!changed) break;
            }
        }

        // what still breaks the sequence inside one main section is reported
        for (int i = 1; i < list.Count; i++)
        {
            if (nums[i] == nums[i - 1] + 1) continue;
            if (nums[i] == 1) continue; // a new numbering starts
            if (Root(list[i].Poem.Category) != Root(list[i - 1].Poem.Category)) continue;
            list[i].Poem.Flags.Add("number-jump");
            _book.Issues.Add($"page {list[i].Poem.PdfPageStart}, poem «{list[i].Poem.Title}»: number {Fa(nums[i])} follows {Fa(nums[i - 1])} (a poem may be missing, or a number was misread)");
        }
    }

    private static List<(int Start, int End)> Runs(int[] nums)
    {
        var runs = new List<(int, int)>();
        int st = 0;
        for (int i = 1; i <= nums.Length; i++)
            if (i == nums.Length || nums[i] != nums[i - 1] + 1) { runs.Add((st, i - 1)); st = i; }
        return runs;
    }

    private static Category Root(Category c) { while (c.Parent != null) c = c.Parent; return c; }
    private static string Fa(int n) => TextNormalizer.ToPersianDigits(n);

    private static string ApplyTemplate(string template, string number, string category)
        => template.Replace("{number}", number).Replace("{category}", category);

    /// <summary>Finalizes verses, titles and ids. Call once after the last page.</summary>
    public Book Finish()
    {
        if (_suspect != null) { ReportJump(_suspect, confirmed: false); _suspect = null; } // never confirmed: report it as it is

        // drop empty poems and categories (a main section that only holds sub-sections stays)
        foreach (var c in _book.Categories)
            foreach (var p in c.Poems.Where(p => p.Rows.Count == 0).ToList())
            {
                c.Poems.Remove(p);
                _book.Issues.Add($"poem «{p.Title}» (page {p.PdfPageStart}) had no verses and was dropped");
            }
        for (int ci = _book.Categories.Count - 1; ci >= 0; ci--)
        {
            var c = _book.Categories[ci];
            if (c.Poems.Count == 0 && !_book.Categories.Any(x => x.Parent == c))
            {
                _book.Categories.RemoveAt(ci);
                _book.Issues.Add($"category «{c.Title}» (page {c.PdfPageStart}) had no poems and was dropped");
            }
        }

        CheckPoemNumbers();

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
                PoemBuilder.Build(p, _cfg.IncludeProse);
            }
        }
        return _book;
    }
}
