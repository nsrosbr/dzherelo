
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dzherelo;

// Незалежне рев'ю коду моделлю: один пакет (код + доки) → файл. Повторний запуск не платить двічі.
public static class CodeReview
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    public static int Run(string repoRoot, string model, string outPath, Action<string> log, CancellationToken ct = default)
    {
        if (File.Exists(outPath) && new FileInfo(outPath).Length > 2000)
        {
            log("рев'ю вже є — не платимо вдруге: " + outPath);
            return 0;
        }
        var key = Secrets.Get("openrouter.key");
        if (string.IsNullOrWhiteSpace(key)) { log("немає ключа OpenRouter"); return 2; }

        var sb = new StringBuilder();
        sb.AppendLine("# Код проєкту Dzherelo (повний)");
        foreach (var f in Directory.EnumerateFiles(repoRoot, "*.cs").OrderBy(x => x))
        {
            sb.AppendLine("\n## Файл: " + Path.GetFileName(f));
            sb.AppendLine(File.ReadAllText(f));
        }
        foreach (var f in Directory.EnumerateFiles(repoRoot, "*.md").OrderBy(x => x))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("_")) continue;
            sb.AppendLine("\n## Документ: " + name);
            sb.AppendLine(File.ReadAllText(f));
        }
        var codeText = sb.ToString();
        log($"пакет: {codeText.Length:N0} символів");

        var body = new
        {
            model,
            temperature = 0,
            max_tokens = 6000,
            messages = new object[]
            {
                new { role = "system", content =
                    "Ти суворий технічний рев'юер C#/WinForms. Дивись лише факти в коді. " +
                    "Шукай: (1) реальні помилки логіки, (2) втрату даних і гонки (файли, каталог, паралельність), " +
                    "(3) місця, де застосунок може брехати у звіті (вигаданий успіх), (4) витоки ресурсів і незакриті потоки, " +
                    "(5) крихкі місця з мережею/таймаутами, (6) дублювання й мертвий код. " +
                    "Формат: список знахідок, кожна — 'ФАЙЛ:рядок — проблема — чому це небезпечно — як виправити'. " +
                    "Без загальних порад і без похвали. Якщо місце спірне — познач 'сумнівно'. " +
                    "Відповідай СТИСЛО: без ланцюжка міркувань, лише список знахідок" },
                new { role = "user", content = "Ось повний код:\n\n" + codeText },
            },
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://nsrosbr.github.io/dzherelo");
        req.Headers.TryAddWithoutValidation("X-Title", "Dzherelo review");

        log("запит до " + model + " …");
        using var resp = Http.SendAsync(req, ct).GetAwaiter().GetResult();
        var raw = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
        {
            log("HTTP " + (int)resp.StatusCode + ": " + (raw.Length > 300 ? raw[..300] : raw));
            return 1;
        }
        using var doc = JsonDocument.Parse(raw);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        string text = "";
        if (msg.TryGetProperty("content", out var cEl) && cEl.ValueKind == JsonValueKind.String)
            text = cEl.GetString() ?? "";
        // Kimi/Moonshot віддає «міркування» окремим полем: якщо content порожній — беремо його
        if (string.IsNullOrWhiteSpace(text) && msg.TryGetProperty("reasoning", out var rEl) && rEl.ValueKind == JsonValueKind.String)
            text = rEl.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(text) && msg.TryGetProperty("reasoning_content", out var rcEl) && rcEl.ValueKind == JsonValueKind.String)
            text = rcEl.GetString() ?? "";
        long pin = 0, pout = 0;
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            if (u.TryGetProperty("prompt_tokens", out var a)) pin = a.GetInt64();
            if (u.TryGetProperty("completion_tokens", out var b)) pout = b.GetInt64();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath,
            $"# Рев'ю коду — {model}\n\n_створено {DateTime.Now:yyyy-MM-dd HH:mm} · токенів: вхід {pin:N0}, вихід {pout:N0}_\n\n" + text,
            Encoding.UTF8);
        log($"готово → {outPath} (вхід {pin:N0}, вихід {pout:N0})");
        return 0;
    }
}
