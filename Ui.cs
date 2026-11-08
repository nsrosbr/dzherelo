using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Dzherelo
{
    // Windows 11 / Fluent-style theme (light + dark) with runtime switching.
    internal static class Theme
    {
        public enum ThemeMode { System, Light, Dark }
        public static ThemeMode Mode { get; private set; }
        private static bool _dark;
        public static bool IsDark { get { return _dark; } }

        // Raised (on the caller's thread) when the resolved palette actually changes at runtime.
        public static event Action? Changed;

    /// <summary>Перемалювати після програмної зміни палітри (подія недоступна ззовні).</summary>

        // Palette — MUTABLE (swapped by ApplyLight/ApplyDark). Custom-painted controls (Card,
        // ToggleSwitch) read these at paint time, so an Invalidate() re-themes them for free.
        public static Color PageBg, CardBg, CardBorder, TextPrimary, TextSecondary,
            Accent, AccentHover, AccentPressed, AccentText,
            ControlBg, ControlHover, ControlBorder, ToggleOff, Divider, GridSelBg,
            DonateBg, DonateText, DonateHover;

        public static readonly Font Title = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Section = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Body = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Caption = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font ButtonFont = new Font("Segoe UI", 9.75f, FontStyle.Regular, GraphicsUnit.Point);

        static Theme() { ApplyLight(); } // sensible default until Init() runs

        // mode: "light" | "dark" | anything else ("auto") = follow the Windows setting.
        public static void Init(string mode)
        {
            Mode = string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase) ? ThemeMode.Light
                 : string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase) ? ThemeMode.Dark
                 : ThemeMode.System;
            Resolve(false);
        }

        // Called when the OS theme changes; only re-themes if we're following the system.
        public static void SystemChanged() { if (Mode == ThemeMode.System) Resolve(true); }

    public static void Refresh() => Changed?.Invoke();

        private static void Resolve(bool notify)
        {
            bool dark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemUsesDark());
            if (notify && dark == _dark) return; // no real change
            if (dark) ApplyDark(); else ApplyLight();
            if (notify && Changed != null) Changed();
        }

        // HKCU AppsUseLightTheme == 0  ->  dark.
        public static bool SystemUsesDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null) { object v = k.GetValue("AppsUseLightTheme"); if (v is int) return (int)v == 0; }
                }
            }
            catch { }
            return false;
        }

        private static void ApplyLight()
        {
            _dark = false;
            PageBg = Color.FromArgb(0xF3, 0xF3, 0xF3);
            CardBg = Color.FromArgb(0xFB, 0xFB, 0xFB);
            CardBorder = Color.FromArgb(0xE5, 0xE5, 0xE5);
            TextPrimary = Color.FromArgb(0x1A, 0x1A, 0x1A);
            TextSecondary = Color.FromArgb(0x60, 0x60, 0x60);
            Accent = Color.FromArgb(0x00, 0x67, 0xC0);
            AccentHover = Color.FromArgb(0x00, 0x59, 0xB3);
            AccentPressed = Color.FromArgb(0x00, 0x4C, 0x99);
            AccentText = Color.White;
            ControlBg = Color.White;
            ControlHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
            ControlBorder = Color.FromArgb(0xD0, 0xD0, 0xD0);
            ToggleOff = Color.FromArgb(0x8A, 0x8A, 0x8A);
            Divider = Color.FromArgb(0xEB, 0xEB, 0xEB);
            GridSelBg = Color.FromArgb(0xE8, 0xF0, 0xFB);
            // Support buttons are GREEN (inviting) — a warm/red tint reads as "stop" and deters clicks.
            DonateBg = Color.FromArgb(0xE9, 0xF5, 0xEC);
            DonateText = Color.FromArgb(0x2E, 0x7D, 0x32);
            DonateHover = Color.FromArgb(0xD8, 0xEE, 0xDD);
        }

        private static void ApplyDark()
        {
            _dark = true;
            PageBg = Color.FromArgb(0x20, 0x20, 0x20);
            CardBg = Color.FromArgb(0x2B, 0x2B, 0x2B);
            CardBorder = Color.FromArgb(0x3A, 0x3A, 0x3A);
            TextPrimary = Color.FromArgb(0xF0, 0xF0, 0xF0);
            TextSecondary = Color.FromArgb(0xA8, 0xA8, 0xA8);
            Accent = Color.FromArgb(0x0A, 0x84, 0xFF);
            AccentHover = Color.FromArgb(0x2A, 0x96, 0xFF);
            AccentPressed = Color.FromArgb(0x00, 0x60, 0xC0);
            AccentText = Color.White;
            ControlBg = Color.FromArgb(0x3E, 0x3E, 0x3E);      // input fields sit clearly above the cards
            ControlHover = Color.FromArgb(0x4A, 0x4A, 0x4A);
            ControlBorder = Color.FromArgb(0x60, 0x60, 0x60);
            ToggleOff = Color.FromArgb(0x6A, 0x6A, 0x6A);
            Divider = Color.FromArgb(0x38, 0x38, 0x38);
            GridSelBg = Color.FromArgb(0x2F, 0x40, 0x55);
            // Support buttons GREEN in dark too (bright, inviting) rather than a warm/red tint.
            DonateBg = Color.FromArgb(0x22, 0x33, 0x27);
            DonateText = Color.FromArgb(0x6B, 0xD1, 0x8A);
            DonateHover = Color.FromArgb(0x2E, 0x44, 0x33);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); p.CloseFigure(); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // A rounded card panel with a subtle border, like Windows 11 Settings groups.
    internal class Card : Panel
    {
        public Card()
        {
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // parent page colour behind the rounded corners
            if (Parent != null) e.Graphics.Clear(Parent.BackColor);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(r, 8))
            using (var b = new SolidBrush(Theme.CardBg))
            using (var pen = new Pen(Theme.CardBorder))
            {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // Windows 11-style pill toggle switch.
    internal class ToggleSwitch : Control
    {
        private bool _on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(40, 20);
            Cursor = System.Windows.Forms.Cursors.Hand;
            TabStop = true;
        }

        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get { return _on; }
            set { if (_on != value) { _on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; Focus(); base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null) g.Clear(Parent.BackColor);

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(r, r.Height / 2))
            {
                if (_on)
                {
                    using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                }
                else
                {
                    using (var pen = new Pen(Theme.ToggleOff, 1.4f)) g.DrawPath(pen, path);
                }
            }

            int pad = Math.Max(2, r.Height / 6);
            int kd = r.Height - 2 * pad;
            int kx = _on ? r.Right - kd - pad : r.Left + pad;
            int ky = r.Top + pad;
            using (var b = new SolidBrush(_on ? Color.White : Theme.ToggleOff))
                g.FillEllipse(b, kx, ky, kd, kd);

            if (Focused)
            {
                using (var pen = new Pen(Theme.Accent, 1f) { DashStyle = DashStyle.Dot })
                using (var path = Theme.Rounded(new Rectangle(r.X, r.Y, r.Width, r.Height), r.Height / 2))
                    g.DrawPath(pen, path);
            }
        }
    }

    // Theme-aware colour table + renderer for the tray context menu (so it goes dark too).
    internal class ThemeMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.CardBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.CardBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.CardBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.CardBg; } }
        public override Color MenuBorder { get { return Theme.CardBorder; } }
        public override Color MenuItemBorder { get { return Theme.ControlHover; } }
        public override Color MenuItemSelected { get { return Theme.ControlHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.ControlHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.ControlHover; } }
        public override Color SeparatorDark { get { return Theme.Divider; } }
        public override Color SeparatorLight { get { return Theme.Divider; } }
    }

    internal class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemeMenuRenderer() : base(new ThemeMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Respect an item's explicit ForeColor (e.g. the warm donate items); otherwise theme it.
            if (e.Item.ForeColor != System.Drawing.SystemColors.ControlText && e.Item.ForeColor != Color.Empty)
                e.TextColor = e.Item.ForeColor;
            else
                e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextSecondary;
            base.OnRenderItemText(e);
        }
    }

    // ComboBox під тему: системний flat-рендер лишає МОРДУ, РАМКУ і КНОПКУ-СТРІЛКУ світлими в
    // темній темі — візуальний стиль Windows ігнорує BackColor. Малюємо пункти самі (owner-draw),
    // а рамку й стрілку домальовуємо поверх у WM_PAINT кольорами теми.
    internal class ThemedCombo : ComboBox
    {
        public ThemedCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            DrawItem += OnDrawItemThemed;
        }

        private void OnDrawItemThemed(object s, DrawItemEventArgs e)
        {
            bool inList = (e.State & DrawItemState.ComboBoxEdit) == 0;
            bool sel = inList && (e.State & DrawItemState.Selected) != 0;
            var bg = sel ? Theme.Accent : Theme.ControlBg;
            var fg = sel ? Theme.AccentText : Theme.TextPrimary;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, e.Bounds, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private const int WM_PAINT = 0x000F;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT) return;
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    var r = ClientRectangle;
                    var btn = new Rectangle(r.Right - 18, r.Top + 1, 17, r.Height - 2);
                    using (var b = new SolidBrush(Theme.ControlBg)) g.FillRectangle(b, btn);
                    int cx = btn.Left + btn.Width / 2, cy = btn.Top + btn.Height / 2 - 1;
                    using (var p = new Pen(Theme.TextSecondary, 1.6f))
                    {
                        g.DrawLine(p, cx - 3, cy - 1, cx, cy + 2);
                        g.DrawLine(p, cx, cy + 2, cx + 3, cy - 1);
                    }
                    using (var p = new Pen(Theme.ControlBorder))
                        g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
            }
            catch { }
        }
    }

    // A Form whose TITLE BAR follows the app theme (Win10 20H1+/Win11 immersive dark mode), so a dark
    // app never gets a white caption bar. All app dialogs derive from this.
    internal class ThemedForm : Form
    {
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.SetDarkTitleBar(Handle, Theme.IsDark);
        }
    }

    internal static class Ui
    {
        public const string Name = "Dzherelo";

        public static Label Title(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = x, Top = y, Width = w, Height = 30, Font = Theme.Title, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        }

        public static Label Section(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = x, Top = y, Width = w, Height = 22, Font = Theme.Section, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
        }

        public static Label Body(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = x, Top = y, Width = w, Font = Theme.Body, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, AutoSize = false, Height = 20 };
        }

        public static Label Caption(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = x, Top = y, Width = w, Font = Theme.Caption, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, AutoSize = false, Height = 16 };
        }

        public static void StylePrimary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Accent;
            b.ForeColor = Theme.AccentText;
            b.Font = Theme.ButtonFont;
            b.Height = 34;
            b.Cursor = System.Windows.Forms.Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
        }

        public static void StyleSecondary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.ControlBorder;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Theme.ControlBg;
            b.ForeColor = Theme.TextPrimary;
            b.Font = Theme.ButtonFont;
            b.Height = 34;
            b.Cursor = System.Windows.Forms.Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = Theme.ControlHover;
        }

        // Support / donation buttons: a warm tint so they read as "support", distinct from the
        // neutral secondary actions and the blue primary. Colours track the current theme.
        public static void StyleDonate(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.DonateText;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = Theme.DonateBg;
            b.ForeColor = Theme.DonateText;
            b.Font = new Font("Segoe UI Semibold", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            b.Height = 34;
            b.Cursor = System.Windows.Forms.Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = Theme.DonateHover;
        }

        public static void StyleInput(TextBox t)
        {
            t.Font = Theme.Body;
            t.BackColor = Theme.ControlBg;
            t.ForeColor = Theme.TextPrimary;
            // Системну рамку FixedSingle малює візуальний стиль Windows — у темній темі вона
            // лишається СВІТЛОЮ і не перефарбовується (BackColor її не стосується). Прибираємо її
            // й малюємо власну 1px кольором теми на батьківській поверхні.
            t.BorderStyle = BorderStyle.None;
            t.AutoSize = false;
            HookThemedBorder(t);
            ApplyScrollTheme(t);
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appname, string idlist);

        // Системні смуги прокрутки лишаються СВІТЛИМИ в темній темі: візуальний стиль малює їх сам
        // і BackColor контролу на них не впливає. Перемикаємо на той самий темний рендер, що в
        // Провіднику (Win10 1809+). Скарга власника 2026-07-29: «залишки світлої теми в темній,
        // наприклад смужка вертикальної прокрутки» — саме цей випадок.
        // Тема читається В МОМЕНТ застосування, тож повторний виклик при зміні теми працює.
        public static void ApplyScrollTheme(Control t)
        {
            Action apply = () =>
            {
                try { SetWindowTheme(t.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); }
                catch { }
            };
            if (t.IsHandleCreated) apply();
            else t.HandleCreated += (s, e) => apply();
            Theme.Changed += () => apply();   // перемикання теми наживо
        }

        // Рамка 1px кольором Theme.ControlBorder довкола контролу, намальована на БАТЬКОВІ (сам
        // контрол своєї рамки більше не має — див. StyleInput). Повторні виклики не дублюють
        // підписку: маркер у Tag.
        private static void HookThemedBorder(Control t)
        {
            if ((t.Tag as string) == "themed-border") return;
            t.Tag = "themed-border";
            PaintEventHandler paint = (s, e) =>
            {
                if (!t.Visible) return;
                var r = t.Bounds;
                r.Inflate(1, 1);
                using (var p = new Pen(Theme.ControlBorder))
                    e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            };
            EventHandler redraw = (s, e) => { if (t.Parent != null) t.Parent.Invalidate(); };
            EventHandler attach = (s, e) => { if (t.Parent != null) t.Parent.Paint += paint; };
            if (t.Parent != null) t.Parent.Paint += paint; else t.ParentChanged += attach;
            t.LocationChanged += redraw;   // рух/розмір/сховання поля — перемалювати стару рамку
            t.SizeChanged += redraw;
            t.VisibleChanged += redraw;
        }

        // Open an external URL in the default browser (blog / donation links).
        public static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            }
        }

        // ---- dark title bar + themed message box (native MessageBox is always light) ----
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Make a window's title bar dark/light to match the theme (removes the white caption "edge").
        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            int v = dark ? 1 : 0;
            try
            {
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1+/Win11); 19 = pre-20H1 fallback.
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch { }
        }

        private struct MsgBtn
        {
            public string Text; public DialogResult Result; public bool Primary;
            public MsgBtn(string t, DialogResult r, bool p) { Text = t; Result = r; Primary = p; }
        }

        // Themed replacement for MessageBox.Show — a dark/light dialog with a dark title bar. Mirrors
        // the common MessageBox.Show(text, caption, buttons, icon) signature (returns DialogResult).
        public static DialogResult Msg(string text, string caption = null,
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        {
            using (var f = BuildMsg(text, caption, buttons, icon)) return f.ShowDialog();
        }

        // Builds the themed message dialog without showing it (Msg shows it; tests can capture it).
        internal static ThemedForm BuildMsg(string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            {
                var f = new ThemedForm();
                f.Text = string.IsNullOrEmpty(caption) ? Ui.Name : caption;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.BackColor = Theme.PageBg; f.ForeColor = Theme.TextPrimary; f.Font = Theme.Body;
                f.AutoScaleDimensions = new SizeF(96F, 96F); f.AutoScaleMode = AutoScaleMode.Dpi;

                const int pad = 18, isz = 32, tw = 360;
                Image ico = SysIcon(icon);
                int tl = pad + (ico != null ? isz + 14 : 0);
                Size ts = TextRenderer.MeasureText(text ?? "", Theme.Body, new Size(tw, 0), TextFormatFlags.WordBreak);
                int th = Math.Max(ico != null ? isz : 0, ts.Height);
                f.Controls.Add(new Label { Text = text ?? "", Left = tl, Top = pad, Width = tw, Height = th, Font = Theme.Body, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent });
                if (ico != null)
                    f.Controls.Add(new PictureBox { Left = pad, Top = pad, Width = isz, Height = isz, SizeMode = PictureBoxSizeMode.Zoom, Image = ico, BackColor = Color.Transparent });

                int cw = tl + tw + pad;
                int by = pad + th + 18;
                f.ClientSize = new Size(cw, by + 34 + pad);

                int right = cw - pad;
                Button accept = null, cancel = null;
                var defs = MsgButtons(buttons);
                for (int i = defs.Count - 1; i >= 0; i--)
                {
                    var d = defs[i];
                    int w = Math.Max(96, TextRenderer.MeasureText(d.Text, Theme.ButtonFont).Width + 30);
                    var b = new Button { Text = d.Text, Width = w, Top = by, DialogResult = d.Result };
                    if (d.Primary) StylePrimary(b); else StyleSecondary(b);
                    b.Left = right - b.Width; right = b.Left - 10;
                    f.Controls.Add(b);
                    if (d.Primary) accept = b;
                    if (d.Result == DialogResult.Cancel || (cancel == null && d.Result == DialogResult.No)) cancel = b;
                }
                if (accept != null) f.AcceptButton = accept;
                if (cancel != null) f.CancelButton = cancel;
                return f;
            }
        }

        private static System.Collections.Generic.List<MsgBtn> MsgButtons(MessageBoxButtons b)
        {
            var l = new System.Collections.Generic.List<MsgBtn>();
            switch (b)
            {
                case MessageBoxButtons.OKCancel:
                    l.Add(new MsgBtn("Гаразд", DialogResult.OK, true));
                    l.Add(new MsgBtn("Скасувати", DialogResult.Cancel, false)); break;
                case MessageBoxButtons.YesNo:
                    l.Add(new MsgBtn("Так", DialogResult.Yes, true));
                    l.Add(new MsgBtn("Ні", DialogResult.No, false)); break;
                case MessageBoxButtons.YesNoCancel:
                    l.Add(new MsgBtn("Так", DialogResult.Yes, true));
                    l.Add(new MsgBtn("Ні", DialogResult.No, false));
                    l.Add(new MsgBtn("Скасувати", DialogResult.Cancel, false)); break;
                default:
                    l.Add(new MsgBtn("Гаразд", DialogResult.OK, true)); break;
            }
            return l;
        }

        private static Image SysIcon(MessageBoxIcon icon)
        {
            try
            {
                switch (icon)
                {
                    case MessageBoxIcon.Error: return SystemIcons.Error.ToBitmap();
                    case MessageBoxIcon.Warning: return SystemIcons.Warning.ToBitmap();
                    case MessageBoxIcon.Question: return SystemIcons.Question.ToBitmap();
                    case MessageBoxIcon.Information: return SystemIcons.Information.ToBitmap();
                    default: return null;
                }
            }
            catch { return null; }
        }
    }
}
