
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Dzherelo;

// «Окрема процедура»: питаємо саму бібліотеку, які в неї рубрики.
// OAI-PMH має ListSets (справжня ієрархія репозиторію), WordPress — /wp-json/wp/v2/categories.
// Інші родини лишаються на шляху URL (Rubrics).
public static class Structure
{
    public static async Task<JsonObject?> FetchAsync(Source s, Fetcher f, CancellationToken ct)
    {
        try
        {
            return s.Kind switch
            {
                "oai" or "dspace" => await OaiSetsAsync(s, f, ct),
                "wp" => await WpCategoriesAsync(s, f, ct),
                _ => null,
            };
        }
        catch { return null; }
    }

    private static async Task<JsonObject?> OaiSetsAsync(Source s, Fetcher f, CancellationToken ct)
    {
        var baseUrl = s.Url.TrimEnd('/');
        var url = baseUrl.Contains("verb=") ? baseUrl : baseUrl + "?verb=ListSets";
        var xml = await f.GetTextAsync(url, 1, ct);
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var tree = new JsonObject();
        foreach (var set in doc.Descendants(ns + "set"))
        {
            var spec = set.Element(ns + "setSpec")?.Value?.Trim();
            var name = set.Element(ns + "setName")?.Value?.Trim();
            if (string.IsNullOrEmpty(spec)) continue;

            var node = tree;
            foreach (var part in spec.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (node[part] is not JsonObject child) { child = new JsonObject(); node[part] = child; }
                node = child;
            }
            node["_label"] = string.IsNullOrEmpty(name) ? spec : name;
            node["_n"] = node["_n"]?.GetValue<int>() ?? 0;
        }
        return tree.Count > 0 ? tree : null;
    }

    private static async Task<JsonObject?> WpCategoriesAsync(Source s, Fetcher f, CancellationToken ct)
    {
        var api = s.Url.TrimEnd('/');
        var arr = JsonNode.Parse(await f.GetTextAsync(api + "/wp-json/wp/v2/categories?per_page=100&_fields=id,parent,name,count", 1, ct))?.AsArray();
        if (arr == null || arr.Count == 0) return null;

        var byId = arr.Where(x => x != null).ToDictionary(x => x!["id"]!.GetValue<int>(), x => x!);
        var tree = new JsonObject();

        JsonObject Place(int id, JsonObject into)
        {
            var node = byId[id];
            var name = node["name"]?.GetValue<string>() ?? id.ToString();
            if (into[name] is not JsonObject child) { child = new JsonObject(); into[name] = child; }
            child["_label"] = name;
            child["_n"] = node["count"]?.GetValue<int>() ?? 0;
            return child;
        }

        foreach (var (id, node) in byId)
        {
            var parent = node["parent"]?.GetValue<int>() ?? 0;
            if (parent == 0 || !byId.ContainsKey(parent)) Place(id, tree);
        }
        foreach (var (id, node) in byId)
        {
            var parent = node["parent"]?.GetValue<int>() ?? 0;
            if (parent != 0 && byId.ContainsKey(parent))
            {
                var parentNode = Place(parent, tree);
                Place(id, parentNode);
            }
        }
        return tree.Count > 0 ? tree : null;
    }
}
