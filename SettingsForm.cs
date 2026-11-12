using System.Diagnostics;

namespace Dzherelo;

// Екран параметрів у логіці Valera (SettingsForm): шапка, картки з розділювачами,
// перемикачі, підвал «Зберегти / Скасувати».
internal sealed class SettingsForm : ThemedForm
{
    private readonly AppSettings _cfg;

    private ToggleSwitch _cUpdates = null!;
    private TextBox _txtOut = null!, _txtKey = null!;
    private TextBox _txtSince = null!;
    private ComboBox _theme = null!;
    private ComboBox _cbModel = null!;
    private ComboBox _cbProbe = null!, _cbCatalog = null!;
    private Label _lblModels = null!;
    private Label _lblVersion = null!;

    private const int FormW = 620;
    private const int CardX = 18;
    private const int CardW = 580;
    private const int Pad = 16;

    public SettingsForm(AppSettings cfg)
    {
        _cfg = cfg;
        BuildUi();
        LoadValues();
    }

    private void BuildUi()
    {
        Text = "Параметри — " + Ui.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.PageBg;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ClientSize = new Size(FormW, 640);
        Icon = Program.AppIcon();

        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.PageBg };
        header.Controls.Add(Ui.Title("Параметри", CardX, 16, 400));
        header.Controls.Add(Ui.Caption(Ui.Name + " " + Program.Version + " · без телеметрії, усе локально", CardX, 50, 520));
        Controls.Add(header);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Theme.PageBg };
        footer.Paint += (_, e) => { using var pen = new Pen(Theme.CardBorder); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var ok = new Button { Text = "Зберегти", Width = 116, DialogResult = DialogResult.OK };
        Ui.StylePrimary(ok);
        ok.Left = FormW - CardX - ok.Width; ok.Top = 13;
        ok.Click += (_, _) => SaveValues();
        var cancel = new Button { Text = "Скасувати", Width = 110, DialogResult = DialogResult.Cancel };
        Ui.StyleSecondary(cancel);
        cancel.Left = ok.Left - cancel.Width - 10; cancel.Top = 13;
        footer.Controls.Add(ok); footer.Controls.Add(cancel);
        Controls.Add(footer);
        AcceptButton = ok; CancelButton = cancel;

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PageBg, AutoScroll = true, Padding = new Padding(0, 4, 0, 8) };
        Ui.ApplyScrollTheme(content);
        Controls.Add(content);
        content.BringToFront();

        int y = 8;

        // ---- Інтерфейс ----
        content.Controls.Add(Ui.Section("Інтерфейс", CardX, y, CardW)); y += 26;
        var card0 = NewCard(content, y);
        card0.Controls.Add(Ui.Body("Тема:", Pad, 16, 90));
        _theme = new ThemedCombo { Left = Pad + 96, Top = 13, Width = 220, Font = Theme.Body, BackColor = Theme.ControlBg, ForeColor = Theme.TextPrimary };
        _theme.Items.Add("Системна");   // 0 -> auto
        _theme.Items.Add("Світла");     // 1 -> light
        _theme.Items.Add("Темна");      // 2 -> dark
        card0.Controls.Add(_theme);
        card0.Controls.Add(Ui.Caption("Тема застосовується одразу, без перезапуску.", Pad, 50, CardW - 2 * Pad));
        FinishCard(card0, 74);
        y += card0.Height + 18;

        // ---- Бібліотека ----
        content.Controls.Add(Ui.Section("Бібліотека", CardX, y, CardW)); y += 26;
        var card1 = NewCard(content, y);
        card1.Controls.Add(Ui.Caption("Куди збирати документи:", Pad, 10, CardW - 2 * Pad));
        _txtOut = new TextBox { Left = Pad, Top = 30, Width = CardW - 2 * Pad - 110 };
        Ui.StyleInput(_txtOut);
        card1.Controls.Add(_txtOut);
        var bBrowse = new Button { Text = "Огляд…", Left = CardW - Pad - 100, Top = 26, Width = 100 };
        Ui.StyleSecondary(bBrowse);
        bBrowse.Click += (_, _) => BrowseFolder();
        card1.Controls.Add(bBrowse);

        card1.Controls.Add(Ui.Body("Збирати з року:", Pad, 76, 150));
        _txtSince = new TextBox { Left = Pad + 150, Top = 73, Width = 90, Height = 24, MaxLength = 4 };
        Ui.StyleInput(_txtSince);
        _txtSince.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        card1.Controls.Add(_txtSince);
        card1.Controls.Add(Ui.Caption("Документи, старші за цей рік, не беруться (крім явно обраних).", Pad, 104, CardW - 2 * Pad));
        FinishCard(card1, 130);
        y += card1.Height + 18;

        // ---- Оновлення ----
        content.Controls.Add(Ui.Section("Оновлення", CardX, y, CardW)); y += 26;
        var card2 = NewCard(content, y);
        int cy = 8;
        card2.Controls.Add(Ui.Body("Перевіряти оновлення при запуску", Pad, cy + 9, CardW - 100));
        card2.Controls.Add(Ui.Caption("Єдиний вихід у мережу поза збором: коротка перевірка версії.", Pad, cy + 30, CardW - 100));
        _cUpdates = new ToggleSwitch { BackColor = Theme.CardBg };
        _cUpdates.Left = CardW - Pad - _cUpdates.Width;
        _cUpdates.Top = cy + 16;
        card2.Controls.Add(_cUpdates);
        cy += 54;
        AddDivider(card2, cy); cy += 1;
        card2.Controls.Add(Ui.Body("Встановлено:", Pad, cy + 9, 110));
        _lblVersion = Ui.Caption("", Pad + 110, cy + 11, CardW - 2 * Pad - 110);
        card2.Controls.Add(_lblVersion);
        cy += 40;
        AddDivider(card2, cy); cy += 1;
        var bCheck = new Button { Text = "Перевірити зараз", Left = Pad, Top = cy + 6, Width = 170 };
        Ui.StyleSecondary(bCheck);
        bCheck.Click += (_, _) => CheckUpdatesNow();
        card2.Controls.Add(bCheck);
        cy += 52;
        FinishCard(card2, cy);
        y += card2.Height + 18;

        // ---- Ключі ----
        content.Controls.Add(Ui.Section("Ключі й доступи", CardX, y, CardW)); y += 26;
        var card3 = NewCard(content, y);
        card3.Controls.Add(Ui.Caption("Ключ OpenRouter — у сховищі Windows (DPAPI), не в settings.json і не в релізі.", Pad, 10, CardW - 2 * Pad));
        card3.Controls.Add(Ui.Body("Ключ:", Pad, 40, 60));
        _txtKey = new TextBox { Left = Pad + 60, Top = 37, Width = CardW - 2 * Pad - 60, UseSystemPasswordChar = true, PlaceholderText = "sk-or-…" };
        Ui.StyleInput(_txtKey);
        card3.Controls.Add(_txtKey);
        card3.Controls.Add(Ui.Caption("Порожнє поле = ключ видалено. Модельні судження без ключа не працюють, решта — працює.", Pad, 64, CardW - 2 * Pad));

        AddDivider(card3, 88);
        // Три ролі: одна модель не робить усе добре — розмітка, копання в джерелі й каталогізація різні.
        card3.Controls.Add(Ui.Body("Судження:", Pad, 98, 110));
        _cbModel = new ThemedCombo { Left = Pad + 112, Top = 95, Width = CardW - 2 * Pad - 112 - 150, Font = Theme.Body };
        card3.Controls.Add(_cbModel);

        card3.Controls.Add(Ui.Body("Копання:", Pad, 136, 110));
        _cbProbe = new ThemedCombo { Left = Pad + 112, Top = 133, Width = CardW - 2 * Pad - 112 - 150, Font = Theme.Body };
        card3.Controls.Add(_cbProbe);

        card3.Controls.Add(Ui.Body("Каталог:", Pad, 174, 110));
        _cbCatalog = new ThemedCombo { Left = Pad + 112, Top = 171, Width = CardW - 2 * Pad - 112 - 150, Font = Theme.Body };
        card3.Controls.Add(_cbCatalog);

        var bModels = new Button { Text = "Оновити список", Left = CardW - Pad - 140, Top = 92, Width = 140 };
        Ui.StyleSecondary(bModels);
        bModels.Click += async (_, _) => await LoadModelsAsync();
        card3.Controls.Add(bModels);
        var bCheckKey = new Button { Text = "Перевірити ключ", Left = Pad, Top = 208, Width = 160 };
        Ui.StyleSecondary(bCheckKey);
        bCheckKey.Click += async (_, _) => await CheckKeyAsync();
        card3.Controls.Add(bCheckKey);
        _lblModels = Ui.Caption("", Pad + 170, 214, CardW - 2 * Pad - 170);
        card3.Controls.Add(_lblModels);
        FinishCard(card3, 244);
        y += card3.Height + 18;

        // ---- Про програму ----
        content.Controls.Add(Ui.Section("Про програму", CardX, y, CardW)); y += 26;
        var card4 = NewCard(content, y);
        card4.Controls.Add(Ui.Body("Дані:", Pad, 16, 60));
        var lblDir = Ui.Caption(DataDir, Pad + 60, 18, CardW - 2 * Pad - 60);
        card4.Controls.Add(lblDir);
        var bOpenData = new Button { Text = "Відкрити теку даних", Left = Pad, Top = 46, Width = 200 };
        Ui.StyleSecondary(bOpenData);
        bOpenData.Click += (_, _) => { try { Process.Start("explorer.exe", DataDir); } catch { } };
        card4.Controls.Add(bOpenData);
        var bReset = new Button { Text = "Скинути адаптери", Left = Pad + 210, Top = 46, Width = 180 };
        Ui.StyleSecondary(bReset);
        bReset.Click += (_, _) => ResetAdapters();
        card4.Controls.Add(bReset);
        var bAbout = new Button { Text = "Про програму…", Left = Pad + 400, Top = 46, Width = 170 };
        Ui.StyleSecondary(bAbout);
        bAbout.Click += (_, _) => { using var about = new AboutForm(); about.ShowDialog(this); };
        card4.Controls.Add(bAbout);
        FinishCard(card4, 92);
        y += card4.Height + 8;
    }

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo");

    private Card NewCard(Panel parent, int y)
    {
        var c = new Card { Left = CardX, Top = y, Width = CardW };
        parent.Controls.Add(c);
        return c;
    }

    private static void FinishCard(Card c, int contentBottom) => c.Height = contentBottom + 8;

    private static void AddDivider(Card c, int y) =>
        c.Controls.Add(new Panel { Left = Pad, Top = y, Width = CardW - 2 * Pad, Height = 1, BackColor = Theme.Divider });

    private async Task LoadModelsAsync()
    {
        if (!Judge.Ready) { Ui.Msg("Спершу впиши ключ OpenRouter і збережи.", Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _lblModels.Text = "тягну список…";
        try
        {
            var list = await Judge.ModelsAsync(default);
            string keep = _cbModel.Text, keepP = _cbProbe.Text, keepC = _cbCatalog.Text;
            foreach (var cb in new[] { _cbModel, _cbProbe, _cbCatalog })
            {
                cb.Items.Clear();
                foreach (var m in list) cb.Items.Add(m);
            }
            _lblModels.Text = "моделей: " + list.Count;
            if (!string.IsNullOrWhiteSpace(keep) && list.Contains(keep)) _cbModel.SelectedItem = keep;
            if (!string.IsNullOrWhiteSpace(keepP) && list.Contains(keepP)) _cbProbe.SelectedItem = keepP;
            if (!string.IsNullOrWhiteSpace(keepC) && list.Contains(keepC)) _cbCatalog.SelectedItem = keepC;
            else if (!string.IsNullOrWhiteSpace(_cfg.Model) && list.Contains(_cfg.Model)) _cbModel.SelectedItem = _cfg.Model;
        }
        catch (Exception ex)
        {
            _lblModels.Text = "";
            Ui.Msg("Не вдалося отримати список моделей: " + ex.Message, Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckKeyAsync()
    {
        Secrets.Set("openrouter.key", _txtKey.Text.Trim());
        _cfg.ModelJudge = (_cbModel.SelectedItem?.ToString() ?? _cbModel.Text).Trim();
        _cfg.ModelProbe = (_cbProbe.SelectedItem?.ToString() ?? _cbProbe.Text).Trim();
        _cfg.ModelCatalog = (_cbCatalog.SelectedItem?.ToString() ?? _cbCatalog.Text).Trim();
        _cfg.Model = _cfg.ModelJudge;
        if (!Judge.Ready) { Ui.Msg("Ключ порожній.", Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            var models = await Judge.ModelsAsync(default);
            Ui.Msg("Ключ працює. Доступних моделей: " + models.Count, Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            Ui.Msg("Ключ не приймається: " + ex.Message, Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Куди збирати документи", UseDescriptionForTitle = true };
        if (Directory.Exists(_txtOut.Text)) dlg.SelectedPath = _txtOut.Text;
        if (dlg.ShowDialog() == DialogResult.OK) _txtOut.Text = dlg.SelectedPath;
    }

    private void LoadValues()
    {
        _theme.SelectedIndex = _cfg.UiTheme == "light" ? 1 : _cfg.UiTheme == "dark" ? 2 : 0;
        _txtOut.Text = _cfg.OutputDir ?? "";
        _txtSince.Text = Math.Clamp(_cfg.SinceYear, 1990, 2035).ToString();
        _cUpdates.Checked = _cfg.CheckUpdatesOnStart;
        _txtKey.Text = Secrets.Get("openrouter.key") ?? "";
        if (!string.IsNullOrWhiteSpace(_cfg.Model)) { _cbModel.Items.Add(_cfg.Model); _cbModel.SelectedItem = _cfg.Model; }
        _lblVersion.Text = Program.Version + " · канал: " + (Updater.IsConfigured ? "підключено" : "не налаштовано");
    }

    private void SaveValues()
    {
        var before = _cfg.UiTheme;
        _cfg.UiTheme = _theme.SelectedIndex == 1 ? "light" : _theme.SelectedIndex == 2 ? "dark" : "auto";
        _cfg.OutputDir = _txtOut.Text.Trim();
        _cfg.SinceYear = int.TryParse(_txtSince.Text.Trim(), out var sinceYear) && sinceYear >= 1990 ? sinceYear : 2022;
        _cfg.CheckUpdatesOnStart = _cUpdates.Checked;
        Secrets.Set("openrouter.key", _txtKey.Text.Trim());
        _cfg.Save();

        if (!string.Equals(before, _cfg.UiTheme, StringComparison.OrdinalIgnoreCase))
        {
            Theme.Init(_cfg.UiTheme);
            Theme.Refresh();
        }
    }

    private async void CheckUpdatesNow()
    {
        try
        {
            var m = await Updater.FetchManifestAsync();
            Ui.Msg("Встановлено: " + Program.Version + "\nУ каналі: " + m.Version +
                   (Updater.IsNewer(m) ? "\n\nЄ новіша версія — натисніть «Оновити»." : "\n\nУ вас найновіша версія."),
                   Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Ui.Msg("Канал недоступний: " + ex.Message, Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ResetAdapters()
    {
        if (Ui.Msg("Прибрати всі перевизначення адаптерів і повернути стандартні?", Ui.Name,
                   MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var path = Path.Combine(App.Root(null), "library", ".state", "adapters.json");
            if (File.Exists(path)) File.Delete(path);
            Ui.Msg("Готово. Адаптери повернуто до стандартних.", Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Ui.Msg("Не вдалося: " + ex.Message, Ui.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
