using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Dzherelo;

// HTML → PDF стилем канону (SPEC §24, T7): сайт без PDF зберігається як аркуш A4 у
// типографіці канону, БЕЗ бланка (без емблеми, реквізитів і підпису — "лише стиль").
// Значення — з tokens.json (поля 30/20/20/20, PT Sans 10,5, Montserrat капсом,
// акцент #F29222, лінійки 0,5/1/1,5 pt, колонтитул «бібліотека · тема · дата · стор. N»).
// Рушій — WebView2 PrintToPdfAsync: нуль python/Word-залежностей.
public static class CanonPdf
{
    public const string Host = "fonts.dzherelo";

    // --- шрифти: вбудовані ресурси видобуваються на диск; у PDF їх посилає віртуальний host ---
    private static readonly (string Res, string Family, int Weight, bool Italic)[] Fonts =
    {
        ("Montserrat-Bold", "Montserrat", 700, false),
        ("Montserrat-ExtraBold", "Montserrat", 800, false),
        ("PT_Sans-Web-Regular", "PT Sans", 400, false),
        ("PT_Sans-Web-Bold", "PT Sans", 700, false),
        ("PT_Sans-Web-Italic", "PT Sans", 400, true),
        ("PT_Sans-Narrow-Web-Bold", "PT Sans Narrow", 700, false),
    };

    public static string FontsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo", "fonts");

    private static string BuildFontFaces()
    {
        var sb = new StringBuilder();
        foreach (var (res, family, weight, italic) in Fonts)
        {
            sb.Append("@font-face{font-family:'").Append(family)
              .Append("';src:url('https://").Append(Host).Append("/").Append(res).Append(".ttf');font-weight:")
              .Append(weight).Append(italic ? ";font-style:italic" : "").Append(";}\n");
        }
        return sb.ToString();
    }

    private static readonly Lazy<string> FontFaces = new(BuildFontFaces);

    // Видобуває вбудовані .ttf на диск (один раз). Якщо ресурс відсутній — браузер упаде на системний шрифт.
    public static string EnsureFonts()
    {
        Directory.CreateDirectory(FontsDir);
        var asm = typeof(CanonPdf).Assembly;
        foreach (var (res, _, _, _) in Fonts)
        {
            var target = Path.Combine(FontsDir, res + ".ttf");
            if (File.Exists(target)) continue;
            var name = "Dzherelo.Fonts." + res + ".ttf";
            using var s = asm.GetManifestResourceStream(name);
            if (s is null) continue;
            using var f = File.Create(target);
            s.CopyTo(f);
        }
        return FontsDir;
    }

