using System.Text;
using System.Text.RegularExpressions;

namespace GanjoorPdfToGdb;

public static class TextNormalizer
{
    // (۱)  (1)  (١)  [۲]  — footnote markers that sometimes leak into verse text
    private static readonly Regex FootnoteMarker = new(@"[\(\[]\s*[0-9۰-۹٠-٩]{1,2}\s*[\)\]]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t ]+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes OCR text for Ganjoor: Persian ی/ک, Persian digits, no tatweel or direction marks,
    /// single spaces, optional ۀ → ه+ٔ. Returns true in <paramref name="removedFootnoteMarker"/> if a marker was stripped.
    /// </summary>
    public static string Normalize(string? text, AppConfig cfg, out bool removedFootnoteMarker)
    {
        removedFootnoteMarker = false;
        if (string.IsNullOrEmpty(text)) return "";

        if (FootnoteMarker.IsMatch(text))
        {
            removedFootnoteMarker = true;
            text = FootnoteMarker.Replace(text, " ");
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case 'ي': case 'ى': sb.Append('ی'); break;          // Arabic ye / alef maksura
                case 'ك': sb.Append('ک'); break;                    // Arabic kaf
                case 'ـ': break;                                    // tatweel
                case '‎': case '‏': case '‪': case '‫':
                case '‬': case '‭': case '‮': case '﻿': break; // direction marks / BOM
                case 'ۀ':                                      // ۀ
                    if (cfg.NormalizeHeYeToHamza) { sb.Append('ه').Append('ٔ'); }
                    else sb.Append(c);
                    break;
                default:
                    if (c >= '٠' && c <= '٩') sb.Append((char)('۰' + (c - '٠')));   // Arabic-Indic → Persian digits
                    else if (c >= '0' && c <= '9') sb.Append((char)('۰' + (c - '0')));
                    else sb.Append(c);
                    break;
            }
        }

        string s = Spaces.Replace(sb.ToString(), " ").Trim();
        return s;
    }

    public static string Normalize(string? text, AppConfig cfg) => Normalize(text, cfg, out _);

    /// <summary>Loose key for comparing headings: no spaces, ZWNJ or diacritics.</summary>
    public static string LooseKey(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || c == '‌') continue;
            var cat = char.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (!char.IsLetterOrDigit(c)) continue;
            sb.Append(c == 'ي' || c == 'ى' ? 'ی' : c == 'ك' ? 'ک' : c);
        }
        return sb.ToString();
    }

    /// <summary>Digits (any script) → int, or null.</summary>
    public static int? ParseNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9') sb.Append(c);
            else if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
        }
        return sb.Length > 0 && sb.Length <= 9 && int.TryParse(sb.ToString(), out int n) ? n : null;
    }

    public static string ToPersianDigits(int n)
        => new(n.ToString().Select(c => (char)('۰' + (c - '0'))).ToArray());
}
