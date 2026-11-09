using System.Reflection;

namespace Dzherelo;

// Вікно «Про програму» в логіці Valera: портрет-іконка, назва, версія, чесна заява про мережу,
// перелік того, що застосунок справді робить, автор і кнопки підтримки.
internal sealed class AboutForm : ThemedForm
{
    public AboutForm()
    {
        Text = "Про програму";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.PageBg;
        Font = Theme.Body;
        ClientSize = new Size(512, 452);
        Icon = Program.AppIcon();

        const int x = 26;

        var pic = new PictureBox
        {
            Left = x, Top = 24, Width = 120, Height = 160,
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.PageBg, BorderStyle = BorderStyle.None,
            Image = LoadMark(),
        };
        Controls.Add(pic);

        const int tx = 158;
        Controls.Add(new Label { Text = "DZHERELO", Left = tx, Top = 30, Width = 320, Height = 32, Font = Theme.Title, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent });
        Controls.Add(Ui.Caption("Збирач відкритих документів", tx, 64, 320));
        Controls.Add(Ui.Caption("версія " + Program.Version, tx, 82, 320));
        Controls.Add(new Label
        {
            Text = "БЕЗ телеметрії. БЕЗ щоденника.\nУсе локально; мережа — лише збір і оновлення.",
            Left = tx, Top = 108, Width = 330, Height = 44, Font = Theme.Body,
            ForeColor = Theme.TextSecondary, BackColor = Color.Transparent,
        });

        Controls.Add(Sep(x, 198, 512 - 2 * x));

        const string features =
            "•  87 бібліотек у 8 розділах: OAI-PMH, DSpace, WordPress, HTML, API\n" +
            "•  Каталог: sha256-дедуп, редакції, черга дошукування, чесний звіт\n" +
            "•  Теми з покриттям, добірки, HTML→PDF стилем канону\n" +
            "•  Журнал і код причини на кожне джерело — без вигадок";
        Controls.Add(new Label { Text = features, Left = x, Top = 208, Width = 512 - 2 * x, Height = 92, Font = Theme.Body, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent });

        Controls.Add(Sep(x, 308, 512 - 2 * x));

        Controls.Add(new Label { Text = "Автор:  Павло Ісаєв", Left = x, Top = 320, Width = 430, Height = 20, Font = new Font("Segoe UI Semibold", 9.75f, FontStyle.Regular, GraphicsUnit.Point), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent });

        const string contactPrefix = "Контакт:  ";
        var blog = new LinkLabel
        {
            Text = contactPrefix + "caussa.blog", Left = x, Top = 342, Width = 430, Height = 20,
            Font = Theme.Body, BackColor = Color.Transparent, ForeColor = Theme.TextPrimary,
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentPressed, VisitedLinkColor = Theme.Accent,
        };
        blog.LinkArea = new LinkArea(contactPrefix.Length, "caussa.blog".Length);
        blog.LinkClicked += (_, _) => Ui.OpenUrl("https://caussa.blog");
        Controls.Add(blog);
        Controls.Add(Ui.Caption("© 2026 · Павло Ісаєв", x, 366, 430));

        const int by = 402;
        var close = new Button { Text = "Закрити", Width = 96, DialogResult = DialogResult.OK };
        Ui.StylePrimary(close);
        close.Left = ClientSize.Width - x - close.Width; close.Top = by;

        var folder = new Button { Text = "Тека налаштувань", Width = 150 };
        Ui.StyleSecondary(folder);
        folder.Font = new Font("Segoe UI Semibold", 9.75f, FontStyle.Regular, GraphicsUnit.Point);
        folder.Left = close.Left - folder.Width - 10; folder.Top = by;
        folder.Click += (_, _) => OpenDataFolder();

        var jar = new Button { Text = "Монобанк", Width = 104, Top = by, Left = x };
        Ui.StyleDonate(jar);
        jar.Click += (_, _) => Ui.OpenUrl("https://send.monobank.ua/jar/52FQ1MSqEK?ref=caussa");

        var kofi = new Button { Text = "Ko-fi", Width = 70, Top = by, Left = x + jar.Width + 8 };
        Ui.StyleDonate(kofi);
        kofi.Click += (_, _) => Ui.OpenUrl("https://ko-fi.com/pavloisaiev?ref=caussa");

        Controls.Add(close); Controls.Add(folder); Controls.Add(jar); Controls.Add(kofi);
        AcceptButton = close;
        CancelButton = close;
    }

    private static void OpenDataFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo");
        try { Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
    }

    // М'який край: альфа плавно спадає до 0 біля рамки, тому фото «розчиняється» у фоні
    // в обох темах, а не тільки у світлій (там це вшите у файл).
    private static Bitmap Feather(Bitmap src, int px)
    {
        var dst = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        src.Dispose();

        var data = dst.LockBits(new Rectangle(0, 0, dst.Width, dst.Height),
            System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int n = data.Width * data.Height;
            var buf = new byte[n * 4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            int w = data.Width, h = data.Height;
            for (int y = 0; y < h; y++)
            {
                int dy = Math.Min(y, h - 1 - y);
                for (int x = 0; x < w; x++)
                {
                    int dx = Math.Min(x, w - 1 - x);
                    int d = Math.Min(dx, dy);
                    if (d >= px) continue;
                    // smoothstep: без різкого сходинки на краю
                    double k = (double)d / px;
                    double f = k * k * (3 - 2 * k);
                    buf[(y * w + x) * 4 + 3] = (byte)(buf[(y * w + x) * 4 + 3] * f);
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally { dst.UnlockBits(data); }
        return dst;
    }

    private static Panel Sep(int x, int y, int w) =>
        new() { Left = x, Top = y, Width = w, Height = 1, BackColor = Theme.CardBorder };

    // Фото автора, як у Valera: темна тема — з рамкою, світла — розчиняється у фоні.
    private static Image? LoadMark()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var stream = Theme.IsDark ? asm.GetManifestResourceStream("authorphoto_dark") : null;
            stream ??= asm.GetManifestResourceStream("authorphoto");
            if (stream == null) return null;
            using (stream)
            using (var tmp = Image.FromStream(stream))
                return Feather(new Bitmap(tmp), 18);
        }
        catch { return null; }
    }
}