    // Канон "без бланка": єдине джерело значень — tokens.json (page/type/color/rule). Колонтитул
    // «бібліотека · тема · дата · стор. N» друкується через @page margin-boxes.
    public static string Css(string library, string topic, string date)
    {
        return FontFaces.Value + $@"
@page {{ size: A4 portrait; margin: 20mm 20mm 20mm 30mm;
  @bottom-left  {{ content: ""{CssEscape(library)} · {CssEscape(topic)} · {CssEscape(date)}""; font: 700 7pt/1.2 'PT Sans Narrow', sans-serif; letter-spacing: .1em; text-transform: uppercase; color: #6C7787; }}
  @bottom-right {{ content: ""стор. "" counter(page); font: 700 7pt/1.2 'PT Sans Narrow', sans-serif; color: #6C7787; }}
}}
* {{ box-sizing: border-box; }}
html {{ -webkit-print-color-adjust: exact; print-color-adjust: exact; }}
body {{ margin: 0; color: #1B2434; font: 400 10.5pt/1.55 'PT Sans', sans-serif; text-align: left; hyphens: none; }}
h1 {{ margin: 0; font: 800 20pt/1.15 'Montserrat', sans-serif; text-transform: uppercase; color: #0F1724; }}
h2 {{ margin: 6mm 0 2mm; font: 700 14pt/1.05 'Montserrat', sans-serif; text-transform: uppercase; color: #0F1724; border-bottom: .5pt solid #DDE1E7; padding-bottom: 1.6mm; }}
h3 {{ margin: 0 0 2mm; font: 700 10.5pt/1.2 'Montserrat', sans-serif; text-transform: uppercase; color: #0F1724; }}
p {{ margin: 0 0 3mm; text-wrap: pretty; }}
a {{ color: #B86A0C; text-decoration: none; }}
.meta {{ color: #6C7787; font-size: 8pt; margin: 1.5mm 0 0; }}
.kicker {{ font: 700 7pt/1.2 'PT Sans Narrow', sans-serif; letter-spacing: .16em; text-transform: uppercase; color: #B86A0C; margin: 0 0 6mm; }}
.rule-top {{ border-top: 1.5pt solid #0F1724; margin-top: 5mm; }}
.accent-bar {{ height: 1.5pt; width: 22mm; background: #F29222; margin: 2mm 0 7mm; }}
table {{ width: 100%; border-collapse: collapse; font-size: 8.5pt; line-height: 1.35; margin: 1mm 0 4mm; font-variant-numeric: tabular-nums; }}
thead th {{ font: 700 7pt/1.2 'PT Sans Narrow', sans-serif; letter-spacing: .16em; text-transform: uppercase; color: #0F1724; text-align: left; border-bottom: 1pt solid #0F1724; padding: 1.6mm 0; }}
thead th.num, td.num {{ text-align: right; }}
tbody td {{ border-bottom: .5pt solid #EDEFF2; padding: 1.5mm 0; }}
strong {{ color: #0F1724; }}
";
    }

    private static string CssEscape(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

    // Повний документ: заголовок + вміст + колонтитул. bodyHtml — уже чистий текстовий вміст.
    public static string Document(string title, string library, string topic, string date, string bodyHtml)
    {
        return "<!doctype html><html lang=\"uk\"><head><meta charset=\"utf-8\"><style>"
             + Css(library, topic, date)
             + "</style></head><body>"
             + "<p class=\"kicker\">" + Html(TopicKicker(topic)) + "</p>"
             + "<h1>" + Html(title) + "</h1>"
             + "<p class=\"meta\">" + Html(library) + " · " + Html(topic) + " · " + Html(date) + "</p>"
             + "<hr class=\"rule-top\"><div class=\"accent-bar\"></div>"
             + bodyHtml
             + "</body></html>";
    }

    private static string TopicKicker(string topic) => string.IsNullOrWhiteSpace(topic) ? "Dzherelo" : topic;

    private static string Html(string s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // Легке витягнення тексту зі сторінки: скрипти/стилі геть, блоки в абзаци.
    // shortcut: це не readability-рушій; навігацію й повтори ми не вичищаємо.
    public static string ExtractText(string rawHtml)
    {
        if (string.IsNullOrWhiteSpace(rawHtml)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(rawHtml,
            @"(?is)<(script|style|noscript|head)[^>]*>.*?</\1>", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?i)<(p|div|h[1-6]|li|tr|br|section|article)[^>]*>", "\n");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", " ");
        t = System.Net.WebUtility.HtmlDecode(t);
        var paras = new StringBuilder();
        foreach (var line in t.Split('\n'))
        {
            var clean = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ").Trim();
            if (clean.Length >= 12) paras.Append("<p>").Append(Html(clean)).Append("</p>\n");
        }
        return paras.ToString();
    }

    // Рендер PDF на окремому STA-потоці з власним message loop: безпечно з будь-якого виклику.
    public static Task RenderToPdfAsync(string html, string outPath, CancellationToken ct = default)
    {
        outPath = Path.GetFullPath(outPath);   // WebView2 PrintToPdf вимагає абсолютний шлях
        return RenderCoreAsync(html, outPath, ct);
    }

    private static Task RenderCoreAsync(string html, string outPath, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RenderLoop(html, outPath, tcs))
        {
            IsBackground = true,
            Name = "canon-pdf-render",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    private static void RenderLoop(string html, string outPath, TaskCompletionSource<int> tcs)
    {
        try
        {
            var fontsDir = EnsureFonts();
            using var form = new Form
            {
                Width = 900, Height = 1200, Opacity = 0, ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.Manual,
            };
            var webView = new WebView2
            {
                Dock = DockStyle.Fill,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(Path.GetTempPath(), "dzherelo-canvas-" + Guid.NewGuid().ToString("N")[..8]),
                },
            };
            form.Controls.Add(webView);
            form.Load += async (_, _) =>
            {
                try
                {
                    await webView.EnsureCoreWebView2Async();
                    var core = webView.CoreWebView2;
                    core.Settings.AreDefaultScriptDialogsEnabled = false;
                    core.Settings.IsScriptEnabled = false;
                    core.SetVirtualHostNameToFolderMapping(Host, fontsDir, CoreWebView2HostResourceAccessKind.Allow);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                    var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    core.NavigationCompleted += (_, e) => nav.TrySetResult(e.IsSuccess);
                    core.NavigateToString(html);
                    await nav.Task;
                    await Task.Delay(250);   // дати шрифтам (віртуальний host) завантажитись до друку
                    _ = await core.PrintToPdfAsync(outPath);
                    tcs.TrySetResult(0);
                }
                catch (Exception ex) { tcs.TrySetException(new Exception(ex.ToString())); }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        catch (Exception ex) { tcs.TrySetException(new Exception(ex.ToString())); }
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var checks = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail = "") => checks.Add((name, ok, detail));

        var css = Css("Бібліотека", "Тема", "2026-11-07");

        Check("канон: A4", css.Contains("size: A4 portrait", StringComparison.Ordinal), "A4");
        Check("канон: поля 30/20/20/20", css.Contains("margin: 20mm 20mm 20mm 30mm", StringComparison.Ordinal),
            "30/20/20/20 мм");
        Check("канон: PT Sans 10,5", css.Contains("10.5pt", StringComparison.Ordinal)
            && css.Contains("'PT Sans'", StringComparison.Ordinal), "основний текст");
        Check("канон: Montserrat капсом", css.Contains("'Montserrat'", StringComparison.Ordinal)
            && css.Contains("text-transform: uppercase", StringComparison.Ordinal), "заголовки");
        Check("канон: акцент #F29222", css.Contains("#F29222", StringComparison.Ordinal), "акцент");
        Check("канон: лінійки 0,5/1/1,5", css.Contains(".5pt", StringComparison.Ordinal)
            && css.Contains("1pt solid", StringComparison.Ordinal) && css.Contains("1.5pt", StringComparison.Ordinal),
            "hair/base/strong");
        Check("канон: колонтитул", css.Contains("стор. ", StringComparison.Ordinal)
            && css.Contains("counter(page)", StringComparison.Ordinal)
            && css.Contains("Бібліотека · Тема · 2026-11-07", StringComparison.Ordinal),
            "бібліотека · тема · дата · стор. N");

        var doc = Document("Заголовок", "Бібліотека", "Тема", "2026-11-07", "<p>Абзац тексту.</p>");
        Check("канон: документ самодостатній", doc.Contains("<!doctype html>", StringComparison.Ordinal)
            && doc.Contains("<h1>Заголовок</h1>", StringComparison.Ordinal)
            && doc.Contains("<p>Абзац тексту.</p>", StringComparison.Ordinal), "заголовок + вміст");

        var text = ExtractText("<html><head><title>x</title></head><body><script>var a=1;</script>"
            + "<p>Перший абзац довжиною достатньою для збереження.</p>"
            + "<div>Другий абзац теж достатньо довгий для збереження.</div></body></html>");
        Check("канон: витягнення тексту", text.Contains("Перший абзац", StringComparison.Ordinal)
            && text.Contains("Другий абзац", StringComparison.Ordinal)
            && !text.Contains("var a=1", StringComparison.Ordinal), "без скриптів");

        return checks.ToArray();
    }
}
