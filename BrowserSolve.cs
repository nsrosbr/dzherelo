using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Net;

namespace Dzherelo;

// Cloudflare (ISW, частина think-tank'ів) пускає лише справжній браузер: будь-який HTTP-клієнт
// отримує «Just a moment…». Проходимо челендж у WebView2 і забираємо його cookies — далі
// звичайні запити з цим cf_clearance проходять без браузера.
internal static class BrowserSolve
{
    private static readonly Dictionary<string, CookieCollection> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Busy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>UA, з яким браузер реально пройшов челендж: cf_clearance привʼязаний до нього.</summary>
    public static string? LastUserAgent { get; private set; }

    // Увага: Cloudflare вставляє /cdn-cgi/challenge-platform у ЗВИЧАЙНІ сторінки — за цим маркером
    // не можна судити. Челендж — це коротка сторінка з «Just a moment» або формою перевірки.
    public static bool IsChallenge(string? body) =>
        body != null && (body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                         || (body.Length < 20000 && body.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Cookies перевіреного візиту для хоста (null — челендж не пройдено).</summary>
    public static async Task<CookieCollection?> CookiesForAsync(string url, CancellationToken ct)
    {
        var host = new Uri(url).Host;
        if (Cache.TryGetValue(host, out var known)) return known;
        await Gate.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(host, out known)) return known;
            var (fresh, _) = await RunAsync(url, ct);
            if (fresh is { Count: > 0 }) Cache[host] = fresh;
            return fresh;
        }
        finally { Gate.Release(); }
    }

    public static void Forget(string host) => Cache.Remove(host);


    /// <summary>
    /// Текст сторінки ТИМ САМИМ браузером: cf_clearance привʼязаний і до TLS-відбитка,
    /// тому звичайний HttpClient після челенджу все одно отримує 403.
    /// </summary>
    public static Task<string?> GetTextAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var form = new Form
            {
                Width = 1100, Height = 800, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000),
                FormBorderStyle = FormBorderStyle.None,
            };
            var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);

            form.Shown += async (_, _) =>
            {
                try
                {
                    var data = Path.Combine(Path.GetTempPath(), "dzherelo-webview2", "main");
                    var env = await CoreWebView2Environment.CreateAsync(null, data);
                    await view.EnsureCoreWebView2Async(env);

                    var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    view.CoreWebView2.NavigationCompleted += (_, e) => arrived.TrySetResult(e.IsSuccess);
                    view.CoreWebView2.Navigate(url);
                    await Task.WhenAny(arrived.Task, Task.Delay(45000, ct));
                    await Task.Delay(1200, ct);   // дати JS дорендерити

                    var raw = await view.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                    var html = UnescapeScriptResult(raw);
                    tcs.TrySetResult(string.IsNullOrWhiteSpace(html) ? null : html);
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { try { form.Close(); } catch { } }
            };

            try { Application.Run(form); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);
    }

    // ExecuteScriptAsync віддає JSON-рядок у лапках із екрануванням.
    private static string UnescapeScriptResult(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "null") return "";
        if (raw.Length > 1 && raw[0] == '"')
            return System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "";
        return raw;
    }

    /// <summary>Один візит: пройти челендж, забрати cookies І html тієї ж сторінки.</summary>
    public static Task<(CookieCollection? cookies, string? html)> SolvePageAsync(string url, CancellationToken ct)
        => RunAsync(url, ct);

    private static Task<(CookieCollection? cookies, string? html)> RunAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(CookieCollection?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var form = new Form
            {
                Width = 1000, Height = 760, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000),
                FormBorderStyle = FormBorderStyle.None, Text = "Dzherelo — перевірка сайту",
            };
            var view = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);

            form.Shown += async (_, _) =>
            {
                try
                {
                    var data = Path.Combine(Path.GetTempPath(), "dzherelo-webview2", "main");
                    var env = await CoreWebView2Environment.CreateAsync(null, data);
                    await view.EnsureCoreWebView2Async(env);
                    try { LastUserAgent = view.CoreWebView2.Settings.UserAgent; } catch { }
                    view.CoreWebView2.Navigate(url);

                    // челендж сам перенаправляє на сайт: чекаємо, поки заголовок перестане бути «Just a moment…»
                    string? title = null;
                    for (int i = 0; i < 90 && !ct.IsCancellationRequested; i++)
                    {
                        await Task.Delay(500);
                        try { title = await view.CoreWebView2.ExecuteScriptAsync("document.title"); } catch { }
                        if (title != null && !title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                                          && !title.Contains("moment", StringComparison.OrdinalIgnoreCase)) break;
                    }

                    Console.WriteLine("  ↻ браузер: заголовок після челенджу = " + (title ?? "—"));
                    string? html = null;
                    try
                    {
                        var raw = await view.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                        html = UnescapeScriptResult(raw);
                    }
                    catch { }
                    var jar = new CookieCollection();
                    var list = await view.CoreWebView2.CookieManager.GetCookiesAsync(url);
                    foreach (var c in list)
                    {
                        try { jar.Add(new Cookie(c.Name, c.Value, string.IsNullOrEmpty(c.Path) ? "/" : c.Path, c.Domain)); }
                        catch { /* домен із крапкою/портом — не критично */ }
                    }
                    tcs.TrySetResult((jar, html));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { try { form.Close(); } catch { } }
            };

            try { Application.Run(form); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);
    }
}
