
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

// Гнучка таксономія: канон живе у topics.json (правиться без перезбирання),
// модель пропонує вільну мітку, нормалізатор зводить її до канону або в кандидати.
public static class TopicTaxonomy
{
    public sealed record Entry(string Id, string Name, string Group, string[] Aliases, int FreshnessYears);

    private static readonly object CandLock = new();
    private static List<Entry>? _cache;
    private static string? _cachePath;

    public static string StateDir(string root) => Path.Combine(root, "library", ".state");
    public static string CandidatesPath(string root) => Path.Combine(StateDir(root), "topics-candidates.jsonl");

    public static IReadOnlyList<Entry> Load(string root)
    {
        if (_cache != null) return _cache;
        var list = Topics.Load(StateDir(root))
            .Select(t => new Entry(t.Id, t.Name, "", t.Aliases.Append(t.Name).Distinct().ToArray(), t.FreshnessYears))
            .ToList();
        _cache = list;
        return list;
    }

    /// <summary>Створити topics.json із вбудованого канону, якщо його ще немає (щоб правити руками).</summary>
    public static void EnsureFile(string root)
    {
        var dir = StateDir(root);
        Directory.CreateDirectory(dir);
        if (File.Exists(Path.Combine(dir, "topics.json"))) return;
        Topics.Save(dir, Topics.Default());
        _cache = null;
    }

    public static void Invalidate() { _cache = null; _cachePath = null; }

    private static string Norm(string s) =>
        string.Join(' ', s.ToLowerInvariant()
            .Replace('’', ' ').Replace('\'', ' ').Replace('-', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Звести вільну мітку до канонічного id. null — це нова тема (кандидат).</summary>
    public static string? Resolve(string root, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var l = Norm(label);
        if (l.Length == 0 || l is "none" or "other" or "інше") return "";

        var canon = Load(root);
        foreach (var t in canon)
        {
            if (Norm(t.Id) == l || Norm(t.Name) == l) return t.Id;
            foreach (var a in t.Aliases)
                if (Norm(a) == l) return t.Id;
        }

        // нечітко: перетин токенів
        var lt = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach (var t in canon)
        {
            foreach (var candidateName in t.Aliases.Append(t.Name))
            {
                var ct = Norm(candidateName).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                if (ct.Count == 0) continue;
                var inter = lt.Intersect(ct).Count();
                var union = lt.Union(ct).Count();
                if (union > 0 && (double)inter / union >= 0.6) return t.Id;
            }
        }
        return null;
    }

    /// <summary>Записати кандидата (нову тему), щоб було видно, що проситься в канон.</summary>
    public static void AddCandidate(string root, string label, string? sha = null)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        var path = CandidatesPath(root);
        var line = JsonSerializer.Serialize(new { sha = sha ?? "", label, ts = DateTime.UtcNow.ToString("o") });
        // 6 потоків пишуть одночасно: без лока другий відкриває файл, який тримає перший
        lock (CandLock)
        {
            try
            {
                using var w = new StreamWriter(path, true, System.Text.Encoding.UTF8);
                w.WriteLine(line);
            }
            catch (IOException) { /* не втрачаємо прогін через один рядок */ }
        }
        return;
    }

    /// <summary>Порахувати кандидатів (агрегат із per-doc рядків).</summary>
    // Слова, які модель видає як «тему», але це ФОРМА документа, а не тема.
    private static readonly string[] FormWords =
    {
        "інструкція", "інструкції", "перелік документів", "перелік", "керівництво", "донесення",
        "розпорядження", "наказ", "директива", "загальне", "різне", "документ", "документи",
        "бойове розпорядження", "функціональні обов'язки", "тактичне завдання", "посібник", "настанова",
    };

    public static bool IsFormWord(string label) =>
        FormWords.Contains(label.Trim().ToLowerInvariant());

    public static List<(string Label, int Count)> Candidates(string root, int min = 1)
    {
        var path = CandidatesPath(root);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return new List<(string, int)>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var d = JsonDocument.Parse(line);
                var k = d.RootElement.GetProperty("label").GetString() ?? "";
                var sha = d.RootElement.TryGetProperty("sha", out var s) ? s.GetString() : null;
                if (k.Length == 0 || string.IsNullOrEmpty(sha)) continue;   // старі агрегатні рядки
                if (IsFormWord(k)) continue;
                counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
            }
            catch { }
        }
        return counts.Where(kv => kv.Value >= min).OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Додати тему в канон (topics.json) і одразу переписати каталог для документів із цією міткою.</summary>
    public static int Promote(string root, string label, string id, string name, Catalog store, Action<string> log)
    {
        var dir = StateDir(root);
        var topics = Topics.Load(dir);
        var existing = topics.FirstOrDefault(x => x.Id == id);
        if (existing == null)
        {
            topics.Add(new Topic { Id = id, Name = name, Aliases = new() { label, name }, FreshnessYears = 10 });
            Topics.Save(dir, topics);
        }
        else if (!existing.Aliases.Any(a => string.Equals(a, label, StringComparison.OrdinalIgnoreCase)))
        {
            // тема вже в каноні — просто додаємо мітку синонімом, щоб далі зводилась автоматично
            existing.Aliases.Add(label);
            Topics.Save(dir, topics);
        }

        // переписати документи з цією міткою в кандидатах — без жодного виклику моделі
        var path = CandidatesPath(root);
        if (!File.Exists(path)) return 0;
        var shas = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var d = JsonDocument.Parse(line);
                var l = d.RootElement.GetProperty("label").GetString() ?? "";
                var sha = d.RootElement.TryGetProperty("sha", out var s) ? s.GetString() : null;
                if (!string.IsNullOrEmpty(sha) && string.Equals(l, label, StringComparison.OrdinalIgnoreCase)) shas.Add(sha!);
            }
            catch { }
        }

        int updated = 0;
        foreach (var sha in shas.Distinct())
        {
            var doc = store.BySha(sha);
            if (doc == null) continue;
            if (string.Equals(doc.Topic, id, StringComparison.OrdinalIgnoreCase)) continue;
            doc.Topic = id;
            store.Add(doc);
            updated++;
        }
        log($"тему «{label}» → {id}: оновлено документів {updated}");
        return updated;
    }
}
