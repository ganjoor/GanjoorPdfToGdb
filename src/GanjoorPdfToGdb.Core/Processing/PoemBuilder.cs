namespace GanjoorPdfToGdb;

/// <summary>
/// Turns the printed rows of one poem into GDB verses and decides what to do with centered lines.
///
/// Printed books often center a poem's last couplet (the one with the takhallos) purely as a typographic
/// choice. Such a couplet is an ordinary bayt and belongs in the GDB as Right + Left. A real centered verse
/// (the refrain «band» of a tarji'-band / tarkib-band) breaks the poem's rhyme and repeats. This class tests a
/// centered pair against the poem's rhyme and flags every decision:
///  * qasida / ghazal: the pair's second line ends with the rhyme shared by the normal rows' second hemistichs;
///  * masnavi: the two hemistichs of every bayt rhyme with each other, so a centered pair whose two lines
///    rhyme with each other is an ordinary bayt too.
/// </summary>
public static class PoemBuilder
{
    /// <summary>Share of normal rows whose two hemistichs must rhyme for the poem to count as a masnavi.</summary>
    private const double MasnaviShare = 0.8;
    private const int MasnaviMinCommonEnding = 2;

    public static void Build(Poem poem, bool proseBook = false)
    {
        poem.Verses.Clear();

        var twoCol = poem.Rows.Where(r => r.Layout == "two_column" && !string.IsNullOrWhiteSpace(r.Left)).ToList();
        RhymeInfo? rhyme = twoCol.Count >= 3 ? RhymeHelper.FindRhyme(twoCol.Select(r => r.Left!)) : null;
        bool strongRhyme = rhyme is { Weak: false, Support: >= 0.85 };

        bool masnavi = false;
        if (!strongRhyme && twoCol.Count >= 3)
        {
            int internal2 = twoCol.Count(r => RhymeHelper.CommonEnding(r.Right, r.Left) >= MasnaviMinCommonEnding);
            masnavi = internal2 >= Math.Ceiling(twoCol.Count * MasnaviShare);
        }

        int order = 1;
        var rows = poem.Rows;
        int i = 0;
        while (i < rows.Count)
        {
            var r = rows[i];
            switch (r.Layout)
            {
                case "two_column":
                {
                    Verse? first = null;
                    if (!string.IsNullOrWhiteSpace(r.Right))
                        first = Add(poem, ref order, VersePosition.Right, r.Right!, r);
                    else
                        r.Flags.Add("missing-right-hemistich");

                    if (!string.IsNullOrWhiteSpace(r.Left))
                    {
                        var v = Add(poem, ref order, VersePosition.Left, r.Left!, r);
                        first ??= v;
                        if (strongRhyme && !RhymeHelper.EndsWithRhyme(r.Left, rhyme!))
                        {
                            v.Flags.Add("rhyme-outlier");
                            poem.Flags.Add("rhyme-outlier");
                        }
                    }
                    else
                        r.Flags.Add("missing-left-hemistich");

                    if (first != null) first.MarginNumber = r.MarginNumber;
                    foreach (var f in r.Flags.Where(f => f.StartsWith("missing-"))) poem.Flags.Add(f);
                    i++;
                    break;
                }
                case "centered":
                {
                    int j = i;
                    while (j < rows.Count && rows[j].Layout == "centered") j++;
                    int k = i;
                    for (; k + 1 < j; k += 2)
                    {
                        if (proseBook) AddStackedCouplet(poem, ref order, rows[k], rows[k + 1]);
                        else AddCenteredPair(poem, ref order, rows[k], rows[k + 1], rhyme, masnavi);
                    }
                    if (k < j && proseBook) // a lone hemistich quoted in prose
                    {
                        var lone = rows[k];
                        var v = Add(poem, ref order, VersePosition.Single, lone.Text ?? "", lone);
                        v.Flags.Add("odd-hemistich");
                        poem.Flags.Add("odd-hemistich");
                    }
                    else if (k < j) // odd line left over
                    {
                        var lone = rows[k];
                        var v = Add(poem, ref order, VersePosition.CenteredVerse1, lone.Text ?? "", lone);
                        v.MarginNumber = lone.MarginNumber;
                        v.Flags.Add("centered-odd-row");
                        poem.Flags.Add("centered-odd-row");
                    }
                    i = j;
                    break;
                }
                case "paragraph":
                {
                    Add(poem, ref order, VersePosition.Paragraph, r.Text ?? "", r);
                    i++;
                    break;
                }
                default: // single
                {
                    var v = Add(poem, ref order, VersePosition.Single, r.Text ?? "", r);
                    v.MarginNumber = r.MarginNumber;
                    v.Flags.Add("single-line");
                    i++;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// In a book of mixed prose and verse the verses are often printed as stacked hemistichs only because of the
    /// narrow column, so every two stacked lines are one ordinary bayt.
    /// </summary>
    private static void AddStackedCouplet(Poem poem, ref int order, Row first, Row second)
    {
        Add(poem, ref order, VersePosition.Right, first.Text ?? "", first);
        Add(poem, ref order, VersePosition.Left, second.Text ?? "", second);
    }

    private static void AddCenteredPair(Poem poem, ref int order, Row first, Row second, RhymeInfo? rhyme, bool masnavi)
    {
        // A one-letter "rhyme" proves nothing (many words end in the same letter), so it counts as no evidence:
        // the pair stays centered and is flagged for a human decision.
        bool? normal = rhyme == null || rhyme.Weak ? null : RhymeHelper.EndsWithRhyme(second.Text, rhyme);
        string normalFlag = "centered-treated-as-normal";

        if (normal != true && masnavi)
        {
            if (RhymeHelper.CommonEnding(first.Text, second.Text) >= MasnaviMinCommonEnding)
            {
                normal = true;
                normalFlag = "centered-treated-as-normal-couplet-rhyme";
            }
            else
                normal ??= false;
        }

        if (normal == true)
        {
            var a = Add(poem, ref order, VersePosition.Right, first.Text ?? "", first);
            var b = Add(poem, ref order, VersePosition.Left, second.Text ?? "", second);
            a.MarginNumber = first.MarginNumber; b.MarginNumber = second.MarginNumber;
            a.Flags.Add(normalFlag); b.Flags.Add(normalFlag);
            poem.Flags.Add(normalFlag);
        }
        else
        {
            var a = Add(poem, ref order, VersePosition.CenteredVerse1, first.Text ?? "", first);
            var b = Add(poem, ref order, VersePosition.CenteredVerse2, second.Text ?? "", second);
            a.MarginNumber = first.MarginNumber; b.MarginNumber = second.MarginNumber;
            string flag = normal == null ? "centered-unverified" : "centered-kept";
            a.Flags.Add(flag); b.Flags.Add(flag);
            poem.Flags.Add(flag);
        }
    }

    private static Verse Add(Poem poem, ref int order, VersePosition pos, string text, Row row)
    {
        var v = new Verse { Order = order++, Position = pos, Text = text, PdfPage = row.PdfPage };
        foreach (var f in row.Flags.Where(f => !f.StartsWith("missing-"))) v.Flags.Add(f);

        if (row.UncertainWords.Count > 0)
        {
            // specific words worth a second look: this is what the reviewer should check
            v.Flags.Add("unsure-word:" + string.Join("،", row.UncertainWords));
            poem.Flags.Add("unsure-word");
        }
        else if (row.Uncertain)
        {
            // legacy, imprecise flag: kept on the verse but kept out of the poem-level report
            v.Flags.Add("model-uncertain");
        }
        poem.Verses.Add(v);
        return v;
    }
}
