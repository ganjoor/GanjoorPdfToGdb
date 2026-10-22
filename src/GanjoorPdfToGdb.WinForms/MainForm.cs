using System.Diagnostics;

namespace GanjoorPdfToGdb.WinForms;

/// <summary>
/// Main window: pick a PDF, enter the poet and the AI service settings, press Start.
/// All real work happens in <see cref="ConversionJob"/> (the same code the command line tool uses).
/// </summary>
public sealed class MainForm : Form
{
    // inputs
    private readonly TextBox _pdf = new();
    private readonly TextBox _out = new();
    private readonly TextBox _poet = new();
    private readonly TextBox _pages = new();
    private readonly TextBox _poppler = new();
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _model = new();
    private readonly TextBox _apiKey = new();
    private readonly CheckBox _saveKey = new();
    private readonly CheckBox _includeProse = new();

    // actions
    private readonly Button _btnStart = new();
    private readonly Button _btnTest = new();
    private readonly Button _btnAssemble = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnAdvanced = new();
    private readonly Button _btnLink = new();
    private readonly List<Control> _lockWhileRunning = new();

    // feedback
    private readonly ProgressBar _bar = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();

    private AppConfig _cfg;
    private readonly UiSettings _ui;
    private CancellationTokenSource? _cts;
    private bool _running;

    public MainForm()
    {
        _ui = UiSettings.Load();
        _cfg = LoadConfig();

        Text = "تبدیل کتاب اسکن‌شده به GDB";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 720);
        MinimumSize = new Size(720, 600);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        AllowDrop = true;

        BuildLayout();
        LoadFieldsFromSettings();
        WireEvents();
        UpdateRunningState(false);
        _status.Text = "آماده";
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // book group
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // service group
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // progress
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // log

        // ---- book group
        var bookGrid = NewGrid();
        var browsePdf = NewButton("انتخاب…");
        browsePdf.Click += (_, _) => BrowsePdf();
        var browseOut = NewButton("انتخاب…");
        browseOut.Click += (_, _) => BrowseOutDir();
        var browsePoppler = NewButton("انتخاب…");
        browsePoppler.Click += (_, _) => BrowsePopplerDir();

