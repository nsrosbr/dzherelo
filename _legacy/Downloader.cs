using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DticLoader;

public sealed class DownloadItem
{
    public DownloadItem(string ad, SubjectGroup group, string? title = null)
    {
        Ad = ad;
        Group = group;
        Title = title;
    }
    public string Ad { get; }
    public SubjectGroup Group { get; }
    public string? Title { get; }
}

public sealed class DownloadOutcome
{
    public List<string[]> IndexRows { get; } = new();
    public List<string[]> FailedRows { get; } = new();
    public bool Stopped { get; set; }
    public string? StopReason { get; set; }
    public int Downloaded { get; set; }
}

public sealed class Downloader
{
    public const int MinDelayMs = 5000;
    public const int DailyLimit = 200;

    private readonly HttpClient _http;
    private readonly string _outputRoot;
    private readonly int _delayMs;

    public Downloader(HttpClient http, string outputRoot, int delayMs = MinDelayMs)
    {
        _http = http;
        _outputRoot = outputRoot;
        _delayMs = Math.Max(MinDelayMs, delayMs);
    }

    public Action<string>? OnLog { get; set; }
    public Func<bool>? IsPaused { get; set; }
    public Action<int, int>? OnProgress { get; set; }
    public string? LastUrl { get; private set; }

    private void Log(string s) => OnLog?.Invoke(s);

    public async Task<DownloadOutcome> RunAsync(IReadOnlyList<DownloadItem> items, CancellationToken ct)
    {
        var outcome = new DownloadOutcome();
        Directory.CreateDirectory(_outputRoot);

        var index = LoadIndex();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        int downloadedToday = index.Values.Count(r => r.Length > 8 && r[7] == "ok" && r[8] == today);

        int n = 0;
        foreach (var it in items)
        {
            n++;
            if (ct.IsCancellationRequested)
            {
                outcome.Stopped = true;
                outcome.StopReason = "Зупинено користувачем";
                break;
            }
            while ((IsPaused?.Invoke() ?? false) && !ct.IsCancellationRequested)
                await Task.Delay(200, ct);

            if (downloadedToday >= DailyLimit)
            {
                outcome.Stopped = true;
                outcome.StopReason = $"Досягнуто денний ліміт {DailyLimit} файлів";
                break;
            }

            var ad = it.Ad.Trim().ToUpperInvariant();
            if (index.TryGetValue(ad, out var prev) && prev.Length > 7 && prev[7] == "ok")
            {
                Log($"пропуск {ad} (вже скачано)");
                continue;
            }
            OnProgress?.Invoke(n, items.Count);

            if (_delayMs > 0)
                await Task.Delay(_delayMs, ct);

            bool saved = false;
            bool blocked = false;
            string? blockReason = null;
            string lastErr = "немає PDF";

            foreach (var url in await CandidateUrlsAsync(it.Ad, ct))
            {
                LastUrl = url;
                var res = await TryGetAsync(url, ct);

                if (res.Bytes is { Length: > 4 } && IsPdf(res.Bytes))
                {
                    var dir = Path.Combine(
                        _outputRoot,
                        PathUtil.SanitizeName(it.Group.FieldName),
                        PathUtil.SanitizeName(it.Group.FolderName));
                    Directory.CreateDirectory(dir);

                    var title = PathUtil.SanitizeName(it.Title ?? "", "");
                    var fileName = title.Length == 0 ? ad + ".pdf" : ad + "_" + title + ".pdf";
                    var path = Path.Combine(dir, fileName);
                    await File.WriteAllBytesAsync(path, res.Bytes, ct);

                    var sha = Convert.ToHexString(SHA256.HashData(res.Bytes)).ToLowerInvariant();
                    var row = new[]
                    {
                        ad, it.Title ?? "", it.Group.FieldName, it.Group.FolderName,
                        path, res.Bytes.Length.ToString(), sha, "ok", today,
                    };
                    index[ad] = row;
                    outcome.IndexRows.Add(row);
                    downloadedToday++;
                    outcome.Downloaded++;
                    Log($"збережено {path} ({res.Bytes.Length} байт)");
                    saved = true;
                    break;
                }

                if (res.Status == 403 || res.Status == 429)
                {
                    blocked = true;
                    blockReason = $"HTTP {res.Status} — захист від ботів (WAF). Зупинка, без спроб обходу.";
                    break;
                }
                if (CaptchaLike(res.Body))
                {
                    blocked = true;
                    blockReason = "Виявлено капчу — розв'яжіть вручну в браузері.";
                    break;
                }
                lastErr = res.Error.Length > 0 ? res.Error : $"HTTP {res.Status}";
            }

            if (saved)
                continue;
            if (blocked)
            {
                index[ad] = new[] { ad, it.Title ?? "", it.Group.FieldName, it.Group.FolderName, "", "", "", "failed", today };
                outcome.FailedRows.Add(new[] { ad, it.Group.FieldName, it.Group.FolderName, blockReason! });
                outcome.Stopped = true;
                outcome.StopReason = blockReason;
                break;
            }
            index[ad] = new[] { ad, it.Title ?? "", it.Group.FieldName, it.Group.FolderName, "", "", "", "failed", today };
            outcome.FailedRows.Add(new[] { ad, it.Group.FieldName, it.Group.FolderName, lastErr });
            Log($"не вдалося {ad}: {lastErr} (продовжую)");
        }

        await WriteIndexAsync(index);
        var failedHeader = new[] { "AD", "поле", "група", "причина" };
        var failedRows = new List<IEnumerable<string>> { failedHeader };
        failedRows.AddRange(outcome.FailedRows);
        await Csv.WriteAsync(Path.Combine(_outputRoot, "failed.csv"), failedRows);

        return outcome;
    }

