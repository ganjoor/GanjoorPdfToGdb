namespace GanjoorPdfToGdb.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Anything unexpected is shown in a copyable error window instead of crashing silently.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            ErrorDialog.ShowError(null, "خطای پیش‌بینی‌نشده", e.Exception.ToString());

        Application.Run(new MainForm());
    }
}
