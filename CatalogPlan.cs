
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

// Третя роль: каталогізатор. Отримує СПРАВЖНЮ структуру бібліотек (OAI ListSets, WP categories)
// і будує з неї каталог: що є рубрикою, що підрубрикою, що зливається, що нове.
// Результат — categories.json, який далі тільки застосовується до документів.
public static class CatalogPlan
{
    public static string PathFor(string root) => Path.Combine(root, "library", ".state", "categories.json");

    public static string BuildDigest(string root, int maxLines = 220)
    {
        var sb = new StringBuilder();
        var structPath = Path.Combine(root, "library", ".state", "structure.json");
        if (File.Exists(structPath))
        {
            var st = JsonNode.Parse(File.ReadAllText(structPath)) as JsonObject;
            if (st != null)
                foreach (var src in st)
                {
                    sb.AppendLine("## " + src.Key);
                    if (src.Value is JsonObject o) Walk(o, sb, "  ", 0, 3);
                }
        }

        var rubPath = Path.Combine(root, "library", ".state", "rubrics.json");
        if (File.Exists(rubPath))
        {
            var rb = JsonNode.Parse(File.ReadAllText(rubPath)) as JsonObject;
            if (rb != null)
                foreach (var src in rb)
                {
                    sb.AppendLine("## " + src.Key + " (шлях URL)");
                    if (src.Value is JsonObject o) Walk(o, sb, "  ", 0, 2);
                }
        }

        var lines = sb.ToString().Split('\n');
        return lines.Length <= maxLines ? sb.ToString() : string.Join('\n', lines.Take(maxLines));
    }

    private static void Walk(JsonObject node, StringBuilder sb, string indent, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;
        var items = node.Where(kv => kv.Key != "_n" && kv.Key != "_label" && kv.Value is JsonObject)
                        .OrderByDescending(kv => kv.Value?["_n"]?.GetValue<int>() ?? 0);
        foreach (var kv in items.Take(25))
        {
            var child = kv.Value!.AsObject();
            var label = child["_label"]?.GetValue<string>() ?? kv.Key;
            var n = child["_n"]?.GetValue<int>() ?? 0;
            sb.AppendLine($"{indent}{label}" + (n > 0 ? $" [{n}]" : ""));
            Walk(child, sb, indent + "  ", depth + 1, maxDepth);
        }
    }

    public static async Task<JsonObject?> PlanAsync(string root, string apiKey, string model, CancellationToken ct, Action<string> log)
    {
        var digest = BuildDigest(root);
        if (digest.Trim().Length == 0) { log("немає структури: спершу --structure"); return null; }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["categories"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["id"] = new JsonObject { ["type"] = "string" },
                            ["name"] = new JsonObject { ["type"] = "string" },
                            ["group"] = new JsonObject { ["type"] = "string" },
                            ["from"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                        },
                        ["required"] = new JsonArray("id", "name", "group", "from"),
                        ["additionalProperties"] = false,
                    },
                },
            },
            ["required"] = new JsonArray("categories"),
            ["additionalProperties"] = false,
        };

        var system = "Ти каталогізатор військової бібліотеки. Тобі дано СПРАВЖНЮ структуру джерел (їхні рубрики). " +
                     "Побудуй каталог бібліотеки: 8–25 категорій, згрупованих у 3–6 груп. " +
                     "Не вигадуй категорій, яких не видно у структурі. У полі from переліч ТОЧНІ назви рубрик джерел, " +
                     "які належать цій категорії (можна кілька). id — латиницею, короткий.";

        var answer = await Judge.AskJsonAsync(model, system, digest, schema, ct, 2500);
        if (answer is not JsonObject obj) { log("модель не віддала каталог"); return null; }

        var path = PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var cats = obj["categories"] as JsonArray;
        log($"категорій: {cats?.Count ?? 0} → {path}");
        if (cats != null)
            foreach (var c in cats)
            {
                if (c is not JsonObject o) continue;
                log($"  {o["group"]} / {o["name"]}  ← {string.Join(", ", (o["from"] as JsonArray)?.Select(x => x?.ToString() ?? "") ?? Array.Empty<string>())}");
            }
        return obj;
    }
}
