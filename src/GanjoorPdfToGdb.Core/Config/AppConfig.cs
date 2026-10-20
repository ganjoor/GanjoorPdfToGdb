using System.Text.Json;

namespace GanjoorPdfToGdb;

/// <summary>
/// Settings for a conversion run. Loaded from a JSON file (optional) and overridden by
/// environment variables, so the API key never has to be stored in a file.
/// </summary>
public sealed class AppConfig
{
    // ---- OpenAI-compatible endpoint ----
    // BaseUrl, ApiKey and Model have their own fields in the main window, so they are hidden in the settings grid.
    [System.ComponentModel.Browsable(false)]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    [System.ComponentModel.Browsable(false)]
    public string? ApiKey { get; set; }
    [System.ComponentModel.Browsable(false)]
    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0;
    public int MaxOutputTokens { get; set; } = 8000;
    /// <summary>Name of the token-limit parameter ("max_tokens" or "max_completion_tokens").</summary>
    public string MaxTokensParameter { get; set; } = "max_tokens";
    /// <summary>Ask for response_format=json_object. Automatically disabled if the endpoint rejects it.</summary>
    public bool UseJsonResponseFormat { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 300;

    // ---- Rendering ----
    public int RenderDpi { get; set; } = 200;

    // ---- Output conventions ----
    /// <summary>Title used when a poem heading carries only a number. {number} and {category} are available.</summary>
    public string PoemTitleTemplate { get; set; } = "شمارهٔ {number}";
    /// <summary>Title used for verses that appear with no heading at all. {n} is a running counter.</summary>
    public string UntitledPoemTemplate { get; set; } = "بدون عنوان {n}";
    /// <summary>Append a counter to repeated titles inside one category (e.g. several «قطعه»).</summary>
    public bool DisambiguateDuplicateTitles { get; set; } = true;
    /// <summary>Convert «ۀ» (U+06C0) to «ه» + hamza above (U+0654), as used for ezafe in Ganjoor text.</summary>
    public bool NormalizeHeYeToHamza { get; set; } = true;
    /// <summary>Category used for verses that appear before any section heading.</summary>
    public string UnknownCategoryTitle { get; set; } = "نامشخص";
    public int FirstPoetId { get; set; } = 1001;
    public int FirstCatId { get; set; } = 10001;
    public int FirstPoemId { get; set; } = 100001;

    /// <summary>
    /// Running headers that may switch the current category when no explicit heading was detected.
    /// Headings the model reports as category_heading are always accepted and added to this set.
    /// </summary>
    public List<string> KnownSections { get; set; } = new()
    {
        "قصاید", "قصیده", "غزلیات", "قطعات", "رباعیات", "مثنویات", "ترکیبات", "ترکیب‌بندها",
        "ترجیعات", "ترجیع‌بندها", "مسمطات", "مفردات", "دوبیتی", "دوبیتیها"
    };

    public static AppConfig Load(string? path)
    {
        var cfg = new AppConfig();
        if (!string.IsNullOrWhiteSpace(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            var root = doc.RootElement;
            cfg.BaseUrl = Str(root, cfg.BaseUrl, "BaseUrl", "OpenAIBaseUrl")!;
            cfg.ApiKey = Str(root, cfg.ApiKey, "ApiKey", "OpenAIAPIKey");
            cfg.Model = Str(root, cfg.Model, "Model", "OpenAIModel", "OpenAITitleModel")!;
            cfg.Temperature = Num(root, cfg.Temperature, "Temperature");
            cfg.MaxOutputTokens = (int)Num(root, cfg.MaxOutputTokens, "MaxOutputTokens");
            cfg.MaxTokensParameter = Str(root, cfg.MaxTokensParameter, "MaxTokensParameter")!;
            cfg.UseJsonResponseFormat = Bool(root, cfg.UseJsonResponseFormat, "UseJsonResponseFormat");
            cfg.MaxRetries = (int)Num(root, cfg.MaxRetries, "MaxRetries");
            cfg.RequestTimeoutSeconds = (int)Num(root, cfg.RequestTimeoutSeconds, "RequestTimeoutSeconds");
            cfg.RenderDpi = (int)Num(root, cfg.RenderDpi, "RenderDpi");
            cfg.PoemTitleTemplate = Str(root, cfg.PoemTitleTemplate, "PoemTitleTemplate")!;
            cfg.UntitledPoemTemplate = Str(root, cfg.UntitledPoemTemplate, "UntitledPoemTemplate")!;
            cfg.DisambiguateDuplicateTitles = Bool(root, cfg.DisambiguateDuplicateTitles, "DisambiguateDuplicateTitles");
            cfg.NormalizeHeYeToHamza = Bool(root, cfg.NormalizeHeYeToHamza, "NormalizeHeYeToHamza");
            cfg.UnknownCategoryTitle = Str(root, cfg.UnknownCategoryTitle, "UnknownCategoryTitle")!;
            cfg.FirstPoetId = (int)Num(root, cfg.FirstPoetId, "FirstPoetId");
            cfg.FirstCatId = (int)Num(root, cfg.FirstCatId, "FirstCatId");
            cfg.FirstPoemId = (int)Num(root, cfg.FirstPoemId, "FirstPoemId");
            if (Find(root, "KnownSections") is { ValueKind: JsonValueKind.Array } arr)
                cfg.KnownSections = arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
        }

        // Environment variables win over the file.
        cfg.ApiKey = Env("GDB_API_KEY") ?? Env("OPENAI_API_KEY") ?? cfg.ApiKey;
        cfg.BaseUrl = Env("GDB_BASE_URL") ?? cfg.BaseUrl;
        cfg.Model = Env("GDB_MODEL") ?? cfg.Model;
        return cfg;
    }

    /// <summary>Independent copy (the lists are copied too).</summary>
    public AppConfig Clone()
    {
        var c = (AppConfig)MemberwiseClone();
        c.KnownSections = new List<string>(KnownSections);
        return c;
    }

    /// <summary>Saves the settings as JSON that <see cref="Load"/> can read back. The API key is only written if asked.</summary>
    public void Save(string path, bool includeApiKey)
    {
        var copy = (AppConfig)MemberwiseClone();
        if (!includeApiKey) copy.ApiKey = null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(copy, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }), new System.Text.UTF8Encoding(false));
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static JsonElement? Find(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in root.EnumerateObject())
            foreach (var n in names)
                if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
        return null;
    }

    private static string? Str(JsonElement root, string? def, params string[] names)
        => Find(root, names) is { ValueKind: JsonValueKind.String } e ? e.GetString() : def;

    private static double Num(JsonElement root, double def, params string[] names)
        => Find(root, names) is { ValueKind: JsonValueKind.Number } e ? e.GetDouble() : def;

    private static bool Bool(JsonElement root, bool def, params string[] names)
        => Find(root, names) is { } e && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : def;
}
