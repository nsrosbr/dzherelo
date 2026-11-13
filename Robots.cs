
using System.Collections.Concurrent;

namespace Dzherelo;

// Професійний мінімум: читаємо robots.txt і поважаємо заборонені шляхи та Crawl-delay.
// Кеш на добу; 4xx (немає файла) = дозволено; 5xx теж дозволяємо — інакше одна зламана
// сторінка вимкнула б цілу бібліотеку (позначається в звіті як 'robots недоступний').
public static class Robots
{
    public sealed record Rule(List<string> Disallow, List<string> Allow, int DelayMs, DateTime At, string Status);

    private static readonly ConcurrentDictionary<string, Rule> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(6),
    };
    private static readonly SemaphoreSlim FetchGate = new(2, 2);

    public static async Task<(bool Allowed, int DelayMs, string Status)> CheckAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return (true, 0, "no-url");
        var key = u.Scheme + "://" + u.Host;

        if (!Cache.TryGetValue(key, out var rule) || DateTime.UtcNow - rule.At > TimeSpan.FromDays(1))
        {
            rule = await LoadAsync(key, ct);
            Cache[key] = rule;
        }

        var path = u.PathAndQuery;
        if (rule.Status != "ok") return (true, rule.DelayMs, rule.Status);

        // довший збіг керує: Allow перекриває Disallow, якщо він специфічніший
        var bestAllow = rule.Allow.Where(p => p.Length > 0 && path.StartsWith(p, StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault();
        var bestDis = rule.Disallow.Where(p => p.Length > 0 && path.StartsWith(p, StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault();
        var allowed = bestDis == null || (bestAllow != null && bestAllow.Length >= bestDis.Length);
        return (allowed, rule.DelayMs, allowed ? "ok" : "robots:" + bestDis);
    }

    private static async Task<Rule> LoadAsync(string origin, CancellationToken ct)
    {
        await FetchGate.WaitAsync(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, origin + "/robots.txt");
            req.Headers.TryAddWithoutValidation("User-Agent", Fetcher.UserAgent);
            using var resp = await Http.SendAsync(req, ct);
            var now = DateTime.UtcNow;

            if (!resp.IsSuccessStatusCode)
                return new Rule(new(), new(), 0, now, "robots " + (int)resp.StatusCode);

            var text = await resp.Content.ReadAsStringAsync(ct);
            return Parse(text, now);
        }
        catch
        {
            return new Rule(new(), new(), 0, DateTime.UtcNow, "robots недоступний");
        }
        finally { FetchGate.Release(); }
    }

    public static Rule Parse(string text, DateTime now)
    {
        var dis = new List<string>(); var allow = new List<string>();
        int delay = 0;
        bool applies = false, sawSpecific = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var i = line.IndexOf(':');
            if (i <= 0) continue;
            var key = line[..i].Trim().ToLowerInvariant();
            var val = line[(i + 1)..].Trim();

            if (key == "user-agent")
            {
                // '*' = загальні правила; наш UA не називаємо — беремо '*' (найсуворіший розумний вибір)
                applies = val == "*" || val.Contains("dzherelo", StringComparison.OrdinalIgnoreCase);
                if (val.Contains("dzherelo", StringComparison.OrdinalIgnoreCase)) sawSpecific = true;
                continue;
            }
            if (!applies) continue;

            switch (key)
            {
                case "disallow": if (val.Length > 0) dis.Add(val); break;
                case "allow": if (val.Length > 0) allow.Add(val); break;
                case "crawl-delay":
                    if (int.TryParse(val, out var s) && s > 0) delay = (int)Math.Min(s * 1000, 30000);
                    break;
            }
        }
        return new Rule(dis, allow, delay, now, "ok" + (sawSpecific ? "+named" : ""));
    }
}
