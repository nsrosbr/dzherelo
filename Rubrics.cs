
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

// Рубрики беруться з самого джерела: шлях URL і є його структура.
// Каталог дає URL кожного документа — окремий обхід сайту не потрібен.
public static class Rubrics
{
    // сегменти, які нічого не кажуть про рубрику
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "tag", "tags", "author", "authors", "feed", "rss", "index", "index.html",
        "search", "category", "categories", "node", "wp-content", "uploads", "files", "download",
        "downloads", "media", "static", "assets", "content", "dam", "en", "uk", "ru", "www",
        "portals", "sites", "default", "en-us", "node", "docs", "document", "view",
    };

    public static string PathFor(string root) => System.IO.Path.Combine(root, "library", ".state", "rubrics.json");

    /// <summary>Дерево: джерело → рубрика → підрубрика. Рахує документи в кожному вузлі.</summary>
    public static JsonObject Build(Catalog store, int depth = 3)
    {
        var tree = new JsonObject();
        foreach (var d in store.All())
        {
            if (string.IsNullOrWhiteSpace(d.Url)) continue;
            var src = string.IsNullOrWhiteSpace(d.Source) ? "—" : d.Source;
            if (tree[src] is not JsonObject node) { node = new JsonObject(); tree[src] = node; }

            var segs = Segments(d.Url, depth);
            foreach (var s in segs)
            {
                if (node[s] is not JsonObject child) { child = new JsonObject(); node[s] = child; }
                node = child;
            }
            var count = node["_n"]?.GetValue<int>() ?? 0;
            node["_n"] = count + 1;
        }
        return tree;
    }

    public static List<string> Segments(string url, int depth = 3)
    {
        var res = new List<string>();
        if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out var u)) return res;
        var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);          // ім'я файлу — не рубрика

        foreach (var raw in parts)
        {
            var p = Uri.UnescapeDataString(raw).Trim();
            if (p.Length == 0) continue;
            if (Noise.Contains(p)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d{1,4}$")) continue;              // роки й короткі id
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^[0-9a-f]{16,}$")) continue;        // ObjectID з CMS
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^[0-9a-f-]{20,}$")) continue;
            if (p.Contains(".htm") || p.Contains(".pdf") || p.Contains(".xml")) continue;
            res.Add(p.Length > 40 ? p[..40] : p);
            if (res.Count >= depth) break;
        }
        return res;
    }

    public static void Save(string root, JsonObject tree)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, tree.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Текстова розгортка дерева для CLI.</summary>
    public static void Print(JsonObject node, Action<string> log, string indent = "", int maxDepth = 3, int depth = 0)
    {
        if (depth >= maxDepth) return;
        foreach (var kv in node.OrderByDescending(k => Count(k.Value)).Take(60))
        {
            if (kv.Key == "_n" || kv.Value is not JsonObject child) continue;
            log($"{indent}{kv.Key}  ({Count(child)})");
            Print(child, log, indent + "    ", maxDepth, depth + 1);
        }
    }

    private static int Count(JsonNode? n) => n is JsonObject o ? (o["_n"]?.GetValue<int>() ?? 0) : 0;
}
