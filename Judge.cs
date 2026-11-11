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

    public static long TokensIn, TokensOut;

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
    public static async Task<Judgement?> ClassifyAsync(string model, string title, string hint, CancellationToken ct, int runs = 3)
    {
        if (!Ready) return null;
        var votes = new List<Judgement>();
        for (int i = 0; i < Math.Max(1, runs); i++)
        {
            var one = await OnceAsync(model, title, hint, ct);
            if (one != null) votes.Add(one);
        }
        if (votes.Count == 0) return null;
        string Top(Func<Judgement, string> f) => votes.GroupBy(f).OrderByDescending(g => g.Count()).First().Key;
        return new Judgement(Top(v => v.Type), Top(v => v.Topic), Top(v => v.Lang),
            votes.Average(v => v.Confidence), model);
    }


    /// <summary>
    /// Пакетний прогін суджень із станом на диску: обрив мережі чи зупинка не коштують уже зробленого.
    /// </summary>
    public static async Task<int> RunAll(Catalog store, string statePath, string model, int limit,
        Action<string> log, Action<int, int>? progress, CancellationToken ct, int parallel = 4, int runs = 3)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(statePath))
            foreach (var line in File.ReadAllLines(statePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var d = JsonDocument.Parse(line);
                    if (d.RootElement.TryGetProperty("ok", out var wasOk) && wasOk.GetBoolean()
                        && d.RootElement.TryGetProperty("sha", out var sha))
                        done.Add(sha.GetString() ?? "");
                }
                catch { }
            }

        var todo = store.All()
            .Where(d => (d.Type is null or "" or "other") || string.IsNullOrWhiteSpace(d.Topic))
            .Where(d => !done.Contains(d.Key))
            .Take(limit <= 0 ? int.MaxValue : limit)
            .ToList();

        log($"до обробки: {todo.Count} (уже зроблено раніше: {done.Count}), паралельно: {parallel}, прогонів на документ: {runs}");

        int ok = 0, failed = 0;
        var stateLock = new object();
        using var stateFile = new StreamWriter(statePath, append: true) { AutoFlush = true };
        var gate = new SemaphoreSlim(Math.Max(1, parallel));

        void Mark(string sha, bool success, int attempts, string why)
        {
            var line = JsonSerializer.Serialize(new { sha, ok = success, model, ts = DateTime.UtcNow.ToString("o"), attempts, why });
            lock (stateLock) stateFile.WriteLine(line);
        }

        var tasks = todo.Select(async (doc, idx) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var name = string.IsNullOrWhiteSpace(doc.Title) ? Path.GetFileNameWithoutExtension(doc.Path) : doc.Title;
                Judgement? j = null; int attempt = 0; string why = "";

                while (attempt < 3 && j == null)
                {
                    attempt++;
                    try { j = await ClassifyAsync(model, name, doc.Path, ct, runs); }
                    catch (Exception ex) { why = ex.Message; }
                    if (j == null && attempt < 3) await Task.Delay(attempt == 1 ? 2000 : 10000, ct);
                }

                if (j != null)
                {
                    doc.Type = j.Type;
                    // гнучка таксономія: вільна мітка → канон, інакше кандидат у файл
                    if (j.Topic is "none" or "" or null) doc.Topic = "";
                    else
                    {
                        var resolved = TopicTaxonomy.Resolve(store.Root, j.Topic);
                        if (resolved == null) { TopicTaxonomy.AddCandidate(store.Root, j.Topic, doc.Key); doc.Topic = ""; }
                        else doc.Topic = resolved;
                    }
                    doc.Lang = j.Lang;
                    lock (stateLock) store.Add(doc);      // каталог уже з власним локом, але тримаємо порядок
                    Mark(doc.Key, true, attempt, "");
                    Interlocked.Increment(ref ok);
                    log($"  ✓ {Short(name)} → {j.Type} / {(doc.Topic.Length == 0 ? "—" : doc.Topic)} / {j.Lang}");
                }
                else
                {
                    Mark(doc.Key, false, attempt, why);
                    Interlocked.Increment(ref failed);
                    log($"  ? {Short(name)} — {why}");
                }
            }
            finally { gate.Release(); }
        });

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { log("зупинено — стан збережено"); return 3; }

        log($"готово: успішно {ok}, невдач {failed} | токенів: вхід {TokensIn:N0}, вихід {TokensOut:N0}");
        return failed > 0 ? 1 : 0;
    }

    private static string Short(string s) => s.Length <= 60 ? s : s[..60] + "…";

    private static async Task<Judgement?> OnceAsync(string model, string title, string hint, CancellationToken ct)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Types.Select(t => (JsonNode)t!).ToArray()) },
                ["topic"] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = 40,
                    ["description"] = "коротка тема українською, 1–3 слова (напр. 'логістика', 'підготовка офіцерів'); 'none' якщо тема не визначається",
                },
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
                                  "topic — коротка тема українською у 1–3 словах (не з жорсткого списку), або none. lang — мова документа.",
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
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pi)) Interlocked.Add(ref TokensIn, pi.GetInt64());
            if (usage.TryGetProperty("completion_tokens", out var co)) Interlocked.Add(ref TokensOut, co.GetInt64());
        }
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
