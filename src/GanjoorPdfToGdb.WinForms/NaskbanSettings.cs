using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// What the "link to ganjoor" window remembers (%AppData%\GanjoorPdfToGdb\naskban.json). The password is only stored when
/// the user asks for it, and then encrypted with Windows DPAPI (readable only by the same Windows user on this computer).
/// </summary>
public sealed class NaskbanSettings
{
    public string GdbPath { get; set; } = "";
    public string JsonPath { get; set; } = "";
    public string BookId { get; set; } = "";
    public bool AllPages { get; set; } = true;
    public bool SuggestedByMachine { get; set; }
    public bool Remember { get; set; }
    public string UserName { get; set; } = "";
    /// <summary>DPAPI-protected password, base64.</summary>
    public string PasswordProtected { get; set; } = "";

    private static string FilePath => Path.Combine(UiSettings.Dir, "naskban.json");

    public static NaskbanSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<NaskbanSettings>(File.ReadAllText(FilePath)) ?? new NaskbanSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return new NaskbanSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(UiSettings.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public string GetPassword()
    {
        if (string.IsNullOrEmpty(PasswordProtected)) return "";
        try
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return ""; }
    }

    public void SetPassword(string password)
    {
        PasswordProtected = string.IsNullOrEmpty(password)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
    }
}
