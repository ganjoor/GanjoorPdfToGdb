namespace GanjoorPdfToGdb.WinForms;

/// <summary>All the less common settings (DPI, title templates, ID ranges, ...) in a property grid.</summary>
public sealed class AdvancedSettingsForm : Form
{
    /// <summary>The edited copy. Only use it when the dialog returned OK.</summary>
    public AppConfig Config { get; }

    public AdvancedSettingsForm(AppConfig current)
    {
        Config = current.Clone();

        Text = "تنظیمات پیشرفته";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 560);
        MinimumSize = new Size(480, 400);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = Config,
            ToolbarVisible = false,
            HelpVisible = true,
            RightToLeft = RightToLeft.No // property names are English
        };

        var ok = new Button { Text = "تأیید", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "انصراف", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.Cancel };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(grid);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
    }
}
