using System.Text.RegularExpressions;

namespace Dzherelo;

// SPEC §10: ключ редакції = серія|дата|зміна, без серії — title_key|рік|sha12.
// Дата — лише те, що надруковано: рік без дня не добудовується до 01-01.
public static class Editions
{
    private const int Head = 3000;

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["january"] = 1, ["february"] = 2, ["march"] = 3, ["april"] = 4, ["may"] = 5, ["june"] = 6,
        ["july"] = 7, ["august"] = 8, ["september"] = 9, ["october"] = 10, ["november"] = 11, ["december"] = 12,
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4, ["jun"] = 6, ["jul"] = 7,
        ["aug"] = 8, ["sep"] = 9, ["sept"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
        ["січня"] = 1, ["лютого"] = 2, ["березня"] = 3, ["квітня"] = 4, ["травня"] = 5, ["червня"] = 6,
        ["липня"] = 7, ["серпня"] = 8, ["вересня"] = 9, ["жовтня"] = 10, ["листопада"] = 11, ["грудня"] = 12
    };

    private const string Mil = @"\b(FM|ATP|ADP|AJP|JP|EM|TC|TM|MCWP|MCRP|NWP|NDP|JDP)\s*(\d+(?:[.\-]\d+)*)";
    private const string Dbn = @"ДБН\s*(?:[А-ЯІЇЄҐ]{1,3}\.?\s*)?\d+(?:[.\-]\d+)*";
    private const string Dstu = @"ДСТУ(?:-[А-ЯІЇЄҐ])?\s*(?:[А-ЯІЇЄҐ]\s*\.?\s*){0,3}\d+(?:[.\-]\d+)*";
    private const string Word = @"[A-Za-zА-Яа-яІЇЄҐіїєґ]{3,9}";

    public static string Key(string? series, string? title, int? year, string? sha256, string? pageText = null)
    {
        var text = Clip(pageText);
        var ser = Norm(series);
        if (ser.Length == 0) ser = SeriesFromTitle(title ?? "") ?? "";
        if (ser.Length > 0)
        {
            // дата редакції, якщо надрукована; інакше рік — інакше дві редакції без дати стануть одним ключем
            var stamp = EditionDate(text) ?? (year is > 0 ? year.Value.ToString("0000") : "");
            return ser + "|" + stamp + "|" + (ChangeNumber(text) ?? "");
        }

        var hash = (sha256 ?? "").Trim().ToLowerInvariant();
        if (hash.Length > 12) hash = hash[..12];
        return Catalog.SearchKey(title ?? "") + "|" + (year is > 0 ? year.Value.ToString() : "") + "|" + hash;
    }

    // «FM3-34» і «fm 3-34» — одна серія; крапки в номері лишаються
    private static string Norm(string? series)
    {
        if (string.IsNullOrWhiteSpace(series)) return "";
        var s = Regex.Replace(series.ToUpperInvariant(), @"\s+", " ").Trim();
        s = Regex.Replace(s, @"(?<=[A-ZА-ЯІЇЄҐ])(?=\d)", " ");
        s = Regex.Replace(s, @"\s*\.\s*", ".");
        return Regex.Replace(s, @"\s+", " ").Trim(' ', '.');
    }

    public static string? SeriesFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = Regex.Match(title, Mil, RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(title, Dbn);
        if (!m.Success) m = Regex.Match(title, Dstu);
        return m.Success ? Norm(m.Value) : null;
    }

    public static string? EditionDate(string? pageText)
    {
        var t = Clip(pageText);
        if (t.Length == 0) return null;

        var m = Regex.Match(t, @"\b((?:19|20)\d{2})-(\d{1,2})-(\d{1,2})\b");
        if (m.Success && Make(N(m, 1), N(m, 2), N(m, 3)) is { } iso) return iso;

        m = Regex.Match(t, @"\b(\d{1,2})[./](\d{1,2})[./]((?:19|20)\d{2})\b");
        if (m.Success && Make(N(m, 3), N(m, 2), N(m, 1)) is { } dmy) return dmy;

        foreach (Match x in Regex.Matches(t, @"\b(\d{1,2})\s+(" + Word + @")\s*,?\s+((?:19|20)\d{2})\b"))
            if (Months.TryGetValue(x.Groups[2].Value, out var mo) && Make(N(x, 3), mo, N(x, 1)) is { } d) return d;

        foreach (Match x in Regex.Matches(t, @"\b(" + Word + @")\s+(\d{1,2})\s*,?\s+((?:19|20)\d{2})\b"))
            if (Months.TryGetValue(x.Groups[1].Value, out var mo) && Make(N(x, 3), mo, N(x, 2)) is { } d) return d;

        return null;
    }

