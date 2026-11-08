using System.Text.Json;

namespace Dzherelo;

// SPEC §19: лікуємо лише жорсткі збої (виняток розбору, зміна схеми сторінки, 5xx на
// відомому шляху). Кандидатний метод спершу йде в staging: проганяється на відомих-добрих
// URL джерела і приймається ЛИШЕ за точним збігом набору URL. Один файл нічого не доводить
// (на FM.aspx перший .pdf був бланком форми). Легальний нуль (NOT_FOUND/NO_PDF/PAYWALL) не
// лікується — він не поломка, а чесна відповідь.
public static class AdapterHeal
{
    // Порядок перебору кандидатів. static/catalog — не кандидати: вони нічого не розбирають.
    private static readonly string[] Candidates = { "html", "sitemap", "wp", "oai", "dspace" };

    // Єдине правило прийняття: та сама множина URL, а не «хоч один збіг». Порожній набір
    // кандидата нічого не доводить — reject.
    public static bool SameUrlSet(IEnumerable<string> a, IEnumerable<string> b)
    {
        var sa = Set(a);
        var sb = Set(b);
        if (sa.Count == 0 || sb.Count == 0) return false;
        return sa.Count == sb.Count && sa.SetEquals(sb);
    }

    private static HashSet<string> Set(IEnumerable<string> xs) =>
        new(xs.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()),
            StringComparer.OrdinalIgnoreCase);   // query НЕ зрізаємо: download?id=1 і ?id=2 — різні документи

    // Кандидати в порядку перебору, без поточного й уже пробуваних.
    public static IEnumerable<string> CandidateKinds(string current, IEnumerable<string>? tried = null)
    {
        var t = new HashSet<string>(tried ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in Candidates)
            if (!c.Equals(current, StringComparison.OrdinalIgnoreCase) && !t.Contains(c))
                yield return c;
    }

    // Відомі-добрі URL джерела — база, з якою звіряють кандидата. Без бази лікувати нічого.
    public static List<string> KnownUrls(Catalog store, Source s) =>
        store.All()
            .Where(d => d.Source == s.Name && d.Status == "ok" && !string.IsNullOrWhiteSpace(d.Url))
            .Select(d => d.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    // Staging: проганяє кандидатів на живому Fetcher і повертає першого, чий набір URL збігся
    // з базою. Повертає (candidate, proof) або (null, 0).
    public static async Task<(string? Kind, int Proof)> HealAsync(Source s, Fetcher f, Catalog store, int sinceYear, CancellationToken ct)
    {
        var baseline = KnownUrls(store, s);
        if (baseline.Count == 0) return (null, 0);

        foreach (var candidate in CandidateKinds(s.Kind))
        {
            ct.ThrowIfCancellationRequested();
            var probe = s.WithKind(candidate);
            var got = new List<string>();
            var stats = new DiscoveryStats();
            try
            {
                await foreach (var d in Adapters.DiscoverAsync(probe, f, sinceYear, stats, null, ct))
                {
                    got.Add(d.Url);
                    // +1: перелишок (кандидат бачить більше, ніж було) не ховається за зрізом
                    if (got.Count > baseline.Count) break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;   // кандидат упав — не він
            }
            if (SameUrlSet(got, baseline)) return (candidate, baseline.Count);
        }
        return (null, 0);
    }

    // state/adapters.json: override kind після самолікування. sources.json лишається базовим —
    // лікування й --revert-adapter живуть тут, тож апдейт застосунку їх не затирає.
    public sealed class State
    {
        private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
        private readonly string _path;
        private readonly Dictionary<string, Override> _map = new(StringComparer.OrdinalIgnoreCase);

        public sealed class Override
        {
            public string Prev { get; set; } = "";
            public string Cur { get; set; } = "";
            public int Proof { get; set; }
            public string At { get; set; } = "";
        }

        public State(string stateDir)
        {
            Directory.CreateDirectory(stateDir);
            _path = Path.Combine(stateDir, "adapters.json");
            if (!File.Exists(_path)) return;
            try
            {
                _map = JsonSerializer.Deserialize<Dictionary<string, Override>>(File.ReadAllText(_path), Opts) ?? new();
            }
            catch { _map = new(); }
        }

        // Старт прогону: джерело йде з вилікуваним kind, старий лишається для звіту й revert.
        public void Apply(IEnumerable<Source> sources)
        {
            foreach (var s in sources)
                if (_map.TryGetValue(s.Id, out var o) && o is { Cur.Length: > 0 })
                    s.Kind = o.Cur;
        }

        public void Switch(string id, string prev, string cur, int proof)
        {
            _map[id] = new Override { Prev = prev, Cur = cur, Proof = proof, At = DateTime.UtcNow.ToString("o") };
            Save();
        }

        public Override? Get(string id) => _map.TryGetValue(id, out var o) ? o : null;

        // --revert-adapter: прибирає override, джерело повертається до базового kind із sources.json.
        public bool Revert(string id) { if (_map.Remove(id)) { Save(); return true; } return false; }

        public int Count => _map.Count;

        private void Save()
        {
            try
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_map, Opts));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch { }
        }
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var r = new List<(string, bool, string)>();
        void Check(string name, bool ok, string detail = "") => r.Add((name, ok, detail));

        // --- правило прийняття: та сама множина URL ---
        Check("збіг набору URL — приймається",
            SameUrlSet(new[] { "https://x/a.pdf", "https://x/b.pdf" },
                       new[] { "https://x/b.pdf", "https://X/a.pdf" }));
        Check("query робить URL іншим (download?id=1 ≠ ?id=2)",
            !SameUrlSet(new[] { "https://x/download?id=1" }, new[] { "https://x/download?id=2" }));
        Check("зайвий query — це вже інший документ, не збіг",
            !SameUrlSet(new[] { "https://x/a.pdf", "https://x/b.pdf" },
                        new[] { "https://x/b.pdf", "https://X/a.pdf?download=1" }));
        Check("один з N — не доказ (підмножина)",
            !SameUrlSet(new[] { "https://x/a.pdf" }, new[] { "https://x/a.pdf", "https://x/b.pdf" }));
        Check("перелишок — не збіг",
            !SameUrlSet(new[] { "https://x/a.pdf", "https://x/b.pdf", "https://x/c.pdf" },
                        new[] { "https://x/a.pdf", "https://x/b.pdf" }));
        Check("порожній кандидат — не доказ",
            !SameUrlSet(Array.Empty<string>(), new[] { "https://x/a.pdf" }));

        // --- порядок кандидатів ---
        var cand = CandidateKinds("wp").ToList();
        Check("кандидати виключають поточний", !cand.Contains("wp"), string.Join(",", cand));
        var tried = CandidateKinds("wp", new[] { "html", "sitemap" }).ToList();
        Check("кандидати виключають пробувані", !tried.Contains("html") && !tried.Contains("sitemap"), string.Join(",", tried));

        // --- override і --revert-adapter ---
        var dir = Path.Combine(Path.GetTempPath(), "dtic-heal-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var st = new State(dir);
            Check("новий стан порожній", st.Count == 0);

            st.Switch("army-pubs", "catalog", "html", 12);
            Check("switch читається одразу", st.Get("army-pubs") is { Cur: "html", Prev: "catalog", Proof: 12 });

            var src = new Source { Id = "army-pubs", Kind = "catalog" };
            new State(dir).Apply(new[] { src });
            Check("override застосовується до реєстру", src.Kind == "html", src.Kind);

            var again = new State(dir);
            Check("override переживає перезапуск", again.Get("army-pubs") is { Cur: "html" });

            again.Revert("army-pubs");
            Check("revert прибирає override", new State(dir).Count == 0);
        }
        catch (Exception ex)
        {
            Check("selfcheck без винятків", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        return r.ToArray();
    }
}
