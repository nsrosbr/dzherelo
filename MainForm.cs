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
    private TabControl _tabs = null!;
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
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 2), WrapContents = false };
        _btnRun = Big("ЗІБРАТИ", 150);
        _btnRun.Font = new Font(_btnRun.Font.FontFamily, 10f, FontStyle.Bold);
        _btnStop = Btn("Стоп"); _btnStop.Enabled = false;
        _chkNew = new CheckBox { Text = "лише нове", Checked = true, AutoSize = true, Margin = new Padding(16, 9, 0, 0) };
        _numSince = new NumericUpDown { Minimum = 1990, Maximum = 2030, Value = 2022, Width = 64, Margin = new Padding(16, 6, 0, 0) };
        _btnBrowser = Btn("Браузер (вхід)");
        _btnReport = Btn("Звіт");
        _btnLog = Btn("Зберегти лог");
        _btnOpen = Btn("Тека");
        _btnSettings = Btn("Параметри…");
        row1.Controls.AddRange(new Control[]
        {
            _btnRun, _btnStop, _chkNew,
            new Label { Text = "з року:", AutoSize = true, Margin = new Padding(16, 9, 0, 0) }, _numSince,
            _btnBrowser, _btnReport, _btnLog, _btnOpen, _btnSettings,
        });

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 2, 8, 6), WrapContents = false };
        _txtOut = new TextBox { Width = 620, Text = _settings.OutputDir ?? "" };
        _btnBrowse = Btn("…");
        _lblState = new Label { AutoSize = true, Margin = new Padding(16, 9, 0, 0), Text = "—" };
        row2.Controls.AddRange(new Control[]
        {
            new Label { Text = "Куди збирати:", AutoSize = true, Margin = new Padding(0, 9, 0, 0) }, _txtOut, _btnBrowse, _lblState,
        });

        _tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false, Font = new Font("Segoe UI", 9f) };
        _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
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
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(250, 250, 250), WordWrap = false,
        };

        _right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        _right.Panel1.Controls.Add(_list);
        _right.Panel2.Controls.Add(_txtLog);

        _body = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        _body.Panel1.Controls.Add(_tree);
        _body.Panel2.Controls.Add(_right);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 4, 8, 4) };
        _prog = new ProgressBar { Dock = DockStyle.Bottom, Height = 16 };
        var lblHelp = new Label
        {
            Dock = DockStyle.Top, Height = 18, ForeColor = Color.DimGray,
            Text = "Збір іде у фоні — вікно можна згорнути. Прогрес і стан кожного джерела — у журналі.",
        };
        bottom.Controls.Add(_prog);
        bottom.Controls.Add(lblHelp);

        _topics = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        _topics.Columns.Add("Тема", 300);
        _topics.Columns.Add("Стан", 100);
        _topics.Columns.Add("Документів", 90, HorizontalAlignment.Right);
        _topics.Columns.Add("Найсвіжіший", 90, HorizontalAlignment.Right);
        _topics.Columns.Add("Підстава", 520);

        var tabSources = new TabPage("Джерела");
        tabSources.Controls.Add(_body);
        var tabTopics = new TabPage("Теми") { Padding = new Padding(6) };
        tabTopics.Controls.Add(_topics);
        _tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed };
        _tabs.ItemSize = new Size(120, 30);
        _tabs.DrawItem += DrawTab;
        _tabs.TabPages.Add(tabSources);
        _tabs.TabPages.Add(tabTopics);

        Controls.Add(_tabs);
        Controls.Add(bottom);
        Controls.Add(row2);
        Controls.Add(row1);

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
    }

    // Тема як у Valera: рівні поверхні PageBg → CardBg → ControlBg, акцент лише на головній дії.
    private void ApplyTheme()
    {
        BackColor = Theme.PageBg;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;

        Ui.StylePrimary(_btnRun);
        foreach (var b in new[] { _btnStop, _btnBrowse, _btnOpen, _btnReport, _btnBrowser, _btnLog, _btnSettings })
            Ui.StyleSecondary(b);

        foreach (var c in new Control[] { _tree, _list, _topics, _txtLog, _txtOut, _numSince })
        {
            c.BackColor = Theme.ControlBg;
            c.ForeColor = Theme.TextPrimary;
            c.Font = c == _txtLog ? new Font("Consolas", 9f) : Theme.Body;
            Ui.ApplyScrollTheme(c);
        }
        _tree.BorderStyle = BorderStyle.None;
        _list.BorderStyle = BorderStyle.None;
        _topics.BorderStyle = BorderStyle.None;
        _list.GridLines = false;
        _topics.GridLines = false;
        _txtLog.WordWrap = false;
        _lblState.ForeColor = Theme.TextSecondary;
        _chkNew.ForeColor = Theme.TextPrimary;

        foreach (Control c in Controls) PaintSurface(c);

        _tabs?.Invalidate();
        Invalidate(true);
    }

    // Фарбуємо лише «поверхні» (панелі/вкладки), а не кожен Label — інакше текст стає сірим на сірому.
    private static void PaintSurface(Control c)
    {
        switch (c)
        {
            case TabControl tabs:
                tabs.BackColor = Theme.PageBg;
                foreach (TabPage tp in tabs.TabPages) { tp.BackColor = Theme.PageBg; tp.ForeColor = Theme.TextPrimary; }
                break;
            case TabPage tp:
                tp.BackColor = Theme.PageBg; tp.ForeColor = Theme.TextPrimary;
                break;
            case SplitContainer sc:
                sc.BackColor = Theme.Divider;
                sc.SplitterWidth = 1;
                PaintSurface(sc.Panel1); PaintSurface(sc.Panel2);
                break;
            case Panel or FlowLayoutPanel:
                if (c is SplitterPanel == false) c.BackColor = Theme.PageBg;
                break;
        }
        foreach (Control child in c.Controls) PaintSurface(child);
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var g = e.Graphics;
        bool sel = e.Index == _tabs.SelectedIndex;
        var r = e.Bounds;
        using (var b = new SolidBrush(Theme.PageBg)) g.FillRectangle(b, r);
        if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X + 6, r.Bottom - 3, r.Width - 12, 3);
        TextRenderer.DrawText(g, _tabs.TabPages[e.Index].Text, sel ? Theme.Section : Theme.Body, r,
            sel ? Theme.TextPrimary : Theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
