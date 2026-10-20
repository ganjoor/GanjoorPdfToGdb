using System.Text.Json;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>What the main window remembers between runs (%AppData%\GanjoorPdfToGdb\ui.json).</summary>
public sealed class UiSettings
{
    public string PdfPath { get; set; } = "";
    public string OutDir { get; set; } = "";
    public string PoetName { get; set; } = "";
    public string PagesSpec { get; set; } = "";
    public string PopplerDir { get; set; } = "";
    public bool SaveApiKey { get; set; }

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GanjoorPdfToGdb");
    public static string UiPath => Path.Combine(Dir, "ui.json");
    public static string ConfigPath => Path.Combine(Dir, "config.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(UiPath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(UiPath)) ?? new UiSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // a broken settings file must never stop the app from starting
        }
        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(UiPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings are a convenience; ignore failures
        }
    }
}
