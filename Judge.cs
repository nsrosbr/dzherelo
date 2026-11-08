using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

public sealed record Judgement(string Type, string Topic, string Lang, double Confidence, string By);

// Судження моделі: тип / тема / мова документа. Один виклик = одна класифікація;
// три прогони з temperature 0 і беремо те, що повторилось найчастіше (SPEC §модельні судження).
public static class Judge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static readonly string[] Types = { "doctrine", "lessons", "journal", "report", "book", "manual", "other" };

    public static string[] TopicIds =>
        Topics.Default().Select(t => t.Id).Append("none").ToArray();

    private static string? Key => Secrets.Get("openrouter.key");

    public static bool Ready => !string.IsNullOrWhiteSpace(Key);

    public static async Task<List<string>> ModelsAsync(CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
        if (Ready) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var list = new List<string>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
            if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s) list.Add(s);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>Три прогони, більшість. null — модель не відповіла жодного разу.</summary>
    public static async Task<Judgement?> ClassifyAsync(string model, string title, string hint, CancellationToken ct)
    {
        if (!Ready) return null;
        var votes = new List<Judgement>();
        for (int i = 0; i < 3; i++)
        {
            var one = await OnceAsync(model, title, hint, ct);
            if (one != null) votes.Add(one);
        }
        if (votes.Count == 0) return null;
        string Top(Func<Judgement, string> f) => votes.GroupBy(f).OrderByDescending(g => g.Count()).First().Key;
        return new Judgement(Top(v => v.Type), Top(v => v.Topic), Top(v => v.Lang),
            votes.Average(v => v.Confidence), model);
    }

    private static async Task<Judgement?> OnceAsync(string model, string title, string hint, CancellationToken ct)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Types.Select(t => (JsonNode)t!).ToArray()) },
                ["topic"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(TopicIds.Select(t => (JsonNode)t!).ToArray()) },
                ["lang"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("uk", "ru", "en", "other") },
                ["confidence"] = new JsonObject { ["type"] = "number" },
            },
            ["required"] = new JsonArray("type", "topic", "lang", "confidence"),
            ["additionalProperties"] = false,
        };

        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["max_tokens"] = 400,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = "Ти класифікуєш військові документи. Відповідай ЛИШЕ JSON за схемою. " +
                                  "type: doctrine (статут/доктрина), lessons (уроки війни), journal (журнал/збірник), " +
                                  "report (звіт/дослідження), book, manual (посібник), other. " +
                                  "topic — один id із переліку, або none. lang — мова документа.",
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Назва: " + title + "\nАдреса/файл: " + hint,
                }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "judgement", ["strict"] = true, ["schema"] = schema },
            },
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://nsrosbr.github.io/dzherelo");
        req.Headers.TryAddWithoutValidation("X-Title", "Dzherelo");

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var parsed = JsonDocument.Parse(content);
            var r = parsed.RootElement;
            return new Judgement(
                r.GetProperty("type").GetString() ?? "other",
                r.GetProperty("topic").GetString() ?? "none",
                r.GetProperty("lang").GetString() ?? "other",
                r.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5,
                model);
        }
        catch { return null; }
    }
}
