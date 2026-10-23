using System.Diagnostics;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>Short tips about what to do with the generated files (shown in the main window and after a conversion).</summary>
internal static class NextSteps
{
    public const string DesktopGanjoorUrl = "https://dg.ganjoor.net";
    public const string Email = "ganjoor@ganjoor.net";

    public const string OpenTip =
        "فایل gdb را می‌توانید در گنجور رومیزی (dg.ganjoor.net) باز کنید، بخوانید و غلط‌هایش را اصلاح کنید.";

    public const string SendTip =
        "برای ارسال کار به گنجور، هر دو فایل gdb و json را بفرستید (فایل json برای پیوند دادن شعرها به صفحه‌های کتاب در نسک‌بان لازم است) به نشانی ganjoor@ganjoor.net.";

    public static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no default handler; nothing useful to do */ }
    }

    /// <summary>A LinkLabel whose text contains the two links (site and e-mail).</summary>
    public static LinkLabel NewTipsLabel(bool includeTitle)
    {
        string text = (includeTitle ? "نکته‌ها:\r\n" : "")
            + "• " + OpenTip + "\r\n"
            + "• " + SendTip;
        var l = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 4, 3, 4),
            LinkBehavior = LinkBehavior.HoverUnderline
        };
        int a = text.IndexOf("dg.ganjoor.net", StringComparison.Ordinal);
        if (a >= 0) l.Links.Add(a, "dg.ganjoor.net".Length, DesktopGanjoorUrl);
        int b = text.IndexOf(Email, StringComparison.Ordinal);
        if (b >= 0) l.Links.Add(b, Email.Length, "mailto:" + Email);
        l.LinkClicked += (_, e) => { if (e.Link?.LinkData is string s) OpenLink(s); };
        return l;
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
        var tips = NextSteps.NewTipsLabel(false);
        tips.MaximumSize = new Size(600, 0);

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