    private static bool IsPdf(byte[] b) =>
        b[0] == (byte)'%' && b[1] == (byte)'P' && b[2] == (byte)'D' && b[3] == (byte)'F' && b[4] == (byte)'-';

    private static bool CaptchaLike(string body) =>
        body.Contains("captcha", StringComparison.OrdinalIgnoreCase)
        || body.Contains("challenge", StringComparison.OrdinalIgnoreCase)
        || body.Contains("verify you are human", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> CandidateUrls(string ad)
    {
        var a = ad.Trim();
        if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new[] { a };

        var up = a.ToUpperInvariant();
        var bare = up.StartsWith("AD", StringComparison.Ordinal) ? up[2..] : up;
        var low = bare.ToLowerInvariant();
        return new[]
        {
            $"https://apps.dtic.mil/sti/trecms/pdf/{up}.pdf",
            $"https://apps.dtic.mil/sti/tr/pdf/{up}.pdf",
            $"https://apps.dtic.mil/dtic/tr/fulltext/u2/{low}.pdf",
        };
    }

    private async Task<IReadOnlyList<string>> CandidateUrlsAsync(string ad, CancellationToken ct)
    {
        var urls = new List<string>(CandidateUrls(ad));
        var archiveUrl = await InternetArchivePdfUrlAsync(ad, ct);
        if (archiveUrl != null)
        {
            Log("archive.org: знайдено PDF для " + ad.Trim().ToUpperInvariant());
            urls.Add(archiveUrl);
        }
        return urls;
    }

    private async Task<string?> InternetArchivePdfUrlAsync(string ad, CancellationToken ct)
    {
        var trimmed = ad.Trim();
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        var id = "DTIC_" + trimmed.ToUpperInvariant();
        Log("archive.org: перевіряю " + id);
        try
        {
            var json = await _http.GetStringAsync($"https://archive.org/metadata/{id}", ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("files", out var files))
                return null;
            foreach (var file in files.EnumerateArray())
            {
                var name = file.GetProperty("name").GetString();
                if (name?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true)
                    return $"https://archive.org/download/{id}/{Uri.EscapeDataString(name)}";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // No public mirror for this accession number, or archive.org unavailable.
        }
        return null;
    }

    private sealed record FetchResult(byte[]? Bytes, string Error, int Status, string Body);

    private async Task<FetchResult> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                string snippet = "";
                // Non-PDF 200 response (likely an HTML interstitial): keep a short
                // text prefix so the caller can detect a captcha/challenge page.
                if (bytes.Length > 0 && bytes[0] != (byte)'%')
                {
                    try { snippet = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4000)); }
                    catch { /* optional */ }
                }
                return new FetchResult(bytes, "", 200, snippet);
            }
            int status = (int)resp.StatusCode;
            string body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { /* body optional */ }
            return new FetchResult(null, $"HTTP {status} {resp.ReasonPhrase}", status, body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FetchResult(null, ex.Message, 0, "");
        }
    }

    private Dictionary<string, string[]> LoadIndex()
    {
        var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var rows = Csv.ReadAll(Path.Combine(_outputRoot, "index.csv"));
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i == 0 && r.Length > 0 && r[0].Equals("AD", StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.Length > 0 && r[0].Length > 0)
                dict[r[0].ToUpperInvariant()] = r;
        }
        return dict;
    }

    private async Task WriteIndexAsync(Dictionary<string, string[]> index)
    {
        var header = new[] { "AD", "назва", "поле", "група", "шлях", "розмір", "sha256", "статус", "дата" };
        var all = new List<IEnumerable<string>> { header };
        all.AddRange(index.Values);
        await Csv.WriteAsync(Path.Combine(_outputRoot, "index.csv"), all);
    }
}