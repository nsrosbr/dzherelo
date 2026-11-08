using System.Net;
using System.Text.RegularExpressions;

namespace DticLoader;

public static class CatalogService
{
    public const string HomeUrl = "https://discover.dtic.mil/subject-categories/";

    private static readonly Regex FieldRegex = new(
        @"(\d{2})\.\s*(?:<span[^>]*>)?\s*<a[^>]+href=""([^""]+)""[^>]*>([\s\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodeLineRegex = new(@"^\d{2}(?:/\d{2}){0,2}$", RegexOptions.Compiled);

    public static async Task<CatalogCache> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync(HomeUrl, ct);

        var fields = new List<SubjectField>();
        foreach (Match m in FieldRegex.Matches(html))
        {
            fields.Add(new SubjectField
            {
                Number = m.Groups[1].Value,
                Url = WebUtility.HtmlDecode(m.Groups[2].Value),
                Name = Clean(m.Groups[3].Value),
            });
        }
        if (fields.Count != 25)
            throw new InvalidOperationException($"Очікувалося 25 полів, отримано {fields.Count}");

        var groups = new List<SubjectGroup>();
        foreach (var f in fields)
        {
            ct.ThrowIfCancellationRequested();
            string page;
            try
            {
                page = await http.GetStringAsync(f.Url, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Поле {f.FolderName}: {ex.Message}", ex);
            }

            foreach (var (code, name) in ParseGroups(page))
            {
                groups.Add(new SubjectGroup
                {
                    Code = code,
                    Name = name,
                    FieldNumber = f.Number,
                    FieldName = f.FolderName,
                });
            }
        }

        return new CatalogCache { Updated = DateTimeOffset.Now, Fields = fields, Groups = groups };
    }

    public static List<(string Code, string Name)> ParseGroups(string html)
    {
        var result = new List<(string, string)>();
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        int h1 = html.IndexOf("<h1", StringComparison.OrdinalIgnoreCase);
        if (h1 < 0)
            return result;
        html = html[h1..];
        html = Regex.Replace(html, @"<[^>]+>", "\n");
        html = WebUtility.HtmlDecode(html);

        var lines = html.Split('\n')
            .Select(s => Regex.Replace(s, @"\s+", " ").Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        for (int i = 0; i + 1 < lines.Length; i++)
        {
            // real group headers are a bare code line followed by a name (letter);
            // cross-references are followed by punctuation (",", ".", "-").
            if (CodeLineRegex.IsMatch(lines[i]) && lines[i + 1].Length > 0 && IsGroupName(lines[i + 1]))
                result.Add((lines[i], lines[i + 1]));
        }
        return result;
    }

    public static async Task<bool> IsSearchOfflineAsync(HttpClient http, CancellationToken ct)
    {
        var html = await http.GetStringAsync("https://discover.dtic.mil/results/?q=test", ct);
        return html.Contains("offline", StringComparison.OrdinalIgnoreCase)
            || html.Contains("temporarily offline", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGroupName(string s)
    {
        // First meaningful char decides: letter/digit -> a real group name;
        // punctuation (",", ".", "-", ...) -> a cross-reference continuation.
        // Invisible/format chars (soft hyphen, ZWSP, BOM) are skipped.
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                return true;
            if (c is ',' or '.' or ';' or ':' or '-' or ')')
                return false;
        }
        return false;
    }

    private static string Clean(string innerHtml)
    {
        var text = Regex.Replace(innerHtml, @"<[^>]+>", "");
        return WebUtility.HtmlDecode(text).Trim();
    }
}