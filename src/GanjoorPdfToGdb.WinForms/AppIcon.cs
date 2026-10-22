namespace GanjoorPdfToGdb.WinForms;

/// <summary>The application icon (embedded in the exe by &lt;ApplicationIcon&gt;), also used for the window title bars.</summary>
internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon? Get()
    {
        try { return _icon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return null; }
    }
}
