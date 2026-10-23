using System.Diagnostics;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// Links the poems of a published .gdb to the pages of the PDF on naskban.ir, using the .pages.json written by the
/// converter. All the work is done by <see cref="LinkJob"/> (the command line tool's "link" command uses the same code).
/// </summary>
public sealed class NaskbanLinkForm : Form
{
    private readonly TextBox _gdb = new();
    private readonly TextBox _json = new();
    private readonly TextBox _bookId = new();
    private readonly TextBox _user = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _remember = new();
    private readonly CheckBox _allPages = new();
    private readonly CheckBox _machine = new();

    private readonly Button _btnPreview = new();
    private readonly Button _btnSend = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnReport = new();
    private readonly List<Control> _lockWhileRunning = new();

    private readonly ProgressBar _bar = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();

    private readonly NaskbanSettings _s;
    private CancellationTokenSource? _cts;
    private bool _running;
    private string? _lastReport;

    public NaskbanLinkForm()
    {
        _s = NaskbanSettings.Load();

        Icon = AppIcon.Get();
        Text = "پیوند شعرهای گنجور به صفحه‌های PDF در نسک‌بان";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 700);
        MinimumSize = new Size(640, 420);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);

        BuildLayout();
        LoadFields();
        _json.Leave += (_, _) => SuggestBookId();
        _btnPreview.Click += async (_, _) => await PreviewAsync();
        _btnSend.Click += async (_, _) => await SendAsync();
        _btnStop.Click += (_, _) => { _status.Text = "در حال توقف… (پیوندهای فرستاده‌شده یادآوری می‌شوند)"; _cts?.Cancel(); };
        _btnReport.Click += (_, _) => OpenReport();
        FormClosing += (_, e) =>
        {
            if (_running)
            {
                e.Cancel = true;
                MessageBox.Show(this, "ابتدا کار جاری را متوقف کنید.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                return;
            }
            SaveFields();
        };
        UpdateRunningState(false);
        _status.Text = "آماده";
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // files + book
        var files = NewGrid();
        var browseGdb = NewButton("انتخاب…");
        browseGdb.Click += (_, _) => Browse(_gdb, "انتخاب فایل gdb (خروجی گرفته‌شده از گنجور)", "GDB (*.gdb)|*.gdb|همهٔ فایل‌ها (*.*)|*.*");
        var browseJson = NewButton("انتخاب…");
        browseJson.Click += (_, _) =>
        {
            Browse(_json, "انتخاب فایل JSON مبدل (*.pages.json)", "JSON (*.json)|*.json|همهٔ فایل‌ها (*.*)|*.*");
            SuggestBookId();
        };
        MakeLtr(_gdb); MakeLtr(_json); MakeLtr(_bookId);
        AddRow(files, "فایل gdb گنجور", _gdb, browseGdb);
        AddRow(files, "فایل JSON مبدل", _json, browseJson);
        AddRow(files, "شمارهٔ کتاب در نسک‌بان", _bookId, null);
        AddWide(files, Hint("شمارهٔ کتاب همان عدد نشانی کتاب است: برای naskban.ir/24017 عدد ۲۴۰۱۷. شمارهٔ صفحه‌ها همان شمارهٔ صفحهٔ PDF است (از ۱)."));
        var filesGroup = NewGroup("کتاب", files);

        // account
        var acct = NewGrid();
        MakeLtr(_user); MakeLtr(_password);
        _password.UseSystemPasswordChar = true;
        _remember.Text = "ذخیرهٔ نام کاربری و رمز روی این رایانه (رمزگذاری‌شده برای حساب ویندوز شما)";
        _remember.AutoSize = true;
        _remember.Anchor = AnchorStyles.Right;
        AddRow(acct, "نام کاربری نسک‌بان", _user, null);
        AddRow(acct, "رمز عبور", _password, null);
        AddWide(acct, _remember);
        var acctGroup = NewGroup("حساب نسک‌بان", acct);

        // options
        var opts = NewGrid();
        _allPages.Text = "هر شعر به همهٔ صفحه‌هایش پیوند شود (اگر برداشته شود فقط صفحهٔ اول شعر)";
        _allPages.AutoSize = true;
        _allPages.Anchor = AnchorStyles.Right;
        _machine.Text = "پیشنهادها به‌عنوان «پیشنهاد ماشینی» ثبت شوند";
        _machine.AutoSize = true;
        _machine.Anchor = AnchorStyles.Right;
        AddWide(opts, _allPages);
        AddWide(opts, _machine);
        AddWide(opts, Hint("پیوندها به صف بازبینی نسک‌بان می‌روند. اگر دوباره اجرا شود، پیوندهای فرستاده‌شده تکرار نمی‌شوند."));
        var optsGroup = NewGroup("گزینه‌ها", opts);

        // buttons
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
        SetupButton(_btnSend, "ارسال پیوندها");
        _btnSend.Font = new Font(Font, FontStyle.Bold);
        SetupButton(_btnPreview, "بررسی تطبیق (بدون ارسال)");
        SetupButton(_btnStop, "توقف");
        SetupButton(_btnReport, "باز کردن گزارش");
        buttons.Controls.AddRange(new Control[] { _btnSend, _btnPreview, _btnStop, _btnReport });

        // progress
        var progress = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progress.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progress.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bar.Dock = DockStyle.Fill;
        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _bar.Height = 20;
        _status.AutoSize = true;
        _status.Anchor = AnchorStyles.Right;
        progress.Controls.Add(_bar, 0, 0);
        progress.Controls.Add(_status, 0, 1);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.WordWrap = true;
        _log.Dock = DockStyle.Fill;
        _log.RightToLeft = RightToLeft.No;
        _log.Font = new Font("Consolas", 9.5f);

        root.Controls.Add(filesGroup, 0, 0);
        root.Controls.Add(acctGroup, 0, 1);
        root.Controls.Add(optsGroup, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(progress, 0, 4);
        root.Controls.Add(_log, 0, 5);
        Controls.Add(root);
        FormFit.Attach(this, root, _log);

        _lockWhileRunning.AddRange(new Control[]
        {
            _gdb, _json, _bookId, _user, _password, _remember, _allPages, _machine, browseGdb, browseJson
        });
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Anchor = AnchorStyles.Right,
        Margin = new Padding(3, 0, 3, 6)
    };

    private static TableLayoutPanel NewGrid()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    private static GroupBox NewGroup(string title, Control content)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6) };
        g.Controls.Add(content);
        return g;
    }

    private static void AddRow(TableLayoutPanel t, string label, Control input, Control? extra)
    {
        int r = t.RowStyles.Count;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowCount = r + 1;
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 6, 8, 6) };
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(3, 4, 3, 4);
        t.Controls.Add(lbl, 0, r);
        t.Controls.Add(input, 1, r);
        if (extra != null) t.Controls.Add(extra, 2, r);
        else t.SetColumnSpan(input, 2);
    }

    private static void AddWide(TableLayoutPanel t, Control c)
    {
        int r = t.RowStyles.Count;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowCount = r + 1;
        t.Controls.Add(c, 0, r);
        t.SetColumnSpan(c, 3);
    }

    private static Button NewButton(string text)
        => new() { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3) };

    private static void SetupButton(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.Padding = new Padding(10, 4, 10, 4);
        b.Margin = new Padding(4);
    }

    private static void MakeLtr(TextBox t) => t.RightToLeft = RightToLeft.No;

    // ------------------------------------------------------------------ settings

    private void LoadFields()
    {
        _gdb.Text = _s.GdbPath;
        _json.Text = _s.JsonPath;
        _bookId.Text = _s.BookId;
        _allPages.Checked = _s.AllPages;
        _machine.Checked = _s.SuggestedByMachine;
        _remember.Checked = _s.Remember;
        _user.Text = _s.UserName;
        _password.Text = _s.Remember ? _s.GetPassword() : "";
    }

    private void SaveFields()
    {
        _s.GdbPath = _gdb.Text.Trim();
        _s.JsonPath = _json.Text.Trim();
        _s.BookId = _bookId.Text.Trim();
        _s.AllPages = _allPages.Checked;
        _s.SuggestedByMachine = _machine.Checked;
        _s.Remember = _remember.Checked;
        _s.UserName = _remember.Checked ? _user.Text.Trim() : "";
        _s.SetPassword(_remember.Checked ? _password.Text : "");
        _s.Save();
    }

    private void Browse(TextBox target, string title, string filter)
    {
        using var dlg = new OpenFileDialog { Title = title, Filter = filter };
        if (!string.IsNullOrWhiteSpace(target.Text))
        {
            try
            {
                string? dir = Path.GetDirectoryName(target.Text.Trim().Trim('"'));
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            catch (ArgumentException) { }
        }
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
    }

    /// <summary>The converter names the PDF «00024017-name.pdf» when it came from naskban; use that as a first guess.</summary>
    private void SuggestBookId()
    {
        if (!string.IsNullOrWhiteSpace(_bookId.Text)) return;
        string path = _json.Text.Trim().Trim('"');
        if (!File.Exists(path)) return;
        try
        {
            int? id = SidecarData.Load(path).GuessBookId();
            if (id != null) _bookId.Text = id.Value.ToString();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { }
    }

    // ------------------------------------------------------------------ actions

    private LinkOptions? ReadOptions(bool sending)
    {
        var o = new LinkOptions
        {
            GdbPath = _gdb.Text.Trim().Trim('"'),
            SidecarPath = _json.Text.Trim().Trim('"'),
            Username = _user.Text.Trim(),
            Password = _password.Text,
            AllPages = _allPages.Checked,
            SuggestedByMachine = _machine.Checked
        };
        var problems = new List<string>();
        if (!int.TryParse(TextNormalizer.ParseNumber(_bookId.Text)?.ToString(), out int id) || id <= 0)
            problems.Add("شمارهٔ کتاب در نسک‌بان را وارد کنید (مثلاً ۲۴۰۱۷).");
        else o.PdfBookId = id;
        foreach (string p in LinkJob.Validate(o, sending))
        {
            if (p.StartsWith("The Naskban book id")) continue; // already reported above in Persian
            problems.Add(p switch
            {
                "The .gdb file does not exist." => "فایل gdb پیدا نشد.",
                "The .pages.json file does not exist." => "فایل JSON مبدل پیدا نشد.",
                _ => "نام کاربری و رمز عبور نسک‌بان را وارد کنید."
            });
        }
        if (problems.Count > 0)
        {
            ErrorDialog.ShowError(this, "ورودی‌ها کامل یا درست نیستند", string.Join(Environment.NewLine, problems));
            return null;
        }
        return o;
    }

    private async Task PreviewAsync()
    {
        if (_running) return;
        var o = ReadOptions(sending: false);
        if (o == null) return;
        SaveFields();
        Begin("در حال خواندن و تطبیق…");
        try
        {
            LinkJobResult r = await Task.Run(() => LinkJob.Preview(o, UiLog), _cts!.Token);
            _lastReport = r.ReportPath;
            _status.Text = Summary(r);
            UiLog(_status.Text);
        }
        catch (OperationCanceledException) { _status.Text = "متوقف شد."; }
        catch (Exception ex)
        {
            _status.Text = "با خطا متوقف شد.";
            ErrorDialog.ShowError(this, "بررسی تطبیق با خطا متوقف شد", ex.ToString());
        }
        finally { End(); }
    }

    private async Task SendAsync()
    {
        if (_running) return;
        var o = ReadOptions(sending: true);
        if (o == null) return;
        SaveFields();
        Begin("در حال خواندن و تطبیق…");
        try
        {
            LinkJobResult plan = await Task.Run(() => LinkJob.Preview(o, UiLog), _cts!.Token);
            _lastReport = plan.ReportPath;

            string question = $"{Fa(plan.Plan.TotalLinks)} پیوند برای {Fa(plan.Plan.Items.Count)} شعر به کتاب {Fa(o.PdfBookId)} نسک‌بان فرستاده می‌شود." +
                              Environment.NewLine +
                              $"شعرهای بدون تطبیق: {Fa(plan.Plan.UnmatchedSitePoems.Count)} (فهرست در گزارش)." +
                              Environment.NewLine + Environment.NewLine + "ادامه می‌دهید؟";
            var answer = MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            if (answer != DialogResult.Yes) { _status.Text = "ارسال نشد."; return; }

            _status.Text = "در حال ارسال…";
            CancellationToken ct = _cts!.Token;
            LinkJobResult r = await LinkJob.SendAsync(o, UiLog, UiProgress, ct, plan);
            _lastReport = r.ReportPath;
            _status.Text = $"فرستاده شد: {Fa(r.Created)}، از قبل موجود: {Fa(r.AlreadyDone)}، ناموفق: {Fa(r.Failed)}" +
                           (r.Stopped ? " — به‌دلیل خطاهای پیاپی زودتر متوقف شد؛ بعداً دوباره اجرا کنید." : r.Failed > 0 ? " — برای ناموفق‌ها دوباره اجرا کنید." : "");
            UiLog(_status.Text);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "متوقف شد. پیوندهای فرستاده‌شده یادآوری می‌شوند؛ دوباره «ارسال» را بزنید تا ادامه یابد.";
            UiLog("Cancelled.");
        }
        catch (Exception ex)
        {
            _status.Text = "ارسال با خطا متوقف شد.";
            ErrorDialog.ShowError(this, "ارسال با خطا متوقف شد", ex.ToString());
        }
        finally { End(); }
    }

    private static string Summary(LinkJobResult r)
        => $"{Fa(r.Plan.Items.Count)} شعر تطبیق یافت، {Fa(r.Plan.TotalLinks)} پیوند؛ بدون تطبیق: {Fa(r.Plan.UnmatchedSitePoems.Count)} شعر. جزئیات در گزارش.";

    private void OpenReport()
    {
        if (_lastReport == null || !File.Exists(_lastReport))
        {
            MessageBox.Show(this, "هنوز گزارشی ساخته نشده است. ابتدا «بررسی تطبیق» را بزنید.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = _lastReport, UseShellExecute = true });
    }

    private void Begin(string status)
    {
        _log.Clear();
        _bar.Value = 0;
        _status.Text = status;
        _cts = new CancellationTokenSource();
        UpdateRunningState(true);
    }

    private void End()
    {
        _cts?.Dispose();
        _cts = null;
        UpdateRunningState(false);
    }

    private void UpdateRunningState(bool running)
    {
        _running = running;
        foreach (var c in _lockWhileRunning) c.Enabled = !running;
        _btnPreview.Enabled = !running;
        _btnSend.Enabled = !running;
        _btnReport.Enabled = !running;
        _btnStop.Enabled = running;
    }

    private void UiLog(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => _log.AppendText(line + Environment.NewLine))); }
        catch (InvalidOperationException) { }
    }

    private void UiProgress(int done, int total)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                _bar.Maximum = Math.Max(1, total);
                _bar.Value = Math.Min(done, _bar.Maximum);
                _status.Text = $"{Fa(done)} از {Fa(total)} پیوند";
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static string Fa(int n) => TextNormalizer.ToPersianDigits(n);
}
