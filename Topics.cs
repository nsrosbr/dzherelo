using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dzherelo;

// Тема = збережений пошук (SPEC §16): тег або слово в назві, обов'язкові типи з семи (§7), поріг свіжості.
public sealed class Topic
{
    public string Id = "";
    public string Name = "";
    public List<string> Keywords = new();
    public List<string> RequiredTypes = new();
    public int FreshnessYears;
}

public enum TopicState { Ok, Gap, Stale }

public sealed record Coverage(int Count, int? NewestYear, TopicState State, string Basis);

public static class Topics
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // кирилиця в topics.json лишається читабельною
    };

    public static List<Topic> Default() => new()
    {
        // manual: переправа без настанови не існує — парк, розрахунок і норми в одному документі.
        new Topic { Id = "perepravy", Name = "Інженерні переправи", FreshnessYears = 10,
            RequiredTypes = new() { "manual" },
            Keywords = new() { "переправ", "понтон", "брід", "міст", "bridge", "bridging", "pontoon", "ferry", "ford", "river crossing" } },
        new Topic { Id = "rozvidka", Name = "Інженерна розвідка", FreshnessYears = 10,
            Keywords = new() { "розвід", "recon", "reconnaissance", "intelligence", "isr", "surveil", "спостереж" } },
        // manual: мінна безпека — це насамперед норми й процедури (EOD/гуманітарне розмінування), а не звіти.
        new Topic { Id = "minna-bezpeka", Name = "Мінна безпека", FreshnessYears = 10,
            RequiredTypes = new() { "manual" },
            Keywords = new() { "мінн", "міна", "міни", "розмінуван", "сапер", "mine", "clearance", "eod", "ied", "uxo", "demining" } },
        new Topic { Id = "fortyfikatsiia", Name = "Фортифікація і загородження", FreshnessYears = 10,
            Keywords = new() { "фортифікац", "укріплен", "бліндаж", "окоп", "загородж", "fortific", "trench", "bunker", "obstacle" } },
        new Topic { Id = "hidrolohiia", Name = "Гідрологія і водопропуск", FreshnessYears = 15,
            Keywords = new() { "гідролог", "водопропуск", "паводок", "дренаж", "hydrolog", "flood", "culvert", "drainage" } },
        // manual: будівельні конструкції тримаються на нормативі (ДБН/настанова), решта — коментарі до нього.
        new Topic { Id = "budivelni-konstruktsii", Name = "Будівельні конструкції", FreshnessYears = 15,
            RequiredTypes = new() { "manual" },
            Keywords = new() { "конструкц", "будівельн", "бетон", "фундамент", "concrete", "structural", "construction", "steel" } },
        new Topic { Id = "taktyka-malykh-hrup", Name = "Тактика малих груп", FreshnessYears = 5,
            Keywords = new() { "малих груп", "відділенн", "взвод", "штурм", "small unit", "squad", "team tactics", "assault" } },
        new Topic { Id = "drony-i-reb", Name = "Дрони і РЕБ", FreshnessYears = 3,
            Keywords = new() { "дрон", "безпілот", "fpv", "uav", "uas", "реб", "electronic warfare", "jamming" } },
        new Topic { Id = "lohistyka", Name = "Логістика", FreshnessYears = 5,
            Keywords = new() { "логіст", "постачан", "забезпеченн", "колон", "logist", "sustain", "supply", "convoy" } },
        new Topic { Id = "medytsyna", Name = "Медицина", FreshnessYears = 5,
            Keywords = new() { "медич", "поранен", "евакуац", "medic", "casualty", "triage", "medical", "first aid" } },
    };

    public static List<Topic> Load(string stateDir)
    {
        var path = Path.Combine(stateDir, "topics.json");
        if (!File.Exists(path))
        {
            var seeded = Default();
            Save(stateDir, seeded);
            return seeded;
        }
        try
        {
            var list = JsonSerializer.Deserialize<List<Topic>>(File.ReadAllText(path), Json);
            if (list is { Count: > 0 }) return list;
        }
        catch { /* пошкоджений файл не перезаписуємо — віддаємо типові теми */ }
        return Default();
    }

    public static void Save(string stateDir, IEnumerable<Topic> topics)
    {
        Directory.CreateDirectory(stateDir);
        var path = Path.Combine(stateDir, "topics.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(topics.ToList(), Json));
        File.Move(tmp, path, true);   // крах посеред запису не лишає обрізаного реєстру
    }

    // Тег — це ярлик, тому збіг іде за назвою/ід/словом теми; Title — лише за ключовими словами.
    public static bool Matches(Topic t, DocRecord d)
    {
        if (!string.IsNullOrWhiteSpace(d.Topic) && TagFits(t, d.Topic.Trim())) return true;
        if (string.IsNullOrWhiteSpace(d.Title)) return false;
        return t.Keywords.Any(k => k.Length >= 3 && d.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    // обидві сторони токенізуються: назва теми — фраза («Інженерні переправи»), тег — одне слово
    private static List<string> Tokens(string s) =>
        (s ?? "").Split(new[] { ' ', ',', ';', '/', '\\', '-', '_', '.', '(', ')', ':', '«', '»' },
                        StringSplitOptions.RemoveEmptyEntries)
                 .Select(w => w.Trim().ToLowerInvariant())
                 .Where(w => w.Length >= 3)
                 .ToList();

    private static bool TagFits(Topic t, string tag)
    {
        var tw = Tokens(tag);
        if (tw.Count == 0) return false;
        foreach (var src in new[] { t.Id, t.Name }.Concat(t.Keywords))
            foreach (var k in Tokens(src))
            {
                if (tw.Contains(k)) return true;
                // основа відмінка: «переправ» ↔ «переправи» — порівнюємо пʼять перших літер довгих слів
                if (k.Length >= 6 && tw.Any(w => w.Length >= 6 && w[..5] == k[..5])) return true;
            }
        return false;
    }

    public static Coverage Compute(Topic t, IEnumerable<DocRecord> docs)
    {
        // рахується лише ok і лише з довірою L1..L4: без позначки документ unverified і в покриття не входить
        var counted = docs.Where(d => Matches(t, d)
                                      && string.Equals(d.Status, "ok", StringComparison.OrdinalIgnoreCase)
                                      && Trusted(d.TrustLevel)).ToList();

        var years = counted.Where(d => d.Year.HasValue).Select(d => d.Year!.Value).ToList();
        int? newest = years.Count > 0 ? years.Max() : null;

        var have = counted.Select(d => Naming.Type(d.Type)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = t.RequiredTypes.Where(r => !have.Contains(Naming.Type(r))).ToList();

        var state = counted.Count == 0 || missing.Count > 0 ? TopicState.Gap
            : newest is int y && t.FreshnessYears > 0 && DateTime.Now.Year - y > t.FreshnessYears ? TopicState.Stale
            : TopicState.Ok;

        var docsText = counted.Count + " " + Docs(counted.Count);
        var fresh = newest?.ToString() ?? "—";
        var basis = counted.Count == 0
            ? $"0 документів · обовʼязкових {t.RequiredTypes.Count}: {Req(t)} · найсвіжіший —"
            : missing.Count > 0
                ? $"{missing.Count} із {t.RequiredTypes.Count} обовʼязкових ({string.Join(", ", missing)}) · {docsText} · найсвіжіший {fresh}"
                : state == TopicState.Stale
                    ? $"{docsText} · найсвіжіший {fresh}, поріг {t.FreshnessYears} років"
                    : $"{docsText} · найсвіжіший {fresh}";

        return new Coverage(counted.Count, newest, state, basis);
    }

    private static bool Trusted(string? level) =>
        level?.Trim().ToUpperInvariant() is "L1" or "L2" or "L3" or "L4";

    private static string Req(Topic t) => t.RequiredTypes.Count == 0 ? "немає" : string.Join(", ", t.RequiredTypes);

    private static string Docs(int n)
    {
        if (n % 100 is >= 11 and <= 14) return "документів";
        return (n % 10) switch { 1 => "документ", 2 or 3 or 4 => "документи", _ => "документів" };
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        bool Num(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"\d");
        var year = DateTime.Now.Year;
        var topic = new Topic { Id = "t", Name = "Тестова тема", FreshnessYears = 5, Keywords = new() { "понтон" } };
        var manual = new Topic { Id = "m", Name = "З настановою", FreshnessYears = 5, RequiredTypes = new() { "manual" } };

        var noTrust = Compute(topic, new[] { new DocRecord { Title = "понтон", Status = "ok", Year = year, TrustLevel = "" } });
        var l2 = Compute(topic, new[] { new DocRecord { Title = "понтон", Status = "ok", Year = year, TrustLevel = "L2" } });
        var missing = Compute(manual, new[] { new DocRecord { Title = "x", Status = "ok", Type = "report", Year = year, TrustLevel = "L2" } });
        var stale = Compute(topic, new[] { new DocRecord { Title = "понтон", Status = "ok", Year = year - 7, TrustLevel = "L1" } });
        var empty = Compute(topic, Array.Empty<DocRecord>());

        var bridge = new Topic { Id = "perepravy", Name = "Інженерні переправи", Keywords = new() { "понтон" } };
        var byTag = Matches(bridge, new DocRecord { Topic = "Переправи" });
        var byWord = Matches(bridge, new DocRecord { Title = "Понтонний парк" });
        var defaults = Default();
        var badTypes = defaults.SelectMany(t => t.RequiredTypes).Where(r => !Naming.Types.Contains(r)).ToList();

        return new[]
        {
            ("документ без TrustLevel не рахується", noTrust.Count == 0, "count=" + noTrust.Count),
            ("документ L2 рахується", l2.Count == 1, "count=" + l2.Count),
            ("відсутній обовʼязковий тип дає Gap із числом", missing.State == TopicState.Gap && Num(missing.Basis), missing.Basis),
            ("старий рік дає Stale", stale.State == TopicState.Stale && Num(stale.Basis), stale.Basis),
            ("порожня тема = Gap із нулем", empty.State == TopicState.Gap && empty.Basis.Contains('0'), empty.Basis),
            ("Matches ловить тег і слово", byTag && byWord, "tag=" + byTag + " word=" + byWord),
            ("Default: ≥ 8 тем, типи з Naming.Types", defaults.Count >= 8 && badTypes.Count == 0,
                defaults.Count + " тем" + (badTypes.Count > 0 ? ", чужі: " + string.Join(",", badTypes) : "")),
        };
    }
}
