using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dzherelo;

public sealed class BlockedException(string message) : Exception(message);
public sealed class NetworkDownException(string message) : Exception(message);
public sealed class LoginRequiredException(string message) : Exception(message);
public sealed class NotFoundException(string message) : Exception(message);

// One client for every source: browser header profile (that is the whole .mil gate),
// per-host serialization, retry with backoff, streaming to disk with a size cap.
public sealed class Fetcher : IDisposable
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public int MinIntervalMs { get; set; } = 1000;
    public long MaxBytes { get; set; } = 400L * 1024 * 1024;

    // .mil name servers answer SERVFAIL for minutes at a time, so plain DNS cannot be the
    // only way to find a host. Pins -> disk cache -> system resolver -> DNS over HTTPS.
    // Cloudflare decides by TLS fingerprint, and .NET's Schannel profile is on its block
    // list while the system curl passes. Those hosts get curl as a transport.
    private readonly HashSet<string> _curlHosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string CurlExe =
        File.Exists(@"C:\Windows\System32\curl.exe") ? @"C:\Windows\System32\curl.exe" : "curl";

    private readonly Dictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _cachePath;
    private static readonly HttpClient Doh = CreateDoh();

    private readonly HttpClient _http;
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _lastHit = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static HttpClient CreateDoh()
    {
        // Connect by IP so bootstrapping the resolver never needs the resolver.
        var h = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8),
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        };
        h.ConnectCallback = async (ctx, ct) =>
        {
            var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await sock.ConnectAsync(IPAddress.Parse(ctx.DnsEndPoint.Host), ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(sock, true);
        };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/dns-json");
        return c;
    }

    public Fetcher(string? stateDir = null)
    {
        if (stateDir != null)
        {
            _cachePath = Path.Combine(stateDir, "dns-cache.json");
            try
            {
                if (File.Exists(_cachePath))
                    foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_cachePath)) ?? new())
                        _cache[kv.Key] = kv.Value;
            }
            catch { /* cache is an optimisation */ }
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            // Digital Commons (JCWD, USAWC Press) 403s a PDF until the visitor has loaded
            // a page on the same host and picked up its session cookie.
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        handler.ConnectCallback = async (ctx, ct) =>
        {
            var ip = await ResolveAsync(ctx.DnsEndPoint.Host, ct);
            var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await sock.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct);
                return new NetworkStream(sock, true);
            }
            catch { sock.Dispose(); throw; }
        };
        _http.DefaultRequestVersion = new Version(2, 0);
        _http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        var h = _http.DefaultRequestHeaders;
        h.TryAddWithoutValidation("User-Agent", UserAgent);
        h.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/pdf,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        h.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
        h.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        h.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        h.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
    }

    public void Pin(string host, string ip) => _pins[host] = ip;
    public bool UsesCurl(string url) => _curlHosts.Contains(HostOf(url));

    private async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return literal;

        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct).WaitAsync(TimeSpan.FromSeconds(4), ct);
            var v4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
            if (v4 != null) { Remember(host, v4.ToString()); return v4; }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* fall through */ }

        if (_cache.TryGetValue(host, out var cached) && IPAddress.TryParse(cached, out var c)) return c;

        foreach (var resolver in new[] { "https://1.1.1.1/dns-query", "https://8.8.8.8/resolve" })
        {
            try
            {
                var json = await Doh.GetStringAsync(resolver + "?name=" + Uri.EscapeDataString(host) + "&type=A", ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Answer", out var answers))
                    foreach (var a in answers.EnumerateArray())
                        if (a.GetProperty("type").GetInt32() == 1 &&
                            IPAddress.TryParse(a.GetProperty("data").GetString(), out var ip))
                        {
                            Remember(host, ip.ToString());
                            return ip;
                        }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* next resolver */ }
        }

        if (_pins.TryGetValue(host, out var pinned) && IPAddress.TryParse(pinned, out var p))
        {
            Remember(host, p.ToString());
            return p;
        }

        throw new NetworkDownException("DNS не резолвить " + host + " (система, кеш, DoH і ручний резолв)");
    }

    private void Remember(string host, string ip)
    {
        var changed = _cache.TryGetValue(host, out var old) && old != ip;
        _cache[host] = ip;
        if (_cachePath == null || (!changed && old != null)) return;
        try { File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache)); } catch { }
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    private async Task GateAsync(string url, int rps, CancellationToken ct)
    {
        var host = HostOf(url);
        SemaphoreSlim? gate;
        lock (_lock)
        {
            if (!_gates.TryGetValue(host, out gate)) _gates[host] = gate = new SemaphoreSlim(1, 1);
        }
        await gate!.WaitAsync(ct);
        try
        {
            long last;
            lock (_lock) _lastHit.TryGetValue(host, out last);
            var interval = rps > 0 ? Math.Max(MinIntervalMs, 1000 / rps) : MinIntervalMs;
            var wait = last + interval - Environment.TickCount64;
            if (wait > 0) await Task.Delay((int)wait, ct);
            lock (_lock) _lastHit[host] = Environment.TickCount64;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(string url, int rps, CancellationToken ct)
    {
        await GateAsync(url, rps, ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public async Task<HttpResponseMessage> SendAsync(string url, int rps, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(1500 * attempt, ct);
            try
            {
                var resp = await SendOnceAsync(url, rps, ct);
                var code = (int)resp.StatusCode;
                if (code is 403 or 429 or 503)
                {
                    var retryAfter = (int)(resp.Headers.RetryAfter?.Delta?.TotalSeconds ?? (code == 429 ? 30 : 5));
                    resp.Dispose();
                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Max(retryAfter, 3), 60)), ct);
                        continue;
                    }
                    throw new BlockedException("HTTP " + code + " — сайт відмовляє");
                }
                if (code >= 500 && attempt < 2) { resp.Dispose(); continue; }
                var landed = resp.RequestMessage?.RequestUri?.ToString() ?? url;
                if (IsLoginPage(landed, code))
                {
                    resp.Dispose();
                    throw new LoginRequiredException("редірект на сторінку входу: " + HostOf(landed));
                }
                return resp;
            }
            catch (BlockedException) { throw; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new NetworkDownException("таймаут " + HostOf(url));
                if (attempt >= 1) throw last;
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException se
                && se.SocketErrorCode is System.Net.Sockets.SocketError.HostNotFound)
            {
                last = new NetworkDownException("DNS не резолвить " + HostOf(url));
                if (attempt >= 1) throw last;
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                last = new NetworkDownException("немає з'єднання з " + HostOf(url));
                if (attempt >= 1) throw last;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
            {
                last = new NetworkDownException("TLS-з'єднання відхилено (" + HostOf(url) + ")");
                if (attempt >= 1) throw last;
            }
        }
        throw last ?? new NetworkDownException("невідома мережева помилка");
    }

    private static bool IsLoginPage(string landed, int code) =>
        code is 401 or 403 && landed.Contains("login", StringComparison.OrdinalIgnoreCase)
        || landed.Contains("/login?", StringComparison.OrdinalIgnoreCase)
        || landed.Contains("/login/index", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholder(string body) =>
        (body.Contains("under maintenance", StringComparison.OrdinalIgnoreCase)
         || body.Contains("getting an upgrade", StringComparison.OrdinalIgnoreCase)
         || body.Contains("we'll be back shortly", StringComparison.OrdinalIgnoreCase))
        && body.Length < 20000;

    // WordPress and OJS pages carry the word "captcha" in their comment/JS markup, so a
    // bare substring test flagged every normal page. Only a short interstitial counts.
    private static bool IsCaptcha(string body) =>
        // Cloudflare's interstitial is recognisable by its title at any size.
        body.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase)
        || (body.Length < 20000 &&
            (body.Contains("captcha", StringComparison.OrdinalIgnoreCase)
             || body.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)));

    // HttpClient.Timeout covers headers only when ResponseHeadersRead is used, so a server
    // that sends headers and then stalls would hang the run forever. Every body read gets
    // its own deadline.
    private static async Task<T> WithDeadline<T>(Func<CancellationToken, Task<T>> work, int seconds,
        CancellationToken ct, string what)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { return await work(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new NetworkDownException(what + " — не віддав тіло за " + seconds + " с");
        }
    }

    private async Task<(bool Ok, string Body)> CurlAsync(string url, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(CurlExe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-sSL", "--compressed", "-m", "90", "-A", UserAgent,
                     "-H", "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                     "-H", "Accept-Language: en-US,en;q=0.9", url })
            psi.ArgumentList.Add(a);

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return (false, "");
        var bodyTask = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var body = await bodyTask;
        return (proc.ExitCode == 0 && body.Length > 0, body);
    }

    public async Task<string> GetTextAsync(string url, int rps, CancellationToken ct)
    {
        if (_curlHosts.Contains(HostOf(url)))
        {
            var (ok, body) = await CurlAsync(url, ct);
            if (ok) return body;
            throw new BlockedException("curl не зміг віддати " + HostOf(url));
        }

        HttpResponseMessage resp;
        try
        {
            resp = await SendAsync(url, rps, ct);
        }
        catch (BlockedException)
        {
            var (ok, body) = await CurlAsync(url, ct);
            if (!ok) throw;
            _curlHosts.Add(HostOf(url));
            return body;
        }
        using var _ = resp;
        var text = await WithDeadline(t => resp.Content.ReadAsStringAsync(t), 120, ct, HostOf(url));
        if (IsPlaceholder(text)) throw new BlockedException("сайт віддає заглушку «Under Maintenance»");
        if (IsCaptcha(text)) throw new BlockedException("капча/челендж");
        return text;
    }

    public async Task<string> GetJsonAsync(string url, int rps, CancellationToken ct)
    {
        using var resp = await SendAsync(url, rps, ct);
        return await WithDeadline(t => resp.Content.ReadAsStringAsync(t), 120, ct, HostOf(url));
    }

    // Streams to <dir>/<name>.pdf, verifies %PDF magic. Returns null when the body is not a PDF.
    public async Task<Saved?> DownloadAsync(string url, string dir, string baseName, int rps, CancellationToken ct)
    {
        if (_curlHosts.Contains(HostOf(url))) return await DownloadViaCurlAsync(url, dir, baseName, ct);
        try
        {
            return await DownloadHttpAsync(url, dir, baseName, rps, ct);
        }
        catch (BlockedException)
        {
            var saved = await DownloadViaCurlAsync(url, dir, baseName, ct);
            if (saved == null) throw;
            _curlHosts.Add(HostOf(url));
            return saved;
        }
    }

    private async Task<Saved?> DownloadViaCurlAsync(string url, string dir, string baseName, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var safe = PathUtil.SanitizeName(baseName, "doc");
        if (safe.Length > 110) safe = safe[..110].TrimEnd('.', ' ');
        var temp = Path.Combine(dir, safe + ".part");
        var final = Path.Combine(dir, safe + ".pdf");
        var psi = new System.Diagnostics.ProcessStartInfo(CurlExe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-sSL", "--compressed", "-m", "600", "-A", UserAgent,
                     "-H", "Accept: application/pdf,*/*", "-o", temp, url })
            psi.ArgumentList.Add(a);
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0 || !File.Exists(temp)) return null;
            var len = new FileInfo(temp).Length;
            if (len < 1024 || len > MaxBytes) return null;
            var head = new byte[5];
            using (var fs = File.OpenRead(temp)) await fs.ReadAsync(head, ct);
            if (Encoding.ASCII.GetString(head) != "%PDF-") return null;
            string sha;
            using (var fs = File.OpenRead(temp))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            File.Move(temp, final, true);
            return new Saved(final, len, sha);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private async Task<Saved?> DownloadHttpAsync(string url, string dir, string baseName, int rps, CancellationToken ct)
    {
        using var resp = await SendAsync(url, rps, ct);
        var code = (int)resp.StatusCode;
        if (code is 404 or 410) throw new NotFoundException("HTTP " + code + " — посилання мертве");
        if (resp.StatusCode != HttpStatusCode.OK) throw new NotFoundException("HTTP " + code + " " + resp.ReasonPhrase);

        var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
        var declared = resp.Content.Headers.ContentLength ?? -1;
        if (declared > MaxBytes) return null;

        Directory.CreateDirectory(dir);
        var safe = PathUtil.SanitizeName(baseName, "doc");
        if (safe.Length > 110) safe = safe[..110].TrimEnd('.', ' ');
        var temp = Path.Combine(dir, safe + ".part");
        var final = Path.Combine(dir, safe + ".pdf");

        try
        {
            string? hash = null;
            bool isPdf;
            long total = 0;
            // Streams must be closed before the temp file is renamed; a move over an
            // open handle fails with a sharing violation on Windows.
            using var fileCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            fileCts.CancelAfter(TimeSpan.FromMinutes(15));
            using (var src = await resp.Content.ReadAsStreamAsync(fileCts.Token))
            using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buf = new byte[1 << 16];
                var magic = new byte[5];
                int magicRead = 0;
                int read;
                while ((read = await src.ReadAsync(buf, fileCts.Token)) > 0)
                {
                    total += read;
                    if (total > MaxBytes) throw new IOException("перевищено ліміт розміру файлу");
                    if (magicRead < 5)
                    {
                        var take = Math.Min(5 - magicRead, read);
                        Buffer.BlockCopy(buf, 0, magic, magicRead, take);
                        magicRead += take;
                    }
                    sha.AppendData(buf, 0, read);
                    await dst.WriteAsync(buf.AsMemory(0, read), fileCts.Token);
                }
                await dst.FlushAsync(ct);
                isPdf = magicRead == 5 && Encoding.ASCII.GetString(magic) == "%PDF-";
                if (isPdf) hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }

            if (!isPdf)
            {
                if (mediaType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                    throw new BlockedException("сервер обіцяв PDF, віддав не PDF");
                // 200 with an HTML page is usually maintenance or a bot wall, not a missing file.
                var head = "";
                try
                {
                    var probe = new byte[Math.Min(8192, (int)total)];
                    using (var fs = File.OpenRead(temp)) await fs.ReadAsync(probe, ct);
                    head = Encoding.UTF8.GetString(probe);
                }
                catch { }
                if (IsPlaceholder(head)) throw new BlockedException("сайт віддає заглушку «Under Maintenance»");
                if (IsCaptcha(head)) throw new BlockedException("капча/челендж замість файлу");
                var title = System.Text.RegularExpressions.Regex.Match(head, @"<title[^>]*>([^<]*)</title>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.Trim();
                return null;
            }
            if (total < 1024) return null;

            File.Move(temp, final, true);
            return new Saved(final, total, hash!);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new NetworkDownException(HostOf(url) + " — файл не докачано за 15 хв");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    // One cheap GET to the host root so a cookie-setting site lets the file through.
    public async Task WarmUpAsync(string url, int rps, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
            using var resp = await SendAsync(u.Scheme + "://" + u.Host + "/", rps, ct);
            await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* best effort */ }
    }

    public void Dispose() => _http.Dispose();
}

public sealed record Saved(string Path, long Size, string Sha);
