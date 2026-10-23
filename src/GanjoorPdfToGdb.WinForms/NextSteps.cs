using System.Diagnostics;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>Short tips about what to do with the generated files (shown in the main window and after a conversion).</summary>
internal static class NextSteps
{
    public const string DesktopGanjoorUrl = "https://dg.ganjoor.net";
    public const string Email = "ganjoor@ganjoor.net";

    public static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no default handler; nothing useful to do */ }
    }

    // Latin addresses are kept out of the Persian sentences (in their own left-to-right link labels),
    // because mixing them into right-to-left text makes parentheses, dots and link ranges display wrongly.
    public static Control NewTipsPanel(bool includeTitle)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0, 6, 0, 6)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Add(Control c) { c.Anchor = AnchorStyles.Right; t.Controls.Add(c); }
        Label Para(string s, bool bold = false)
        {
            var l = new Label { Text = s, AutoSize = true, MaximumSize = new Size(740, 0), Margin = new Padding(3, 2, 3, 2) };
            if (bold) l.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            return l;
        }
        Control LinkRow(string caption, string shown, string target)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(3, 0, 3, 4)
            };
            var cap = new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            var link = new LinkLabel
            {
                Text = shown,
                AutoSize = true,
                RightToLeft = RightToLeft.No,
                Margin = new Padding(3, 3, 3, 3),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            link.LinkClicked += (_, _) => OpenLink(target);
            row.Controls.Add(cap);   // first control = rightmost
            row.Controls.Add(link);
            return row;
        }

        if (includeTitle) Add(Para("نکته‌ها", true));
        Add(Para("• فایل gdb را می‌توانید با گنجور رومیزی باز کنید، بخوانید و غلط‌هایش را اصلاح کنید."));
        Add(LinkRow("دریافت گنجور رومیزی:", "dg.ganjoor.net", DesktopGanjoorUrl));
        Add(Para("• برای ارسال کار به گنجور، هر دو فایل gdb و json را بفرستید (فایل json برای پیوند دادن شعرها به صفحه‌های کتاب در نسک‌بان لازم است)."));
        Add(LinkRow("نشانی ایمیل:", Email, "mailto:" + Email));
        return t;
    }
}

/// <summary>Shown after a successful conversion.</summary>
public sealed class NextStepsDialog : Form
{
    private NextStepsDialog(string outDir)
    {
        Text = "تبدیل انجام شد";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 260);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 10f);

        var head = new Label
        {
            Text = "فایل‌ها ساخته شد. اکنون چه کار کنیم؟",
            AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 0, 3, 8)
        };
        var tips = NextSteps.NewTipsPanel(false);

        var open = new Button { Text = "باز کردن پوشهٔ خروجی", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        open.Click += (_, _) => NextSteps.OpenLink(outDir);
        var site = new Button { Text = "صفحهٔ گنجور رومیزی", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        site.Click += (_, _) => NextSteps.OpenLink(NextSteps.DesktopGanjoorUrl);
        var close = new Button { Text = "بستن", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.OK };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange(new Control[] { close, open, site });

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16)
        };
        flow.Controls.AddRange(new Control[] { head, tips, buttons });
        Controls.Add(flow);
        AcceptButton = close;
        CancelButton = close;
    }

    public static void ShowFor(IWin32Window owner, string outDir)
    {
        using var d = new NextStepsDialog(outDir);
        d.ShowDialog(owner);
    }
}
