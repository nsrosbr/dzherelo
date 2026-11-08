using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dzherelo;

/// <summary>Канал оновлень: latest.txt у релізі GitHub. Приватний репо — через API з токеном із DPAPI,
/// публічний — простим завантаженням. Підміна exe лише після збігу sha256.</summary>
public static class Updater
{
    public const string Repo = "nsrosbr/dzherelo";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public sealed record Manifest(string Version, string Url, string Sha256, string Notes);

    private static string? Token() => Secrets.Get("github.token");

    private static HttpRequestMessage Req(string url)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, url);
        m.Headers.UserAgent.ParseAdd("dzherelo-updater");
        m.Headers.Accept.ParseAdd("application/vnd.github+json");
        var t = Token();
        if (!string.IsNullOrWhiteSpace(t)) m.Headers.Authorization = new("Bearer", t);
        return m;
    }

    /// <summary>Знайти реліз і прочитати latest.txt. Кидає з поясненням, якщо канал недоступний.</summary>
    public static async Task<Manifest> FetchManifestAsync(CancellationToken ct = default)
    {
        using var relResp = await Http.SendAsync(Req($"https://api.github.com/repos/{Repo}/releases/latest"), ct);
        if (!relResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"реліз недоступний: HTTP {(int)relResp.StatusCode}" +
                ((int)relResp.StatusCode == 404 && string.IsNullOrWhiteSpace(Token())
                    ? " — приватний репозиторій без токена (--set-secret github.token)" : ""));
        using var doc = JsonDocument.Parse(await relResp.Content.ReadAsStringAsync(ct));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
        var lt = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "latest.txt");
        if (lt.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("у релізі немає latest.txt");

        var rawReq = Req(lt.GetProperty("url").GetString()!);
        rawReq.Headers.Accept.Clear();
        rawReq.Headers.Accept.ParseAdd("application/octet-stream");   // інакше API віддає JSON-метадані, не файл
        var raw = await Http.SendAsync(rawReq, ct);
        raw.EnsureSuccessStatusCode();
        var text = await raw.Content.ReadAsStringAsync(ct);
        var kv = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Select(l => l.Split('=', 2))
                     .Where(p => p.Length == 2)
                     .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (!kv.TryGetValue("version", out var ver) || !kv.TryGetValue("sha256", out var sha))
            throw new InvalidOperationException("latest.txt без version/sha256");
        return new Manifest(ver, kv.GetValueOrDefault("url", ""), sha, kv.GetValueOrDefault("notes", ""));
    }

    public static Version Current => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static bool IsNewer(Manifest m) =>
        Version.TryParse(m.Version, out var v) && v > Current;

    /// <summary>Скачати новий exe, звірити sha256, підмінити після виходу процесу й перезапустити.</summary>
    public static async Task<string> ApplyAsync(Manifest m, CancellationToken ct = default)
    {
        var dir = AppContext.BaseDirectory;
        var exe = Environment.ProcessPath ?? Path.Combine(dir, "Dzherelo.exe");
        var staged = Path.Combine(dir, $"Dzherelo-{m.Version}.new");

        var bytes = await DownloadAssetAsync(m, ct);
        var got = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(got, m.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"sha256 не збігся: отримано {got}, у маніфесті {m.Sha256}");
        await File.WriteAllBytesAsync(staged, bytes, ct);

        var bat = Path.Combine(dir, "_update.cmd");
        await File.WriteAllTextAsync(bat,
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"move /y \"{staged}\" \"{exe}\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "del \"%~f0\"\r\n", ct);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false });
        return $"оновлення до {m.Version} готове ({bytes.Length:N0} байт, sha256 збігся) — застосунок перезапуститься";
    }

    /// <summary>Тягне файл асета через API (працює і для приватного репозиторію).</summary>
    private static async Task<byte[]> DownloadAssetAsync(Manifest m, CancellationToken ct)
    {
        var parts = m.Url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tag = parts.Length >= 2 ? parts[^2] : "v" + m.Version;
        var name = parts.Length >= 1 ? parts[^1] : "Dzherelo.exe";

        using var relResp = await Http.SendAsync(Req($"https://api.github.com/repos/{Repo}/releases/tags/{tag}"), ct);
        if (!relResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"реліз {tag}: HTTP {(int)relResp.StatusCode}");
        using var doc = JsonDocument.Parse(await relResp.Content.ReadAsStringAsync(ct));
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"у релізі {tag} немає асета {name}");

        var req = Req(asset.GetProperty("url").GetString()!);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd("application/octet-stream");
        using var r = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"асет: HTTP {(int)r.StatusCode}");
        return await r.Content.ReadAsByteArrayAsync(ct);
    }
}
