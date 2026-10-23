namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// Makes a form open large enough to show all of its content (also on high-DPI screens and in virtual machines),
/// never larger than the screen, and adds scrollbars when the window is later made smaller than the content.
/// </summary>
internal static class FormFit
{
    public static void Attach(Form form, Control root, Control growing, int minGrowingHeight = 170)
    {
        growing.MinimumSize = new Size(0, minGrowingHeight);
        form.AutoScroll = true;
        form.Load += (_, _) => Fit(form, root);
    }

    private static void Fit(Form form, Control root)
    {
        var wa = Screen.FromControl(form).WorkingArea;
        int width = Math.Min(Math.Max(form.ClientSize.Width, form.MinimumSize.Width), wa.Width);
        var pref = root.GetPreferredSize(new Size(width, 0));
        int needed = pref.Height + 4;

        // space taken by title bar and borders
        int frameH = form.Height - form.ClientSize.Height;
        int frameW = form.Width - form.ClientSize.Width;
        int height = Math.Min(needed, wa.Height - frameH);

        form.AutoScrollMinSize = new Size(0, needed);
        form.ClientSize = new Size(Math.Min(width, wa.Width - frameW), Math.Max(height, 300));
        form.Left = wa.Left + (wa.Width - form.Width) / 2;
        form.Top = Math.Max(wa.Top, wa.Top + (wa.Height - form.Height) / 2);
    }
}
