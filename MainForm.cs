using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dzherelo;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private List<Source> _sources = new();
    private readonly Dictionary<string, TreeNode> _nodes = new();
    private readonly Dictionary<string, SourceResult> _results = new();
    private readonly System.Text.StringBuilder _logBuffer = new();
    private string _lastRunId = "";
    private CancellationTokenSource? _cts;
    private bool _running;

    private Button _btnRun = null!, _btnStop = null!, _btnBrowse = null!, _btnOpen = null!, _btnReport = null!, _btnBrowser = null!, _btnLog = null!, _btnSettings = null!;
    private CheckBox _chkNew = null!;
    private NumericUpDown _numSince = null!;
    private TextBox _txtOut = null!, _txtLog = null!;
    private TreeView _tree = null!;
    private ListView _list = null!;
    private ProgressBar _prog = null!;
    private SplitContainer _body = null!, _right = null!;
    private Label _lblState = null!;
    private ListView _topics = null!;
    private Panel _pageSources = null!, _pageTopics = null!;
    private Button _segSources = null!, _segTopics = null!;
    private Label _lblSubtitle = null!;
    private readonly TaskbarProgress _taskbar = new();

    public MainForm()
    {
        Text = "Dzherelo";                     // назва без хвостів, як домовлено
        Icon = Program.AppIcon();             // та сама іконка, що в exe
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 800);
        MinimumSize = new Size(980, 640);
        BuildUi();
        ApplyTheme();
        Theme.Changed += ApplyTheme;
        LoadSources();
        Load += (_, _) =>
        {
            var root = App.Root(null);
            if (_txtOut.Text.Length == 0) _txtOut.Text = root;
            _taskbar.Attach(Handle);
            _tree.ExpandAll();
            LayoutSplitters();
            Log("Готово. Познач джерела й натисни «ЗІБРАТИ».");
            Log("Стан колекції: " + CountCollection());
            RefreshTopicsFromDisk();
        };
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += OnFormClosing;
    }

    private void BuildUi()
    {
        SuspendLayout();
        BackColor = Theme.PageBg;
        ForeColor = Theme.TextPrimary;
        Padding = new Padding(0);

        // ---- шапка (як у SettingsForm Valera) ----
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.PageBg };
        header.Controls.Add(Ui.Title("Dzherelo", 20, 14, 420));
        _lblSubtitle = Ui.Caption("Збирач відкритих документів", 22, 48, 620);
        header.Controls.Add(_lblSubtitle);

        // ---- панель дій ----
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
            Padding = new Padding(18, 2, 18, 8), BackColor = Theme.PageBg,
        };
        _btnRun = new Button { Text = "ЗІБРАТИ", Width = 150, Height = 36, Margin = new Padding(0, 0, 8, 0) };
        _btnStop = NavBtn("Стоп");
        _btnSettings = NavBtn("Параметри…");
        _btnOpen = NavBtn("Тека");
        _btnReport = NavBtn("Звіт");
        _btnLog = NavBtn("Зберегти лог");
        _btnBrowser = NavBtn("Браузер (вхід)");

        _chkNew = new CheckBox
        {
            Text = "лише нове", Checked = true, AutoSize = true, Font = Theme.Body,
            Margin = new Padding(20, 11, 0, 0), ForeColor = Theme.TextPrimary,
        };
        var lblSince = new Label { Text = "з року:", AutoSize = true, Font = Theme.Body, Margin = new Padding(18, 11, 4, 0), ForeColor = Theme.TextSecondary };
        _numSince = new NumericUpDown
        {
            Minimum = 1990, Maximum = 2035, Value = 2022, Width = 72, Font = Theme.Body,
            Margin = new Padding(0, 7, 0, 0), BorderStyle = BorderStyle.FixedSingle,
        };
        bar.Controls.AddRange(new Control[] { _btnRun, _btnStop, _btnSettings, _btnOpen, _btnReport, _btnLog, _btnBrowser, lblSince, _numSince, _chkNew });

        // ---- тека колекції ----
        var pathRow = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(18, 0, 18, 10), BackColor = Theme.PageBg };
        // Картка шляху: явні координати + Anchor — докінг усередині Card давав нульову висоту полю.
        var pathCard = new Card { Dock = DockStyle.Fill };
        var lblPathCap = new Label { Text = "Куди збирати документи", Left = 14, Top = 5, Width = 240, Height = 16, Font = Theme.Caption, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
        _lblState = new Label { Text = "—", Left = 14, Top = 58, Width = 600, Height = 16, Font = Theme.Caption, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
        _txtOut = new TextBox { Left = 14, Top = 25, Width = 400, Height = 26, Text = _settings.OutputDir ?? "", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        Ui.StyleInput(_txtOut);
        _btnBrowse = new Button { Text = "Огляд…", Left = 420, Top = 21, Width = 92, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        Ui.StyleSecondary(_btnBrowse);
        pathCard.Controls.Add(lblPathCap);
        pathCard.Controls.Add(_lblState);
        pathCard.Controls.Add(_txtOut);
        pathCard.Controls.Add(_btnBrowse);
        void PlacePath()
        {
            _btnBrowse.Left = pathCard.Width - 92 - 14;
            _txtOut.Width = Math.Max(120, _btnBrowse.Left - 14 - 20);
            lblPathCap.Width = pathCard.Width - 28;
        }
        pathCard.Resize += (_, _) => PlacePath();
        pathCard.HandleCreated += (_, _) => PlacePath();
        pathRow.Controls.Add(pathCard);

        // ---- перемикач сторінок (замість системного TabControl) ----
        var segRow = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(18, 0, 18, 8), BackColor = Theme.PageBg };
        var seg = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.PageBg, WrapContents = false };
        _segSources = SegBtn("Джерела");
        _segTopics = SegBtn("Теми");
        _segSources.Click += (_, _) => SetPage(0);
        _segTopics.Click += (_, _) => SetPage(1);
        seg.Controls.AddRange(new Control[] { _segSources, _segTopics });
        segRow.Controls.Add(seg);

        // ---- вміст ----
        _tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false, BorderStyle = BorderStyle.None, Font = Theme.Body, ItemHeight = 22 };
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None, Font = Theme.Body };
        _list.Columns.Add("Джерело", 250);
        _list.Columns.Add("Стан", 92);
        _list.Columns.Add("Знайдено", 74, HorizontalAlignment.Right);
        _list.Columns.Add("Нових", 62, HorizontalAlignment.Right);
        _list.Columns.Add("HTML", 54, HorizontalAlignment.Right);
        _list.Columns.Add("Дубль", 56, HorizontalAlignment.Right);
        _list.Columns.Add("Помилок", 66, HorizontalAlignment.Right);
        _list.Columns.Add("Примітка", 380);

        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), WordWrap = false,
        };

        _topics = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None };
        _topics.Columns.Add("Тема", 300);
        _topics.Columns.Add("Стан", 100);
        _topics.Columns.Add("Документів", 90, HorizontalAlignment.Right);
        _topics.Columns.Add("Найсвіжіший", 90, HorizontalAlignment.Right);
        _topics.Columns.Add("Підстава", 520);

        _right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 8, BackColor = Theme.PageBg };
        _right.Panel1.Controls.Add(CardOf("Результати джерел", _list));
        _right.Panel2.Controls.Add(CardOf("Журнал", _txtLog));

        _body = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8, BackColor = Theme.PageBg };
        _body.Panel1.Controls.Add(CardOf("Джерела", _tree));
        _body.Panel2.Controls.Add(_right);

        _pageSources = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PageBg };
        _pageSources.Controls.Add(_body);
        _pageTopics = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PageBg, Visible = false };
        _pageTopics.Controls.Add(CardOf("Теми", _topics));

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 6), BackColor = Theme.PageBg };
        host.Controls.Add(_pageSources);
        host.Controls.Add(_pageTopics);

        // Docking застосовується від останнього доданого — тому контент додаємо ПЕРШИМ,
        // а шапку ОСТАННЬОЮ, інакше заголовок опиняється під панеллю дій.
        Controls.Add(host);

        // ---- підвал: прогрес + стан ----
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(18, 4, 18, 6), BackColor = Theme.PageBg };
        _prog = new ProgressBar { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Continuous };
        var lblHelp = new Label
        {
            Dock = DockStyle.Fill, Font = Theme.Caption, ForeColor = Theme.TextSecondary,
            Text = "Збір іде у фоні — вікно можна згорнути.",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        bottom.Controls.Add(lblHelp);
        bottom.Controls.Add(_prog);

        // порядок докінгу: Fill → Bottom → Top (знизу догори за z-order)
        Controls.Add(bottom);
        Controls.Add(segRow);
        Controls.Add(pathRow);
        Controls.Add(bar);
        Controls.Add(header);

        ResumeLayout(true);

        _btnRun.Click += async (_, _) => await RunAsync();
        _btnStop.Click += (_, _) => { _cts?.Cancel(); Log("Зупиняю…"); };
        _btnBrowse.Click += (_, _) => BrowseFolder();
        _btnOpen.Click += (_, _) => OpenPath(App.Root(_txtOut.Text));
        _btnReport.Click += (_, _) =>
        {
            var p = Path.Combine(App.Root(_txtOut.Text), "report.md");
            if (File.Exists(p)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
            else Log("Звіту ще немає.");
        };
        _btnBrowser.Click += (_, _) => OpenBrowser();
        _btnSettings.Click += (_, _) => OpenSettings();
        _btnLog.Click += (_, _) => SaveLog(openAfter: true);
        _tree.AfterCheck += TreeAfterCheck;
        _txtOut.Leave += (_, _) => { _settings.OutputDir = _txtOut.Text.Trim(); _settings.Save(); };
        SetPage(0);
    }

    // Картка Valera: заголовок розділу + контрол під ним.
    private static Card CardOf(string title, Control body)
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 12) };
        var lbl = Ui.Caption(title, 0, 0, 400);
        lbl.Dock = DockStyle.Top;
        lbl.Height = 20;
        body.Dock = DockStyle.Fill;
        card.Controls.Add(body);
        card.Controls.Add(lbl);
        return card;
    }

    private Button NavBtn(string text) => new()
    {
        Text = text, AutoSize = true, Height = 34, Margin = new Padding(0, 1, 8, 0),
        Font = Theme.ButtonFont, Padding = new Padding(12, 0, 12, 0), FlatStyle = FlatStyle.Flat,
    };

    private Button SegBtn(string text) => new()
    {
        Text = text, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 8, 0),
        Font = Theme.Body, Padding = new Padding(18, 0, 18, 0), FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
    };

    private void SetPage(int index)
    {
        if (_pageSources is null || _pageTopics is null) return;
        _pageSources.Visible = index == 0;
        _pageTopics.Visible = index == 1;
        StyleSeg(_segSources, index == 0);
        StyleSeg(_segTopics, index == 1);
    }

    private static void StyleSeg(Button b, bool active)
    {
        b.BackColor = active ? Theme.CardBg : Theme.PageBg;
        b.ForeColor = active ? Theme.Accent : Theme.TextSecondary;
        b.Font = active ? Theme.Section : Theme.Body;
    }

    // Тема як у Valera: рівні поверхні PageBg → CardBg → ControlBg, акцент лише на головній дії.
    private void ApplyTheme()
    {
        BackColor = Theme.PageBg;
        ForeColor = Theme.TextPrimary;

        Ui.StylePrimary(_btnRun);
        foreach (var b in new[] { _btnStop, _btnBrowse, _btnOpen, _btnReport, _btnBrowser, _btnLog, _btnSettings })
            Ui.StyleSecondary(b);

        foreach (var c in new Control[] { _tree, _list, _topics, _txtLog, _txtOut, _numSince })
        {
            c.BackColor = Theme.ControlBg;
            c.ForeColor = Theme.TextPrimary;
            Ui.ApplyScrollTheme(c);
        }
        _txtLog.BackColor = Theme.CardBg;
        _lblState.ForeColor = Theme.TextSecondary;
        _lblSubtitle.ForeColor = Theme.TextSecondary;
        _chkNew.ForeColor = Theme.TextPrimary;

        Ui.ThemeListView(_list);
        Ui.ThemeListView(_topics);
        foreach (Control c in Controls) PaintSurface(c);
        SetPage(_pageTopics?.Visible == true ? 1 : 0);
        Invalidate(true);
    }

    // Фарбуємо лише поверхні: панелі, картки, розділювачі. Текст не чіпаємо — інакше сірий на сірому.
    private static void PaintSurface(Control c)
    {
        switch (c)
        {
            case Card card:
                card.BackColor = Theme.CardBg;
                card.Invalidate();
                break;
            case SplitContainer sc:
                sc.BackColor = Theme.PageBg;
                sc.SplitterWidth = 8;
                PaintSurface(sc.Panel1); PaintSurface(sc.Panel2);
                break;
            case Panel or FlowLayoutPanel:
                if (c is not SplitterPanel) c.BackColor = Theme.PageBg;
                break;
        }
        foreach (Control child in c.Controls) PaintSurface(child);
    }

    private void OpenSettings()
    {
        var fresh = AppSettings.Load();
        using var dlg = new SettingsForm(fresh);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Параметри збережено всередині діалогу — підхоплюємо те, що впливає на головне вікно.
        var cur = AppSettings.Load();
        _txtOut.Text = cur.OutputDir ?? "";
        _numSince.Value = Math.Clamp(cur.SinceYear, 1990, 2030);
        ApplyTheme();
        Log("Параметри збережено.");
    }

    private void LayoutSplitters()
    {
        try
        {
            _body.Panel1MinSize = 240;
            _body.Panel2MinSize = 420;
            _body.SplitterDistance = 330;
            _right.Panel1MinSize = 160;
            _right.Panel2MinSize = 120;
            _right.SplitterDistance = Math.Max(180, _right.Height / 2);
        }
        catch { /* window too small for the requested panes */ }
    }

    private static Button Btn(string text) => new() { Text = text, AutoSize = true, Height = 28, Margin = new Padding(4, 2, 0, 0) };
    private static Button Big(string text, int width) => new() { Text = text, Width = width, Height = 30, Margin = new Padding(0, 1, 0, 0) };

    private void LoadSources()
    {
        _sources = Registry.Load();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        _nodes.Clear();
        foreach (var group in _sources.GroupBy(s => s.Section))
        {
            var section = new TreeNode(group.Key) { Tag = null };
            foreach (var s in group)
            {
                var n = new TreeNode(s.Name) { Tag = s, Checked = !s.Manual, ToolTipText = s.Url };
                _nodes[s.Id] = n;
                section.Nodes.Add(n);
            }
            section.Checked = group.Any(x => !x.Manual);
            _tree.Nodes.Add(section);
        }
        _tree.ExpandAll();
        _tree.EndUpdate();
        _lblState.Text = $"{_sources.Count} джерел у {_tree.Nodes.Count} розділах";
    }

    private void TreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (e.Action == TreeViewAction.Unknown) return;
        if (e.Node?.Tag != null) return; // leaf: leave as is
        _tree.AfterCheck -= TreeAfterCheck;
        if (e.Node != null) foreach (TreeNode c in e.Node.Nodes) c.Checked = e.Node.Checked;
        _tree.AfterCheck += TreeAfterCheck;
    }

    private List<Source> Picked() =>
        _sources.Where(s => _nodes.TryGetValue(s.Id, out var n) && n.Checked).ToList();

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(_txtOut.Text.Trim()) ? _txtOut.Text.Trim()
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtOut.Text = dlg.SelectedPath;
            _settings.OutputDir = dlg.SelectedPath;
            _settings.Save();
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    private async Task RunAsync()
    {
        if (_running) return;
        var picked = Picked();
        if (picked.Count == 0)
        {
            MessageBox.Show("Познач хоча б одне джерело.", "Dzherelo");
            return;
        }
        var root = _txtOut.Text.Trim();
        if (root.Length == 0)
        {
            MessageBox.Show("Вкажи теку, куди збирати.", "Dzherelo");
            return;
        }
        Directory.CreateDirectory(root);
        _settings.OutputDir = root;
        _settings.Save();

        _results.Clear();
        _list.Items.Clear();
        _running = true;
        _logBuffer.Clear();
        _btnRun.Enabled = false;
        _btnStop.Enabled = true;
        _prog.Value = 0;
        _lblState.Text = "збираю…";
        _taskbar.SetIndeterminate();
        _txtLog.Clear();

        Catalog store;
        try { store = new Catalog(root); }
        catch (InUseException ex)
        {
            MessageBox.Show(ex.Message, "Dzherelo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _running = false;
            _btnRun.Enabled = true;
            _btnStop.Enabled = false;
            return;
        }
        using var _storeOwner = store;
        using var fetch = new Fetcher(Path.Combine(root, "state")) { MinIntervalMs = 1000 };
        var harvester = new Harvester
        {
            SinceYear = (int)_numSince.Value,
            NewOnly = _chkNew.Checked,
            OnLog = Log,
            OnProgress = (done, total) => RunOnUi(() =>
            {
                _prog.Maximum = Math.Max(1, total);
                _prog.Value = Math.Min(done, _prog.Maximum);
                _taskbar.SetValue(done, total);
                _lblState.Text = $"{done} / {total}";
            }),
            OnSourceDone = r => RunOnUi(() => AddResult(r)),
        };
        _cts = new CancellationTokenSource();
        _lastRunId = harvester.RunId;

        try
        {
            var results = await Task.Run(() => harvester.RunAsync(picked, store, fetch, _cts.Token));
            await store.ExportCsvAsync();
            var report = await Harvester.WriteReportAsync(root, harvester.RunId, results, harvester.SinceYear);

            int neu = results.Sum(r => r.New), html = results.Sum(r => r.Html);
            long bytes = results.Sum(r => r.Bytes);
            var attention = results.Where(r => r.Status is not (SourceStatus.Ok or SourceStatus.Empty)).ToList();
            _lblState.Text = $"готово: {neu} нових, {html} html, {bytes / 1024 / 1024} МБ";
            Log("");
            Log($"ГОТОВО. Нові PDF: {neu}; html: {html}; дублів: {results.Sum(r => r.Duplicates)}; "
                + $"помилок: {results.Sum(r => r.Failed)}; {bytes / 1024 / 1024} МБ");
            Log("Звіт: " + report);

            var msg = $"Зібрано: {neu} PDF, {html} HTML-сторінок, {bytes / 1024 / 1024} МБ.\n";
            if (attention.Count > 0)
            {
                msg += "\nПотребують уваги:\n";
                foreach (var r in attention.Take(8)) msg += $"  • {r.Name}: {r.StatusText} — {r.Note}\n";
                if (attention.Count > 8) msg += $"  … і ще {attention.Count - 8}";
            }
            else msg += "\nУсі джерела відпрацювали без зауважень.";
            MessageBox.Show(msg, "Dzherelo", MessageBoxButtons.OK,
                attention.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log("Зупинено. Уже зібране збережено в журналі — повторний запуск це пропустить.");
            _lblState.Text = "зупинено";
            SaveLog();
        }
        catch (Exception ex)
        {
            Log("ПОМИЛКА: " + ex);
            MessageBox.Show("Помилка: " + ex.Message, "Dzherelo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (_logBuffer.Length > 0 && !File.Exists(LogPath())) SaveLog(silent: true);
            _running = false;
            _btnRun.Enabled = true;
            _btnStop.Enabled = false;
            _taskbar.SetNoProgress();
            _lblState.Text = CountCollection();
            try { RefreshTopics(store); } catch { /* панель не заважає завершенню прогону */ }
        }
    }

    private void AddResult(SourceResult r)
    {
        _results[r.Id] = r;
        var item = new ListViewItem(r.Name);
        item.SubItems.Add(r.StatusText);
        item.SubItems.Add(r.Expected > 0 ? $"{r.Found}/{r.Expected}" : r.Found.ToString());
        item.SubItems.Add(r.New.ToString());
        item.SubItems.Add(r.Html.ToString());
        item.SubItems.Add(r.Duplicates.ToString());
        item.SubItems.Add(r.Failed.ToString());
        item.SubItems.Add(r.Note);
        item.ForeColor = r.Status switch
        {
            SourceStatus.Ok => Color.FromArgb(20, 110, 40),
            SourceStatus.Empty => Color.DimGray,
            SourceStatus.Partial => Color.FromArgb(170, 100, 0),
            _ => Color.FromArgb(170, 30, 30),
        };
        _list.Items.Add(item);

        if (_nodes.TryGetValue(r.Id, out var node))
            node.Text = r.Name + "  ·  " + r.StatusText + (r.New > 0 ? " +" + r.New : "");
        if (_list.Items.Count > 0) _list.EnsureVisible(_list.Items.Count - 1);
    }

    private string CountCollection()
    {
        try
        {
            var root = App.Root(_txtOut.Text);
            if (!Directory.Exists(Path.Combine(root, "library"))) return "колекція порожня";
            var files = Directory.EnumerateFiles(Path.Combine(root, "library"), "*", SearchOption.AllDirectories).Count();
            return $"{files} файлів у колекції";
        }
        catch { return "—"; }
    }

    private static string StateText(TopicState st) => st switch
    {
        TopicState.Ok => "ок",
        TopicState.Gap => "прогалини",
        _ => "застаріле",
    };

    // Панель рахує покриття тем поверх живого каталогу; окремий індекс не тримаємо.
    private void RefreshTopicsFromDisk()
    {
        try
        {
            var root = App.Root(_txtOut.Text);
            if (!Directory.Exists(Path.Combine(root, "library"))) return;
            using var cat = new Catalog(root);
            RefreshTopics(cat);
        }
        catch (InUseException) { /* збір триває — оновимо по завершенні */ }
        catch { }
    }

    private void RefreshTopics(Catalog cat)
    {
        if (_topics == null) return;
        var docs = cat.All();
        _topics.BeginUpdate();
        _topics.Items.Clear();
        foreach (var t in Topics.Load(cat.State))
        {
            var cov = Topics.Compute(t, docs);
            var item = new ListViewItem(t.Name);
            item.SubItems.Add(StateText(cov.State));
            item.SubItems.Add(cov.Count.ToString());
            item.SubItems.Add(cov.NewestYear?.ToString() ?? "—");
            item.SubItems.Add(cov.Basis);
            item.ForeColor = cov.State switch
            {
                TopicState.Ok => Color.FromArgb(20, 110, 40),
                TopicState.Stale => Color.FromArgb(170, 100, 0),
                _ => Color.FromArgb(170, 30, 30),
            };
            _topics.Items.Add(item);
        }
        _topics.EndUpdate();
    }

    private string LogPath()
    {
        var stamp = _lastRunId.Length > 0 ? _lastRunId : DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(App.Root(_txtOut.Text), "logs", "run-" + stamp + ".log");
    }

    // The file is the working material for the next iteration: full URLs, run settings
    // and the per-source table, not just what fits in the window.
    private void SaveLog(bool silent = false, bool openAfter = false)
    {
        try
        {
            if (_logBuffer.Length == 0)
            {
                Log("Журнал порожній, зберігати нічого.");
                return;
            }
            var path = LogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("== Dzherelo " + (GetType().Assembly.GetName().Version?.ToString(3) ?? "?") + " ==");
            sb.AppendLine("запис: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("машина: " + Environment.MachineName + " | .NET " + Environment.Version);
            sb.AppendLine("тека: " + App.Root(_txtOut.Text));
            sb.AppendLine("прогін: " + (_lastRunId.Length > 0 ? _lastRunId : "—"));
            sb.AppendLine("з року: " + _numSince.Value + " | лише нове: " + _chkNew.Checked
                          + " | джерел позначено: " + Picked().Count);
            sb.AppendLine();
            sb.AppendLine("--- журнал ---");
            sb.Append(_logBuffer);

            if (_results.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- джерела ---");
                sb.AppendLine("стан	знайдено	очікувалось	нових	html	дублів	пропущено	помилок	час	примітка	джерело");
                foreach (var r in _results.Values.OrderBy(r => r.Section).ThenBy(r => r.Name))
                    sb.AppendLine($"{r.StatusText}	{r.Found}	{(r.Expected > 0 ? r.Expected.ToString() : "—")}	{r.New}	"
                                  + $"{r.Html}	{r.Duplicates}	{r.Skipped}	{r.Failed}	{r.Elapsed:mm\\:ss}	{r.Note}	{r.Name}");
            }

            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            Log("Лог збережено: " + path);
            if (openAfter)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show("Не вдалося зберегти лог: " + ex.Message, "Dzherelo");
        }
    }

    private void OpenBrowser()
    {
        var src = _tree.SelectedNode?.Tag as Source;
        var url = src?.Url ?? "https://nllp.jallc.nato.int/";
        var f = new BrowserForm(url) { Owner = this };
        f.Show(this);
        Log("Браузер: " + url + " (сесія входу зберігається)");
    }

    private void Log(string s)
    {
        if (_txtLog.IsDisposed) return;
        if (_txtLog.InvokeRequired) { try { _txtLog.BeginInvoke(() => Log(s)); } catch { } return; }
        var line = $"{DateTime.Now:HH:mm:ss}  {s}";
        _logBuffer.AppendLine(line);
        _txtLog.AppendText(line + Environment.NewLine);
    }

    private void RunOnUi(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
        else a();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_running) return;
        var answer = MessageBox.Show("Збір ще йде. Зупинити й закрити?", "Dzherelo",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.No) { e.Cancel = true; return; }
        _cts?.Cancel();
    }

    // Drag&drop: файли, кинуті у вікно, додаються в колекцію.
    private void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        ImportFiles(files);
    }

    private void ImportFiles(string[] files)
    {
        var root = App.Root(_txtOut.Text);
        try
        {
            Directory.CreateDirectory(root);
            Catalog store;
            try { store = new Catalog(root); }
            catch (InUseException ex)
            {
                MessageBox.Show(ex.Message, "Dzherelo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (store)
            {
                int added = 0, dup = 0, bad = 0;
                foreach (var f in files)
                {
                    var res = Harvester.IngestLocal(store, f);
                    if (res.Doc != null) added++;
                    else if (res.Duplicate) dup++;
                    else { bad++; Log("  ✖ " + Path.GetFileName(f) + " — " + res.Error); }
                }
                _lblState.Text = CountCollection();
                Log($"Перетягнуто {files.Length} файлів: додано {added}, дублів {dup}, відхилено {bad}.");
                try { RefreshTopics(store); } catch { }
                MessageBox.Show($"Додано {added} · дублів {dup} · відхилено {bad}.", "Dzherelo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не вдалося додати файли: " + ex.Message, "Dzherelo",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// Only for sources that need a human (NLLP login, captchas); the session persists in UserDataFolder.
public sealed class BrowserForm : Form
{
    public BrowserForm(string url)
    {
        Text = "Браузер — " + url;
        ClientSize = new Size(1100, 780);
        StartPosition = FormStartPosition.CenterParent;
        var view = new WebView2
        {
            Dock = DockStyle.Fill,
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Dzherelo", "WebView2"),
            },
        };
        Controls.Add(view);
        Load += async (_, _) =>
        {
            try
            {
                await view.EnsureCoreWebView2Async();
                view.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2: " + ex.Message);
            }
        };
    }
}
