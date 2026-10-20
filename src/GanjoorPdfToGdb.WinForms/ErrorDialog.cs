namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// Errors are shown in a window with selectable text (not a transient message), so the full text
/// stays visible and can be copied.
/// </summary>
public sealed class ErrorDialog : Form
{
    public ErrorDialog(string headline, string details)
    {
        Text = "خطا";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 440);
        MinimumSize = new Size(480, 300);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = headline,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 44,
            Padding = new Padding(10, 10, 10, 0),
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold)
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            RightToLeft = RightToLeft.No, // technical messages are mostly English
            Font = new Font("Consolas", 10f),
            Text = details,
            BackColor = SystemColors.Window
        };

        var copy = new Button { Text = "کپی متن", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(box.Text); } catch (System.Runtime.InteropServices.ExternalException) { }
        };
        var close = new Button { Text = "بستن", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.OK };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);

        // add in this order so that Fill gets the space left between Top and Bottom
        Controls.Add(box);
        Controls.Add(buttons);
        Controls.Add(title);

        AcceptButton = close;
        CancelButton = close;
    }

    public static void ShowError(IWin32Window? owner, string headline, string details)
    {
        using var dlg = new ErrorDialog(headline, details);
        if (owner != null) dlg.ShowDialog(owner); else dlg.ShowDialog();
    }
}
