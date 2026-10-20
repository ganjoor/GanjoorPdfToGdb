using System.Text.Json;
using System.Text.Json.Serialization;

namespace GanjoorPdfToGdb;

/// <summary>What the vision model returns for one page (see PagePrompts for the contract).</summary>
public sealed class PageResult
{
    /// <summary>poetry | prose | contents | index | title | blank | other | error</summary>
    public string PageKind { get; set; } = "other";
    public string? PrintedPageLabel { get; set; }
    public string? RunningHeader { get; set; }
    public bool StartsMidPoem { get; set; }
    public List<Block> Blocks { get; set; } = new();
    public string? Notes { get; set; }

    // Filled in by the pipeline, not by the model.
    public int PdfPage { get; set; }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new StringOrNumberConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };
}

/// <summary>
/// category_heading | poem_heading | verse_row | footnote | prose | ornament
/// </summary>
public sealed class Block
{
    public string Type { get; set; } = "";
    /// <summary>two_column | centered | single (verse_row only)</summary>
    public string? Layout { get; set; }
    public string? Text { get; set; }
    public string? Right { get; set; }
    public string? Left { get; set; }
    /// <summary>Poem number printed in a poem_heading.</summary>
    public string? Number { get; set; }
    /// <summary>Verse counter printed in the margin at the height of this row.</summary>
    public string? MarginNumber { get; set; }
    /// <summary>Legacy flag: the model was unsure about something in this row (not precise, see UncertainWords).</summary>
    public bool Uncertain { get; set; }
    /// <summary>The specific words (as transcribed) the model is genuinely unsure about.</summary>
    public List<string>? UncertainWords { get; set; }
}

/// <summary>Models sometimes return numbers where strings are expected (and vice versa).</summary>
public sealed class StringOrNumberConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException("Unexpected token " + reader.TokenType)
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
