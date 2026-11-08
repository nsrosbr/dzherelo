using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dzherelo;

public sealed record DocRef(string Url, string Title, string Date, bool IsPage, bool Crawled = false);

// What the source itself says about its size. Only OAI/DSpace/sitemap can answer,
// so completeness is a claim the source makes, never one we invent.
public sealed class DiscoveryStats
{
    public int Expected { get; set; } = -1;
    public bool Capped { get; set; }
}

// Six platform families instead of fifty-six scrapers: oai, dspace, wp, sitemap, html, static.
public static class Adapters
{
    public const int MaxDocs = 5000;

    public static IAsyncEnumerable<DocRef> DiscoverAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        Cursors? cursors, CancellationToken ct) =>
        s.Kind switch
        {
            "oai" => OaiAsync(s, f, sinceYear, stats, cursors, ct),
            "dspace" => DspaceAsync(s, f, sinceYear, stats, cursors, ct),
            "wp" => WpAsync(s, f, sinceYear, stats, cursors, ct),
            "sitemap" => SitemapAsync(s, f, sinceYear, stats, ct),
            "static" => StaticAsync(s, f, sinceYear, stats, ct),
            _ => HtmlAsync(s, f, sinceYear, stats, ct),
        };

    // ---------- OAI-PMH (OJS journals + bepress: JCWD, USAWC Press) ----------
    private static async IAsyncEnumerable<DocRef> OaiAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        Cursors? cursors, [EnumeratorCancellation] CancellationToken ct)
    {
        var sep = s.Url.Contains('?') ? "&" : "?";
        var prefix = string.IsNullOrWhiteSpace(s.Prefix) ? "oai_dc" : s.Prefix!;
        // перерваний прогін відновлюється з resumptionToken минулого разу, а не з першої сторінки
        var resumeToken = cursors?.Get(s.Id);
        var next = !string.IsNullOrWhiteSpace(resumeToken)
            ? s.Url + sep + "verb=ListRecords&resumptionToken=" + Uri.EscapeDataString(resumeToken)
            : s.Url + sep + "verb=ListRecords&metadataPrefix=" + prefix
                   + (string.IsNullOrWhiteSpace(s.Set) ? "" : "&set=" + Uri.EscapeDataString(s.Set!));
        for (int page = 0; next != null && page < 400; page++)
        {
            var xml = await f.GetTextAsync(next, s.Rps, ct);
            if (stats.Expected < 0)
            {
                var sz = Regex.Match(xml, "completeListSize=\"(\\d+)\"").Groups[1].Value;
                if (sz.Length > 0) stats.Expected = int.Parse(sz);
            }
            foreach (Match rec in Regex.Matches(xml, @"<record[\s>][\s\S]*?</record>"))
            {
                var body = rec.Value;
                var ids = Regex.Matches(body, @"<dc:identifier>([^<]+)</dc:identifier>")
                    .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Trim()))
                    .Where(x => x.Length > 0).ToList();
                if (ids.Count == 0) continue;

                var pdf = ids.FirstOrDefault(i => i.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
                var target = pdf ?? ids.FirstOrDefault(i => i.StartsWith("http", StringComparison.OrdinalIgnoreCase));
                if (target == null) continue;
                if (pdf == null && !string.IsNullOrWhiteSpace(s.Slug))
                    target = Regex.Replace(target, @"/index\.php/index/", "/index.php/" + s.Slug + "/", RegexOptions.IgnoreCase);

                var title = Clean(Regex.Match(body, @"<dc:title>([\s\S]*?)</dc:title>").Groups[1].Value);
                var date = Regex.Match(body, @"<dc:date>([^<]+)</dc:date>").Groups[1].Value.Trim();
                if (Old(date, sinceYear)) continue;
                yield return new DocRef(target, title, date, pdf == null);
            }

            var tok = Regex.Match(xml, @"<resumptionToken[^>]*>([^<]*)</resumptionToken>");
            // resumptionToken приходить XML-закодованим (&amp;); декодуємо ДО escape, інакше валідне відновлення ламається
            var token = System.Net.WebUtility.HtmlDecode(tok.Groups[1].Value.Trim());
            next = tok.Success && token.Length > 0
                ? s.Url + sep + "verb=ListRecords&resumptionToken=" + Uri.EscapeDataString(token)
                : null;
            cursors?.Set(s.Id, next is null ? null : token);
        }
    }

    // ---------- DSpace 7 REST (KNVBA, VNTU, DPU) ----------
    private static async IAsyncEnumerable<DocRef> DspaceAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        Cursors? cursors, [EnumeratorCancellation] CancellationToken ct)
    {
        var api = s.Url.TrimEnd('/') + "/server/api";
        int totalPages = 1;
        var resume = int.TryParse(cursors?.Get(s.Id), out var p0) ? p0 : 0;
        for (int page = resume; page < Math.Min(totalPages, 2000); page++)
        {
            var q = api + "/discover/search/objects?size=100&page=" + page;
            if (!string.IsNullOrWhiteSpace(s.Query)) q += "&query=" + Uri.EscapeDataString(s.Query!);
            var json = await f.GetJsonAsync(q, s.Rps, ct);

            // Parse the page without touching the network, then resolve PDFs one item at a
            // time so a caller that stops early stops the requests too.
            var items = new List<(string Uuid, string Title, string Date)>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var sr = doc.RootElement.GetProperty("_embedded").GetProperty("searchResult");
                var p = sr.GetProperty("page");
                totalPages = p.TryGetProperty("totalPages", out var tp) ? tp.GetInt32() : 1;
                if (stats.Expected < 0 && p.TryGetProperty("totalElements", out var te)) stats.Expected = te.GetInt32();
                foreach (var obj in sr.GetProperty("_embedded").GetProperty("objects").EnumerateArray())
                {
                    var item = obj.GetProperty("_embedded").GetProperty("indexableObject");
                    var uuid = item.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                    if (uuid == null) continue;
                    var title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var date = MetaDate(item);
                    if (Old(date, sinceYear)) continue;
                    items.Add((uuid, title, date));
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                yield break;
            }

            foreach (var it in items)
            {
                var pdf = await DspacePdfAsync(f, api, it.Uuid, s.Rps, ct);
                if (pdf == null) continue;
                yield return new DocRef(pdf, it.Title, it.Date, false);
            }
            cursors?.Set(s.Id, page + 1 < Math.Min(totalPages, 2000) ? (page + 1).ToString() : null);
        }
    }

    private static string MetaDate(JsonElement item)
    {
        try
        {
            if (!item.TryGetProperty("metadata", out var md)) return "";
            foreach (var key in new[] { "dc.date.issued", "dc.date.created", "dc.date" })
            {
                if (md.TryGetProperty(key, out var arr) && arr.GetArrayLength() > 0)
                    return arr[0].GetProperty("value").GetString() ?? "";
            }
        }
        catch { }
        return "";
    }

    private static async Task<string?> DspacePdfAsync(Fetcher f, string api, string uuid, int rps, CancellationToken ct)
    {
        try
        {
            var bundles = await f.GetJsonAsync(api + "/core/items/" + uuid + "/bundles", rps, ct);
            using var b = JsonDocument.Parse(bundles);
            foreach (var bundle in b.RootElement.GetProperty("_embedded").GetProperty("bundles").EnumerateArray())
            {
                if (!bundle.TryGetProperty("name", out var name) || name.GetString() != "ORIGINAL") continue;
                var href = bundle.GetProperty("_links").GetProperty("bitstreams").GetProperty("href").GetString();
                if (href == null) continue;
                var bits = await f.GetJsonAsync(href, rps, ct);
                using var d = JsonDocument.Parse(bits);
                foreach (var bit in d.RootElement.GetProperty("_embedded").GetProperty("bitstreams").EnumerateArray())
                {
                    var bn = bit.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                    if (!bn.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                    return bit.GetProperty("_links").GetProperty("content").GetProperty("href").GetString();
                }
            }
        }
        catch { }
        return null;
    }

    // ---------- WordPress REST ----------
    private static async IAsyncEnumerable<DocRef> WpAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        Cursors? cursors, [EnumeratorCancellation] CancellationToken ct)
    {
        var api = s.Url.TrimEnd('/');
        var resume = int.TryParse(cursors?.Get(s.Id), out var p1) ? p1 : 1;
        for (int page = resume; page <= 10000; page++)
        {
            var q = api + "/wp-json/wp/v2/posts?per_page=100&page=" + page + "&_fields=link,date,title";
            if (sinceYear > 0) q += "&after=" + sinceYear + "-01-01T00:00:00";
            var json = await f.GetJsonAsync(q, s.Rps, ct);
            if (json.TrimStart().StartsWith("{")) { cursors?.Set(s.Id, null); yield break; }
            var batch = new List<DocRef>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                { cursors?.Set(s.Id, null); yield break; }
                foreach (var post in doc.RootElement.EnumerateArray())
                {
                    var link = post.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                    if (link.Length == 0) continue;
                    var title = post.TryGetProperty("title", out var t) && t.TryGetProperty("rendered", out var r)
                        ? Clean(r.GetString() ?? "") : "";
                    var date = post.TryGetProperty("date", out var d2) ? d2.GetString() ?? "" : "";
                    batch.Add(new DocRef(link, title, date, true));
                }
            }
            catch (JsonException) { cursors?.Set(s.Id, null); yield break; }
            if (batch.Count == 0) { cursors?.Set(s.Id, null); yield break; }
            foreach (var d in batch) yield return d;
            cursors?.Set(s.Id, page < 10000 ? (page + 1).ToString() : null);
        }
    }

    // ---------- sitemap(.xml | index) ----------
    private static async IAsyncEnumerable<DocRef> SitemapAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(s.Url);
        var found = new List<(string Loc, string Lastmod)>();
        var seenMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0 && seenMaps.Count < 100)
        {
            var mapUrl = queue.Dequeue();
            if (!seenMaps.Add(mapUrl)) continue;
            var xml = await f.GetTextAsync(mapUrl, s.Rps, ct);
            if (Environment.GetEnvironmentVariable("DZ_DEBUG") == "1")
                Console.WriteLine($"  · мапа {mapUrl} — {xml.Length} символів, index={xml.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase)}, urls={xml.Split("<url>").Length - 1}, found={found.Count}");
            if (xml.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in Regex.Matches(xml, @"<loc>([^<]+)</loc>"))
                {
                    var map = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                    // Yoast дробить мапи по типах (post-, page-, people-, map-…).
                    // Беремо лише ті, що реально містять документи — інакше десятки зайвих візитів.
                    if (!string.IsNullOrWhiteSpace(s.MapsInclude)
                        && !Regex.IsMatch(map, s.MapsInclude!, RegexOptions.IgnoreCase)) continue;
                    // Тримаємо той самий хост, що й джерело: fetch() у браузері не піде на «сусідній»
                    // understandingwar.org без www (cross-origin) і поверне порожнечу.
                    map = WithHost(map, new Uri(s.Url).Host);
                    queue.Enqueue(map);
                }
                continue;
            }
            foreach (Match m in Regex.Matches(xml, @"<url>[\s\S]*?</url>"))
            {
                var loc = Regex.Match(m.Value, @"<loc>([^<]+)</loc>").Groups[1].Value.Trim();
                if (loc.Length == 0) continue;
                loc = WebUtility.HtmlDecode(loc);
                if (!Match(s, loc)) continue;
                var lm = Regex.Match(m.Value, @"<lastmod>([^<]+)</lastmod>").Groups[1].Value.Trim();
                if (s.SinceFilter && Old(lm, sinceYear)) continue;
                found.Add((loc, lm));
                if (found.Count >= MaxDocs) break;
            }
            if (found.Count >= MaxDocs) break;
        }

        if (found.Count >= MaxDocs) stats.Capped = true;
        else if (stats.Expected < 0) stats.Expected = found.Count;

        foreach (var (loc, lm) in found.OrderByDescending(x => x.Lastmod).Take(MaxDocs))
            yield return new DocRef(loc, "", lm, true);
    }

    // ---------- a listing page (CALL, armypubs FM, gov.uk DCDC) ----------
    private static async IAsyncEnumerable<DocRef> HtmlAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var html = await f.GetTextAsync(s.Url, s.Rps, ct);
        var links = ExtractLinks(html, s.Url);
        var pageTitle = PageTitle(html);

        // Перелік рідко вміщається на одній сторінці: WordPress віддає /page/2/, /page/3/…
        if (s.Pages > 1)
        {
            var baseUrl = s.Url.TrimEnd('/');
            for (int p = 2; p <= s.Pages; p++)
            {
                string more;
                try { more = await f.GetTextAsync(baseUrl + "/page/" + p + "/", s.Rps, ct); }
                catch { break; }
                var extra = ExtractLinks(more, s.Url);
                if (extra.Count == 0) break;
                var fresh = extra.Where(l => !links.Contains(l)).ToList();
                if (fresh.Count == 0) break;
                links.AddRange(fresh);
            }
        }

        var pdfs = links.Where(IsPdfUrl).Where(l => Match(s, l)).Distinct().ToList();
        foreach (var p in pdfs.Take(MaxDocs)) yield return new DocRef(p, "", "", false);

        // A single article page (BNTU handle, Engineer magazine, The Cove) is itself the
        // document; a listing page is not, so it is only yielded when it has no PDFs.
        // Сторінка-список сама собою документом не є: якщо з неї веде багато посилань — це листинг.
        if (pdfs.Count == 0 && links.Count(l => Match(s, l)) <= 3 && !s.Url.EndsWith("/backgrounders"))
            yield return new DocRef(s.Url, pageTitle, "", true);

        if (s.Depth < 1) yield break;

        var pageHost = BareHost(new Uri(s.Url));
        int n = 0;
        foreach (var l in links.Where(l => !IsPdfUrl(l)).Distinct())
        {
            if (!Match(s, l)) continue;
            // file-looking links are download targets, not pages to crawl
            // Compare the real host: a LinkedIn share link carries the site URL in its query
            // and would otherwise pass a substring check.
            if (!Uri.TryCreate(l, UriKind.Absolute, out var linkUri)
                || !BareHost(linkUri).Equals(pageHost, StringComparison.OrdinalIgnoreCase)) continue;
            if (l.Contains("/cdn-cgi/", StringComparison.OrdinalIgnoreCase)) continue; // CF email obfuscation
            if (Uri.TryCreate(l, UriKind.Absolute, out var lu) && lu.AbsolutePath.Trim('/').Length == 0) continue;
            if (l.Contains("/bitstreams/", StringComparison.OrdinalIgnoreCase)
                || l.Contains("/signposting/", StringComparison.OrdinalIgnoreCase)
                || l.Contains("/download", StringComparison.OrdinalIgnoreCase)) continue;
            if (n++ >= s.MaxPages) break;
            // No title from the listing: each page names itself, otherwise every file in a
            // crawl inherits the site's homepage title.
            yield return new DocRef(l, "", "", true, true);
        }
    }

    // ---------- fixed urls (may be several, whitespace separated) ----------
    private static async IAsyncEnumerable<DocRef> StaticAsync(Source s, Fetcher f, int sinceYear, DiscoveryStats stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var raw in s.Url.Split(new[] { ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out _)) continue;
            var isPdf = IsPdfUrl(raw);
            yield return new DocRef(raw, "", "", !isPdf);
        }
        await Task.CompletedTask;
    }

    // ---------- catalogue pages (armypubs): metadata only, files are CAC-gated ----------
    public sealed record CatalogRow(string Number, string Status, string Date, string Title,
        string Proponent, string Url);

    public static List<CatalogRow> ParseCatalog(string html, string baseUrl)
    {
        var rows = new List<CatalogRow>();
        foreach (Match tr in Regex.Matches(html, @"<tr[\s>][\s\S]*?</tr>", RegexOptions.IgnoreCase))
        {
            var cells = Regex.Matches(tr.Value, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase)
                .Select(m => Clean(m.Groups[1].Value)).ToList();
            if (cells.Count < 4) continue;
            var link = Regex.Match(tr.Value, @"href=""([^""]*Details\.aspx\?PUB_ID=\d+)""", RegexOptions.IgnoreCase);
            if (!link.Success) continue;
            var url = Absolutize(link.Groups[1].Value, baseUrl) ?? "";
            // NUMBER | STATUS | DATE | TITLE | PROPONENT
            rows.Add(new CatalogRow(cells[0], cells[1], cells[2], cells[3],
                cells.Count > 4 ? cells[4] : "", url));
        }
        return rows;
    }

    // ---------- helpers ----------
    private static string WithHost(string url, string host)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url;
        var bare = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        var want = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        if (!bare.Equals(want, StringComparison.OrdinalIgnoreCase)) return url;
        return new UriBuilder(u) { Host = host }.Uri.ToString();
    }

    public static bool Match(Source s, string url)
    {
        if (!string.IsNullOrWhiteSpace(s.Include) && !Regex.IsMatch(url, s.Include!, RegexOptions.IgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(s.Exclude) && Regex.IsMatch(url, s.Exclude!, RegexOptions.IgnoreCase)) return false;
        return true;
    }

    public static bool Old(string? date, int sinceYear)
    {
        if (sinceYear <= 0 || string.IsNullOrWhiteSpace(date)) return false;
        var m = Regex.Match(date, @"(19|20)\d{2}");
        return m.Success && int.Parse(m.Value) < sinceYear;
    }

    public static string Clean(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Regex.Replace(html, @"<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    }

    // Sources that expose only pages (RUSI, RAND, CALL) still carry the date in meta or
    // JSON-LD; without it every file lands in "без дати".
    public static string PageDate(string html)
    {
        string[] patterns =
        {
            @"citation_publication_date""\s+content=""(\d{4})(?:[/-](\d{2}))?",
            @"article:published_time""\s+content=""(\d{4})-(\d{2})",
            @"name=""date""\s+content=""(\d{4})-(\d{2})",
            @"""datePublished""\s*:\s*""(\d{4})-(\d{2})",
            @"<time[^>]+datetime=""(\d{4})-(\d{2})",
            @"published(?:Date|_date)""[^>]*content=""(\d{4})-(\d{2})",
        };
        foreach (var p in patterns)
        {
            var m = Regex.Match(html, p, RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[2].Success ? m.Groups[1].Value + "-" + m.Groups[2].Value : m.Groups[1].Value;
        }

        // Last resort for sites that render the date as text (RUSI has no date in markup
        // at all): "12 February 2025" or "Feb 2025".
        // Only the header area: a body paragraph can mention any date, and a wrong year in
        // a folder is worse than no year at all.
        var text = Clean(html);
        if (text.Length > 6000) text = text[..6000];
        var months = new[] { "January", "February", "March", "April", "May", "June",
                             "July", "August", "September", "October", "November", "December" };
        var tm = Regex.Match(text, @"(?:\d{1,2}\s+)?(" + string.Join("|", months) + @")\s+(20[0-3]\d)",
            RegexOptions.IgnoreCase);
        if (tm.Success)
        {
            var idx = Array.FindIndex(months, m => m.Equals(tm.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) + 1;
            return tm.Groups[2].Value + "-" + idx.ToString("00");
        }
        return "";
    }

    public static string PageTitle(string html)
    {
        var t = Regex.Match(html, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase).Groups[1].Value;
        return Clean(t);
    }

    public static string? FirstPdf(string html, string pageUrl)
    {
        var meta = Regex.Match(html, @"citation_pdf_url""\s+content=""([^""]+)""", RegexOptions.IgnoreCase);
        if (meta.Success) return Absolutize(meta.Groups[1].Value, pageUrl);
        var alt = Regex.Match(html, @"<meta[^>]+name=""citation_pdf_url""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
        if (alt.Success) return Absolutize(alt.Groups[1].Value, pageUrl);
        // SWJ and others wrap outbound files in google.com/url?q=… which returns a consent
        // page instead of the file; prefer a direct .pdf link when one exists.
        var all = ExtractLinks(html, pageUrl);
        bool Usable(string l) => IsPdfUrl(l) && !l.Contains("google.com/url", StringComparison.OrdinalIgnoreCase);

        // armypubs lists blank DA forms before the publication itself; the real file lives
        // under /epubs/DR_pubs/, so it wins over anything else on the page.
        return all.FirstOrDefault(l => Usable(l) && l.Contains("/epubs/", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(l => Usable(l) && !l.Contains("MISC_CAC", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(Usable)
               ?? all.FirstOrDefault(l => l.Contains("/bitstreams/", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("/download", StringComparison.OrdinalIgnoreCase));
    }

    // www.example.org і example.org — той самий сайт: ISW посилається на себе без www.
    private static string BareHost(Uri u) =>
        u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;

    public static List<string> ExtractLinks(string html, string baseUrl)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            var abs = Absolutize(m.Groups[1].Value, baseUrl);
            if (abs == null || IsJunk(abs)) continue;
            if (seen.Add(abs)) list.Add(abs);
        }
        return list;
    }

    private static readonly string[] JunkExt = { ".css", ".js", ".ico", ".png", ".jpg", ".jpeg", ".gif",
        ".svg", ".woff", ".woff2", ".ttf", ".eot", ".map", ".ashx" };

    // A cache-buster or tracking query (?v=1, ?cdv=3993) must not hide a PDF: look at the path.
    public static bool IsPdfUrl(string url)
    {
        var path = url;
        var cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) path = path[..cut];
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return true;
        // everyspec and friends put the file name in the query: download.php?spec=X.pdf
        var q = url.IndexOf('?');
        if (q < 0) return false;
        var query = url[(q + 1)..];
        var anchor = query.IndexOf('#');
        if (anchor >= 0) query = query[..anchor];
        return Regex.IsMatch(query, @"\.pdf(&|$)", RegexOptions.IgnoreCase);
    }

    public static bool IsJunk(string url)
    {
        var path = url;
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return JunkExt.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Absolutize(string href, string baseUrl)
    {
        var h = WebUtility.HtmlDecode(href.Trim());
        if (h.Length == 0 || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) || h.StartsWith('#')) return null;
        try { return new Uri(new Uri(baseUrl), h).AbsoluteUri; } catch { return null; }
    }
}
