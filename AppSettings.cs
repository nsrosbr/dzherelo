using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dzherelo;

public sealed class AppSettings
{
    public string? OutputDir { get; set; }
    public int SinceYear { get; set; } = 2022;

    // "auto" (як у Windows) | "light" | "dark" — та сама логіка, що в Valera.
    public string UiTheme { get; set; } = "auto";
    public bool CheckUpdatesOnStart { get; set; } = true;

    // Модель для суджень (тип/тема/мова). Порожньо — судження вимкнені.
    public string Model { get; set; } = "";            // сумісність: = ModelJudge
    // Три ролі. Одна модель не робить усе добре: розмітка, копання в джерелі й каталогізація — різні задачі.
    public string ModelJudge { get; set; } = "";       // що це за документ (тип, тема, мова)
    public string ModelProbe { get; set; } = "";       // доки не докопаємось: чи джерело віддає те, що треба
    public string ModelCatalog { get; set; } = "";     // куди це покласти: рубрики, нові категорії

    public string JudgeModel => string.IsNullOrWhiteSpace(ModelJudge) ? (string.IsNullOrWhiteSpace(Model) ? "deepseek/deepseek-chat" : Model) : ModelJudge;
    public string ProbeModel => string.IsNullOrWhiteSpace(ModelProbe) ? JudgeModel : ModelProbe;
    public string CatalogModel => string.IsNullOrWhiteSpace(ModelCatalog) ? JudgeModel : ModelCatalog;

    // Ключ OpenRouter і логіни проксі живуть у DPAPI (Secrets), а не в settings.json (§18, SPEC v0.1 §44).
    // [JsonIgnore] + читання/запис через Secrets — ключ ніколи не потрапляє у серіалізовані налаштування.
    [JsonIgnore]
    public string? OpenRouterKey
    {
        get => Secrets.Get("openrouter.key");
        set => Secrets.Set("openrouter.key", value ?? "");   // порожній/null → Remove у Secrets
    }

    // Логіни проксі: proxy.<group>.user / proxy.<group>.pass, теж у DPAPI, не в settings.json.
    public static void SetProxyCredentials(string group, string? user, string? password)
    {
        Secrets.Set("proxy." + group + ".user", user ?? "");
        Secrets.Set("proxy." + group + ".pass", password ?? "");
    }

    public static string? GetProxyUser(string group) => Secrets.Get("proxy." + group + ".user");
    public static string? GetProxyPassword(string group) => Secrets.Get("proxy." + group + ".pass");

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo");

    public static string SettingsPath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* corrupted settings -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch { /* non-fatal */ }
    }

    // Тест: ключ не потрапляє в серіалізовані налаштування і переживає серіалізацію через Secrets.
    // Не чіпає реальний ключ користувача: бере і відновлює попереднє значення.
    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var results = new List<(string, bool, string)>();
        void Check(string name, bool ok, string detail = "") => results.Add((name, ok, detail));

        var prev = new AppSettings().OpenRouterKey;
        var testKey = "sk-test-" + Guid.NewGuid().ToString("N");
        var group = "selftest-group";
        var prevUser = GetProxyUser(group);
        var prevPass = GetProxyPassword(group);
        try
        {
            var s = new AppSettings { OpenRouterKey = testKey };
            SetProxyCredentials(group, "usr-" + Guid.NewGuid().ToString("N")[..8], "pwd-" + Guid.NewGuid().ToString("N")[..8]);

            var json = JsonSerializer.Serialize(s);
            Check("сіріалізовані налаштування без ключа",
                !json.Contains(testKey, StringComparison.Ordinal) && !json.Contains("OpenRouter", StringComparison.Ordinal),
                "немає OpenRouter-поля");

            var reload = JsonSerializer.Deserialize<AppSettings>(json)!;
            Check("ключ переживає серіалізацію через Secrets", reload.OpenRouterKey == testKey, "round-trip");

            Check("логін проксі через Secrets", GetProxyUser(group) != null && GetProxyPassword(group) != null);
        }
        catch (Exception ex) { Check("секрети через AppSettings", false, ex.GetType().Name); }
        finally
        {
            new AppSettings { OpenRouterKey = prev };
            SetProxyCredentials(group, prevUser, prevPass);
        }

        return results.ToArray();
    }
}