    public static string? ChangeNumber(string? pageText)
    {
        var m = Regex.Match(Clip(pageText), @"\b(Change|Зміна|Зм\.?)\s*(?:No\.?|№)?\s*(\d{1,2})\b", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var n = int.Parse(m.Groups[2].Value);
        return m.Groups[1].Value.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "Change " + n : "Зм. " + n;
    }

    public static string? Supersedes(string? pageText)
    {
        var t = Clip(pageText);
        var m = Regex.Match(t, @"(this\s+publication\s+supersedes|supersedes|на\s+заміну|скасовано\s+наказом)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        var start = m.Index;
        while (start > 0 && t[start - 1] is not ('.' or '\n' or '\r')) start--;
        // кінець фрази — крапка перед пробілом: крапки в «В.2.3-4» не ріжуть номер
        var tail = Regex.Match(t[(m.Index + m.Length)..], @"[.\n\r](?=\s|$)");
        var end = tail.Success ? m.Index + m.Length + tail.Index : t.Length;

        var s = Regex.Replace(t[start..end], @"\s+", " ").Trim(' ', '-', ':', ';');
        if (s.Length > 300) s = s[..300].TrimEnd() + "…";
        return s.Length > 0 ? s : null;
    }

    // Дата сильніша за номер зміни; чого не доведено — те не новіше.
    public static bool IsNewer(string candidateKey, string currentKey)
    {
        var (ca, cb) = (Parts(candidateKey), Parts(currentKey));
        if (Full(ca.Date) is { } da && Full(cb.Date) is { } db) return da > db;
        if (Year(ca.Date) is { } ya && Year(cb.Date) is { } yb) return ya > yb;
        return Change(ca.Change) is { } xa && Change(cb.Change) is { } xb && xa > xb;
    }

    // Ревалідація (SPEC §10): групує записи за серією (префікс edition_key до першого «|»),
    // найновішу редакцію лишає ok і веде булаЗамінена wasRevisionOf на безпосередню попередницю,
    // решту (доведено старіших) мітить superseded. Повертає кількість змінених записів.
    public static int Revalidate(Catalog store)
    {
        int changed = 0;
        foreach (var group in store.All()
            .Where(d => !string.IsNullOrWhiteSpace(d.EditionKey) && d.Status != "missing")
            .GroupBy(d => d.EditionKey.Split('|')[0], StringComparer.OrdinalIgnoreCase))
        {
            var docs = group.ToList();
            if (docs.Count < 2) continue;

            DocRecord? newest = null;
            foreach (var d in docs)
                if (newest is null || IsNewer(d.EditionKey, newest.EditionKey)) newest = d;
            if (newest is null) continue;

            foreach (var d in docs)
            {
                if (ReferenceEquals(d, newest) || d.Status == "superseded") continue;
                if (!IsNewer(newest.EditionKey, d.EditionKey)) continue;   // не доведено — не чіпаємо
                d.Status = "superseded";
                store.Add(d);
                changed++;
            }

            DocRecord? predecessor = null;
            foreach (var d in docs)
            {
                if (ReferenceEquals(d, newest)) continue;
                if (!IsNewer(newest.EditionKey, d.EditionKey)) continue;
                if (predecessor is null || IsNewer(d.EditionKey, predecessor.EditionKey)) predecessor = d;
            }
            if (predecessor is not null && newest.RevisionOf != predecessor.Sha)
            {
                newest.RevisionOf = predecessor.Sha;
                store.Add(newest);
                changed++;
            }
        }
        return changed;
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var r = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail = "") => r.Add((name, ok, detail));

        const string page = "FM 3-34\nEngineer Operations\nHeadquarters, Department of the Army\n"
                          + "16 December 2025\nChange 2\nThis publication supersedes FM 3-34, 30 June 2020.";

        var key = Key("FM 3-34", "Engineer Operations", 2025, "ff", page);
        Check("ключ серії з датою і зміною", key == "FM 3-34|2025-12-16|Change 2", key);

        var bare = Key(null, "Naming", 2024, "a3f1c8e09b47d251");
        Check("ключ без серії", bare == "naming|2024|a3f1c8e09b47", bare);

        var series = new[]
        {
            ("FM 3-34 Engineer Operations", "FM 3-34"), ("ATP 3-34.91 Techniques", "ATP 3-34.91"),
            ("AJP 3", "AJP 3"), ("ДБН В.2.3-4 Споруди", "ДБН В.2.3-4"), ("ДСТУ 8768:2018 Вироби", "ДСТУ 8768")
        };
        var bad = series.Where(p => SeriesFromTitle(p.Item1) != p.Item2)
                        .Select(p => p.Item1 + " -> " + (SeriesFromTitle(p.Item1) ?? "null")).ToList();
        Check("серія з заголовка", bad.Count == 0, bad.Count == 0 ? "FM/ATP/AJP/ДБН/ДСТУ" : string.Join("; ", bad));

        var ed = EditionDate("Approved 16 December 2025") + " " + EditionDate("Дата: 16.12.2025");
        Check("дата з титулу", ed == "2025-12-16 2025-12-16", ed);

        var ch = ChangeNumber("Change 2 to FM 3-34") + " " + ChangeNumber("Зм. 1 до ДБН");
        Check("номер зміни", ch == "Change 2 Зм. 1", ch);

        var sup = Supersedes(page) + " | " + Supersedes("На заміну ДБН В.2.3-4:2007 з 01.07.2015.");
        Check("що замінює", sup.Contains("supersedes") && sup.Contains("заміну"), sup);

        Check("новіша дата", IsNewer("FM 3-34|2025-12-16|", "FM 3-34|2022-06-30|"), "2025-12-16 > 2022-06-30");
        Check("без дат — не новіша", !IsNewer("FM 3-34||", "FM 3-34||"), "не доведено");

        var rdir = Path.Combine(Path.GetTempPath(), "dtic-editions-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var cat = new Catalog(rdir))
            {
                var oldSha = new string('a', 64);
                var newSha = new string('b', 64);
                cat.Add(new DocRecord { Sha = oldSha, Title = "FM 3-34", Year = 2020, Series = "FM 3-34", EditionKey = "FM 3-34|2020-06-30|", Status = "ok" });
                cat.Add(new DocRecord { Sha = newSha, Title = "FM 3-34", Year = 2025, Series = "FM 3-34", EditionKey = "FM 3-34|2025-12-16|", Status = "ok" });
                var n = Revalidate(cat);
                var older = cat.BySha(oldSha)!;
                var newer = cat.BySha(newSha)!;
                Check("ревалідація: стара редакція superseded",
                    n == 2 && older.Status == "superseded", "changed=" + n + " status=" + older.Status);
                Check("ревалідація: нова редакція веде wasRevisionOf",
                    newer.RevisionOf == oldSha, newer.RevisionOf.Length > 0 ? "→" + newer.RevisionOf[..8] : "порожньо");

                // §10: ребро чесне в самому каталозі (relations.wasRevisionOf), а не лиш у пам'яті
                string jsonl;
                using (var fs = new FileStream(Path.Combine(rdir, "library", ".state", "catalog.jsonl"),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs)) jsonl = sr.ReadToEnd();
                Check("ревалідація: relations.wasRevisionOf у файлі каталогу",
                    jsonl.Contains("\"wasRevisionOf\"") && jsonl.Contains("superseded"), "jsonl має ребро і статус");
            }
            using (var reopened = new Catalog(rdir))
            {
                Check("ревалідація: ребро й статус переживають перезапуск",
                    reopened.BySha(new string('b', 64))?.RevisionOf == new string('a', 64)
                    && reopened.BySha(new string('a', 64))?.Status == "superseded", "catalog reload");
            }
        }
        catch (Exception ex) { Check("ревалідація: без винятків", false, ex.Message); }
        finally { try { Directory.Delete(rdir, true); } catch { } }

        return r.ToArray();

    }

    private static string Clip(string? pageText)
    {
        if (string.IsNullOrWhiteSpace(pageText)) return "";
        return pageText.Length > Head ? pageText[..Head] : pageText;
    }

    private static int N(Match m, int group) => int.Parse(m.Groups[group].Value);

    private static string? Make(int y, int m, int d)
    {
        if (y is < 1900 or > 2100 || m is < 1 or > 12 || d is < 1 or > 31) return null;
        try { return new DateOnly(y, m, d).ToString("yyyy-MM-dd"); }
        catch { return null; }   // 31 лютого — не дата
    }

    private static (string Date, string Change) Parts(string key)
    {
        var p = (key ?? "").Split('|');
        return (p.Length > 1 ? p[1].Trim() : "", p.Length > 2 ? p[2].Trim() : "");
    }

    private static DateOnly? Full(string s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;

    private static int? Year(string s) =>
        s.Length == 4 && int.TryParse(s, out var y) && y is > 1900 and < 2100 ? y : null;

    private static int? Change(string s)
    {
        var m = Regex.Match(s ?? "", @"(\d{1,2})\s*$");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }
}