        MakeLtr(_pdf); MakeLtr(_out); MakeLtr(_pages); MakeLtr(_poppler);
        AddRow(bookGrid, "فایل PDF کتاب", _pdf, browsePdf);
        AddRow(bookGrid, "پوشهٔ خروجی", _out, browseOut);
        AddRow(bookGrid, "نام شاعر", _poet, null);
        AddRow(bookGrid, "محدودهٔ صفحات", _pages, null);
        AddRow(bookGrid, "پوشهٔ poppler (اختیاری)", _poppler, browsePoppler);
        var hint = new Label
        {
            Text = "محدودهٔ صفحات: «همه» یا مثلاً ۷۴-۱۱۳۰ یا ۹,۷۶,۶۰۰-۶۰۱ (شمارهٔ صفحه در فایل PDF، از ۱).",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 0, 3, 6)
        };
        AddWide(bookGrid, hint);
        _includeProse.Text = "کتاب نثر و نظم دارد (مثل گلستان): بخش‌های نثر هم به‌صورت بند (پاراگراف) وارد شود";
        _includeProse.AutoSize = true;
        _includeProse.Anchor = AnchorStyles.Right;
        AddWide(bookGrid, _includeProse);

        var bookGroup = NewGroup("کتاب", bookGrid);

        // ---- service group
        var svcGrid = NewGrid();
        MakeLtr(_baseUrl); MakeLtr(_model); MakeLtr(_apiKey);
        _apiKey.UseSystemPasswordChar = true;
        _saveKey.Text = "ذخیرهٔ کلید روی این رایانه (بدون رمزگذاری)";
        _saveKey.AutoSize = true;
        _saveKey.Anchor = AnchorStyles.Right;
        _btnAdvanced.Text = "تنظیمات پیشرفته…";
        _btnAdvanced.AutoSize = true;
        _btnAdvanced.Padding = new Padding(6, 2, 6, 2);
        _btnAdvanced.Anchor = AnchorStyles.Right;

        AddRow(svcGrid, "نشانی سرویس (Base URL)", _baseUrl, null);
        AddRow(svcGrid, "نام مدل", _model, null);
        AddRow(svcGrid, "کلید API", _apiKey, null);
        AddWide(svcGrid, _saveKey);
        AddWide(svcGrid, _btnAdvanced);

        var svcGroup = NewGroup("سرویس هوش مصنوعی (سازگار با OpenAI)", svcGrid);

        // ---- buttons
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 6)
        };
        SetupButton(_btnStart, "شروع تبدیل");
        _btnStart.Font = new Font(Font, FontStyle.Bold);
        SetupButton(_btnTest, "آزمایش (۳ صفحهٔ اول محدوده)");
        SetupButton(_btnAssemble, "ساخت دوباره بدون اتصال");
        SetupButton(_btnStop, "توقف");
        SetupButton(_btnOpen, "باز کردن پوشهٔ خروجی");
        SetupButton(_btnLink, "پیوند به گنجور / نسک‌بان…");
        buttons.Controls.AddRange(new Control[] { _btnStart, _btnTest, _btnAssemble, _btnStop, _btnOpen, _btnLink });

        // ---- progress
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

        // ---- log
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.WordWrap = true;
        _log.Dock = DockStyle.Fill;
        _log.RightToLeft = RightToLeft.No; // log lines are technical English
        _log.Font = new Font("Consolas", 9.5f);
        _log.BackColor = SystemColors.Window;

        root.Controls.Add(bookGroup, 0, 0);
        root.Controls.Add(svcGroup, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(progress, 0, 3);
        root.Controls.Add(_log, 0, 4);
        Controls.Add(root);

        _lockWhileRunning.AddRange(new Control[]
        {
            _pdf, _out, _poet, _pages, _poppler, _baseUrl, _model, _apiKey, _saveKey, _includeProse,
            browsePdf, browseOut, browsePoppler, _btnAdvanced
        });
    }

    private static TableLayoutPanel NewGrid()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(6)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // label
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));      // input
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // optional button
        return t;
    }

    private static GroupBox NewGroup(string title, Control content)
    {
        var g = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6)
        };
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
        if (extra != null)
            t.Controls.Add(extra, 2, r);
        else
            t.SetColumnSpan(input, 2);
    }

    /// <summary>A control that spans all three columns (hint text, checkbox, ...).</summary>
    private static void AddWide(TableLayoutPanel t, Control c)
    {
        int r = t.RowStyles.Count;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowCount = r + 1;
        t.Controls.Add(c, 0, r);
        t.SetColumnSpan(c, 3);
    }

    private static Button NewButton(string text)
        => new() { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3, 3, 3, 3) };

    private static void SetupButton(Button b, string text)
    {
        b.Text = text;
        b.AutoSize = true;
        b.Padding = new Padding(10, 4, 10, 4);
        b.Margin = new Padding(4);
    }

    /// <summary>Paths, URLs and keys are left-to-right text, so edit them left-to-right.</summary>
    private static void MakeLtr(TextBox t) => t.RightToLeft = RightToLeft.No;

    // ------------------------------------------------------------------ settings

    private static AppConfig LoadConfig()
    {
        try
        {
            return File.Exists(UiSettings.ConfigPath) ? AppConfig.Load(UiSettings.ConfigPath) : AppConfig.Load(null);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return AppConfig.Load(null);
        }
    }

    private void LoadFieldsFromSettings()
    {
        _pdf.Text = _ui.PdfPath;
        _out.Text = _ui.OutDir;
        _poet.Text = _ui.PoetName;
        _pages.Text = string.IsNullOrWhiteSpace(_ui.PagesSpec) ? "همه" : _ui.PagesSpec;
        _poppler.Text = _ui.PopplerDir;
        _saveKey.Checked = _ui.SaveApiKey;
        _includeProse.Checked = _ui.IncludeProse;

        _baseUrl.Text = _cfg.BaseUrl;
        _model.Text = _cfg.Model;
        _apiKey.Text = _cfg.ApiKey ?? "";
    }

    /// <summary>The settings as currently shown in the window.</summary>
    private AppConfig CurrentConfig()
    {
        var c = _cfg.Clone();
        c.BaseUrl = _baseUrl.Text.Trim();
        c.Model = _model.Text.Trim();
        c.IncludeProse = _includeProse.Checked;
        c.ApiKey = string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text.Trim();
        return c;
    }

    private JobOptions CurrentOptions(JobMode mode, bool trial)
    {
        string poet = _poet.Text.Trim();
        string pdf = _pdf.Text.Trim().Trim('"');
        return new JobOptions
        {
            PdfPath = pdf,
            PoetName = poet,
            OutDir = _out.Text.Trim().Trim('"'),
            PagesSpec = _pages.Text,
            PopplerDir = string.IsNullOrWhiteSpace(_poppler.Text) ? null : _poppler.Text.Trim().Trim('"'),
            Mode = mode,
            MaxPages = trial ? 3 : null,
            FileStem = trial ? JobStem(poet) + " (آزمایش)" : null
        };
    }

    private static string JobStem(string poet) => ConversionJob.Sanitize(poet);

    private void SaveSettings()
    {
        _ui.PdfPath = _pdf.Text.Trim();
        _ui.OutDir = _out.Text.Trim();
        _ui.PoetName = _poet.Text.Trim();
        _ui.PagesSpec = _pages.Text.Trim();
        _ui.PopplerDir = _poppler.Text.Trim();
        _ui.SaveApiKey = _saveKey.Checked;
        _ui.IncludeProse = _includeProse.Checked;
        _ui.Save();

        try
        {
            CurrentConfig().Save(UiSettings.ConfigPath, includeApiKey: _saveKey.Checked);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // convenience only
        }
    }

    // ------------------------------------------------------------------ events

    private void WireEvents()
    {
        _btnStart.Click += async (_, _) => await StartAsync(JobMode.Run, trial: false);
        _btnTest.Click += async (_, _) => await StartAsync(JobMode.Run, trial: true);
        _btnAssemble.Click += async (_, _) => await StartAsync(JobMode.Assemble, trial: false);
        _btnLink.Click += (_, _) =>
        {
            using var dlg = new NaskbanLinkForm();
            dlg.ShowDialog(this);
        };
        _btnStop.Click += (_, _) =>
        {
            _status.Text = "در حال توقف… (صفحه‌های انجام‌شده نگه داشته می‌شوند)";
            _cts?.Cancel();
        };
        _btnOpen.Click += (_, _) => OpenOutputFolder();
        _btnAdvanced.Click += (_, _) => ShowAdvanced();

        // Drag events go to the control under the cursor, so every control must accept drops.
        EnableDropRecursive(this);

        FormClosing += (_, e) =>
        {
            if (_running)
            {
                var answer = MessageBox.Show(this,
                    "تبدیل هنوز در حال انجام است. بسته شود؟\n(صفحه‌های انجام‌شده در حافظهٔ موقت می‌مانند و دفعهٔ بعد ادامه پیدا می‌کنند.)",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                _cts?.Cancel();
            }
            SaveSettings();
        };
    }

    private void EnableDropRecursive(Control parent)
    {
        parent.AllowDrop = true;
        parent.DragEnter += OnDragEnter;
        parent.DragDrop += OnDragDrop;
        foreach (Control child in parent.Controls)
            EnableDropRecursive(child);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = !_running && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_running || e.Data == null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 &&
            files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            SetPdf(files[0]);
    }

    private void BrowsePdf()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "انتخاب فایل PDF",
            Filter = "PDF (*.pdf)|*.pdf|همهٔ فایل‌ها (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetPdf(dlg.FileName);
    }

    private void SetPdf(string path)
    {
        _pdf.Text = path;
        if (string.IsNullOrWhiteSpace(_out.Text))
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            _out.Text = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-gdb");
        }
    }

    private void BrowseOutDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "پوشهٔ خروجی را انتخاب کنید", UseDescriptionForTitle = true };
        if (!string.IsNullOrWhiteSpace(_out.Text) && Directory.Exists(_out.Text)) dlg.InitialDirectory = _out.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _out.Text = dlg.SelectedPath;
    }

    private void BrowsePopplerDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "پوشهٔای را انتخاب کنید که pdftoppm.exe در آن است (معمولاً Library\\bin)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _poppler.Text = dlg.SelectedPath;
    }

    private void ShowAdvanced()
    {
        using var dlg = new AdvancedSettingsForm(CurrentConfig());
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _cfg = dlg.Config;
            _cfg.BaseUrl = _baseUrl.Text.Trim();
            _cfg.Model = _model.Text.Trim();
            _cfg.ApiKey = string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text.Trim();
            SaveSettings();
        }
    }

    private void OpenOutputFolder()
    {
        string dir = _out.Text.Trim().Trim('"');
        if (dir.Length == 0 || !Directory.Exists(dir))
        {
            MessageBox.Show(this, "پوشهٔ خروجی هنوز وجود ندارد.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // ------------------------------------------------------------------ running a job

    private async Task StartAsync(JobMode mode, bool trial)
    {
        if (_running) return;

        AppConfig cfg = CurrentConfig();
        JobOptions opt = CurrentOptions(mode, trial);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(opt.PdfPath)) problems.Add("فایل PDF انتخاب نشده است.");
        else if (!File.Exists(opt.PdfPath)) problems.Add("فایل PDF پیدا نشد: " + opt.PdfPath);
        if (string.IsNullOrWhiteSpace(opt.PoetName)) problems.Add("نام شاعر را وارد کنید.");
        if (string.IsNullOrWhiteSpace(opt.OutDir)) problems.Add("پوشهٔ خروجی را مشخص کنید.");
        if (mode == JobMode.Run)
        {
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl)) problems.Add("نشانی سرویس را وارد کنید.");
            if (string.IsNullOrWhiteSpace(cfg.Model)) problems.Add("نام مدل را وارد کنید.");
            if (string.IsNullOrWhiteSpace(cfg.ApiKey)) problems.Add("کلید API را وارد کنید.");
        }
        if (problems.Count == 0)
        {
            // poppler is needed to turn PDF pages into images
            string? tools = PopplerLocator.Find(opt.PopplerDir);
            if (!PopplerLocator.IsAvailable(tools))
                problems.Add("برنامهٔ poppler پیدا نشد. پوشهٔ poppler را کنار این برنامه بگذارید یا مسیر پوشهٔ آن (جایی که pdftoppm.exe هست) را در کادر «پوشهٔ poppler» وارد کنید.");
        }
        if (problems.Count == 0)
        {
            // fail fast on anything the shared code objects to (page range syntax, URL shape, ...)
            foreach (string p in ConversionJob.Validate(cfg, opt)) problems.Add(p);
        }
        if (problems.Count > 0)
        {
            ErrorDialog.ShowError(this, "ورودی‌ها کامل یا درست نیستند", string.Join(Environment.NewLine, problems));
            return;
        }

        SaveSettings();
        _log.Clear();
        _bar.Value = 0;
        _status.Text = "در حال شروع…";
        UpdateRunningState(true);
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        try
        {
            var job = new ConversionJob(cfg, opt, UiLog, UiProgress);
            // the job does file and database work too, so keep it off the UI thread
            JobResult r = await Task.Run(() => job.RunAsync(ct), ct);

            string summary = $"انجام شد: {Fa(r.Categories)} دسته، {Fa(r.Poems)} شعر، {Fa(r.VerseRows)} مصرع/سطر.";
            if (r.Issues.Count > 0)
                summary += $" موارد نیازمند بازبینی: {Fa(r.Issues.Count)} (در فایل گزارش).";
            if (r.HasFailedPages)
                summary += $" {Fa(r.Stats.PagesFailed)} صفحه ناموفق بود؛ دوباره «شروع» را بزنید تا فقط همان صفحه‌ها دوباره خوانده شوند.";
            _status.Text = summary;
            UiLog("");
            UiLog(summary);
            UiLog(r.GdbPath);
            UiLog(r.SidecarPath);
            UiLog(r.ReportPath);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "متوقف شد. صفحه‌های انجام‌شده در حافظهٔ موقت مانده‌اند؛ «شروع» ادامه می‌دهد.";
            UiLog("Cancelled.");
        }
        catch (Exception ex)
        {
            _status.Text = "تبدیل با خطا متوقف شد.";
            ErrorDialog.ShowError(this, "تبدیل با خطا متوقف شد", ex.ToString());
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            UpdateRunningState(false);
        }
    }

    private void UpdateRunningState(bool running)
    {
        _running = running;
        foreach (var c in _lockWhileRunning) c.Enabled = !running;
        _btnStart.Enabled = !running;
        _btnTest.Enabled = !running;
        _btnAssemble.Enabled = !running;
        _btnOpen.Enabled = !running;
        _btnLink.Enabled = !running;
        _btnStop.Enabled = running;
    }

    // These two are called from the worker thread.
    private void UiLog(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => _log.AppendText(line + Environment.NewLine))); }
        catch (InvalidOperationException) { /* window closed meanwhile */ }
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
                _status.Text = $"{Fa(done)} از {Fa(total)} صفحه";
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static string Fa(int n) => TextNormalizer.ToPersianDigits(n);
}
