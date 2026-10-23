using System.Diagnostics;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// A small window with tips about what to do with the generated files. It is laid out by hand with
/// RightToLeft text but a normal (non-mirrored) coordinate system, so that the Persian sentences are
/// right-aligned while the Latin links sit on their own left-to-right lines.
/// </summary>
public sealed class TipsDialog : Form
{
    public const string DesktopGanjoorUrl = "https://dg.ganjoor.net";
    public const string Email = "ganjoor@ganjoor.net";

    private const int Margin = 16;
    private const int ContentWidth = 608;

    private int _y = Margin;

    private TipsDialog(string title, string? outDir)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = false; // keep coordinates unmirrored; text direction is still right-to-left
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Font;

        AddHeading("بعد از تبدیل، با این فایل‌ها چه کار کنیم؟");
        AddParagraph("• فایل gdb را می‌توانید با «گنجور رومیزی» باز کنید، شعرها را بخوانید و غلط‌هایش را اصلاح کنید. برنامهٔ گنجور رومیزی را از نشانی زیر بگیرید:");
        AddLink(DesktopGanjoorUrl, "dg.ganjoor.net");
        AddParagraph("• اگر می‌خواهید کارتان در گنجور منتشر شود، هر دو فایل gdb و json را بفرستید؛ فایل json برای پیوند دادن شعرها به صفحه‌های کتاب در نسک‌بان لازم است. نشانی ایمیل:");
        AddLink("mailto:" + Email, Email);

        var close = new Button { Text = "بستن", AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.OK };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(Margin, 6, Margin, 6),
            FlowDirection = FlowDirection.RightToLeft // first control = rightmost
        };
        if (!string.IsNullOrEmpty(outDir))
        {
            var open = new Button { Text = "باز کردن پوشهٔ خروجی", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
            open.Click += (_, _) => OpenLink(outDir);
            buttons.Controls.Add(open);
        }
        buttons.Controls.Add(close);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        ClientSize = new Size(Margin * 2 + ContentWidth, _y + 8 + buttons.Height);
    }

    private void AddHeading(string text)
    {
        var l = new Label
        {
            Text = text,
            AutoSize = false,
            RightToLeft = RightToLeft.Yes,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(Margin, _y),
            Size = new Size(ContentWidth, 30)
        };
        Controls.Add(l);
        _y += l.Height + 6;
    }

    private void AddParagraph(string text)
    {
        int h = TextRenderer.MeasureText(text, Font, new Size(ContentWidth, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft | TextFormatFlags.NoPadding).Height + 6;
        var l = new Label
        {
            Text = text,
            AutoSize = false,
            RightToLeft = RightToLeft.Yes,
            Location = new Point(Margin, _y),
            Size = new Size(ContentWidth, h)
        };
        Controls.Add(l);
        _y += h + 2;
    }

    /// <summary>A link on its own line, left-to-right, aligned to the left edge.</summary>
    private void AddLink(string target, string shown)
    {
        var link = new LinkLabel
        {
            Text = shown,
            AutoSize = true,
            RightToLeft = RightToLeft.No,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Location = new Point(Margin + 24, _y)
        };
        link.LinkClicked += (_, _) => OpenLink(target);
        Controls.Add(link);
        _y += link.PreferredHeight + 14;
    }

    public static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no default handler; nothing useful to do */ }
    }

    public static void ShowFor(IWin32Window owner, string title, string? outDir)
    {
        using var d = new TipsDialog(title, outDir);
        d.ShowDialog(owner);
    }
}
