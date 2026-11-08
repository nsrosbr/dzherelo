using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

// Звіт прогону (SPEC §21). JSON — джерело, Markdown — рендер того самого RunReport:
// облік один, числа не пишуться вдруге ніде, тому екран і файл не можуть розійтися.
public sealed class RunReport
{
    public string RunId = "";
    public DateTime StartedAt;
    public DateTime FinishedAt;
    public int Checked;
    public int Fetched;
    public int Skipped;
    public Dictionary<string, int> FailedByCode = new();
    public List<string> NewIds = new();
    public List<string> Superseded = new();
    public List<string> SourceFixes = new();
    public int FilesOnDisk;
    public int FilesInCatalog;
    public List<SourceRow> Sources = new();
    public int Discrepancy => Math.Abs(FilesOnDisk - FilesInCatalog);
}

// Один рядок на джерело: таблиця прогону й агрегат рахуються з одного RunReport.
public sealed class SourceRow
{
    public string Id = "", Name = "", Section = "", Status = "", Why = "";
    public int Found, Expected, New, Html, Dupes, Failed;
    public TimeSpan Elapsed;
}

public static class ReportWriter
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // UTC-мітка + 6 hex: два прогони в одну секунду не зіллються в один файл.
    // shortcut: 6 hex ≈ 1/16M на секунду; лічильник у state — коли колізія стане реальною.
    public static string NewRunId(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
        RandomNumberGenerator.GetHexString(6, lowercase: true);

    public static string Write(RunReport r, string stateDir)
    {
        // run_id стає іменем файлу: вихід за межі stateDir тут коштував би найдорожче
        if (string.IsNullOrWhiteSpace(r.RunId) || r.RunId.Contains("..") ||
            r.RunId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("run_id не може бути іменем файлу: " + r.RunId);

        var dir = Path.Combine(stateDir, "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, r.RunId + ".json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(r).ToJsonString(Json), new UTF8Encoding(false));
        File.Move(tmp, path, true);   // напівзірваний звіт — це неправда в журналі обліку
        File.WriteAllText(Path.ChangeExtension(path, ".md"), ToMarkdown(r), new UTF8Encoding(false));
        return path;
    }

    // ключі — як у §21; лічильники плоско, щоб не тримати другу копію RunReport у DTO
    private static JsonObject ToJson(RunReport r)
    {
        var fails = new JsonObject();
        foreach (var kv in r.FailedByCode.OrderByDescending(kv => kv.Value)) fails[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["run_id"] = r.RunId,
            ["started_at"] = r.StartedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["finished_at"] = r.FinishedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["counts"] = new JsonObject
            {
                ["checked"] = r.Checked,
                ["fetched"] = r.Fetched,
                ["skipped"] = r.Skipped,
                ["failed_by_code"] = fails
            },
            ["new_ids"] = Arr(r.NewIds),
            ["superseded"] = Arr(r.Superseded),
            ["source_fixes"] = Arr(r.SourceFixes),
            ["files_on_disk"] = r.FilesOnDisk,
            ["files_in_catalog"] = r.FilesInCatalog,
            ["discrepancy"] = r.Discrepancy
        };
    }

    private static JsonArray Arr(IEnumerable<string> xs)
    {
        var a = new JsonArray();
        foreach (var x in xs) if (!string.IsNullOrWhiteSpace(x)) a.Add(x);
        return a;
    }

    private static string Cell(string s) => (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

    public static string ToMarkdown(RunReport r, string title = "Звіт прогону")
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append("\n\n");

        sb.Append('`').Append(r.RunId).Append('`');
        // дата 0001-01-01 у звіті — гірше за її відсутність: незаповнені поля не друкуємо
        if (r.StartedAt != default && r.FinishedAt != default)
        {
            sb.Append(" · ")
              .Append(r.StartedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .Append('–').Append(r.FinishedAt.ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture))
              .Append(" UTC");
            var span = r.FinishedAt - r.StartedAt;
            if (span.TotalSeconds >= 1)
                sb.Append(" · ").Append((int)span.TotalMinutes).Append(" хв ").Append(span.Seconds).Append(" с");
        }
        sb.Append("\n\n");

        int failed = 0;
        foreach (var v in r.FailedByCode.Values) failed += v;

        sb.Append("Перевірено ").Append(Num(r.Checked)).Append(' ').Append(Sources(r.Checked))
          .Append(" · зібрано ").Append(Num(r.Fetched))
          .Append(" · пропущено ").Append(Num(r.Skipped))
          .Append(" · невдач ").Append(Num(failed));
        if (failed > 0)
            sb.Append(" (").Append(string.Join(", ", r.FailedByCode.OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key + " " + Num(kv.Value)))).Append(')');
        sb.Append('\n');

        if (r.Sources.Count > 0)
        {
            sb.Append("\n| Джерело | Розділ | Стан | Знайдено | Нових | HTML | Дублів | Помилок | Час | Причина |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (var s in r.Sources)
                sb.Append("| ").Append(Cell(s.Name)).Append(" | ").Append(Cell(s.Section)).Append(" | ")
                  .Append(Cell(s.Status)).Append(" | ").Append(s.Found + (s.Expected > 0 ? "/" + s.Expected : ""))
                  .Append(" | ").Append(s.New).Append(" | ").Append(s.Html).Append(" | ").Append(s.Dupes)
                  .Append(" | ").Append(s.Failed).Append(" | ").Append(s.Elapsed.ToString(@"mm\:ss"))
                  .Append(" | ").Append(Cell(s.Why)).Append(" |\n");
            sb.Append('\n');

            var attention = r.Sources.Where(s => s.Status is not ("ok" or "порожньо")).ToList();
            if (attention.Count > 0)
            {
                sb.Append("Потребують уваги (").Append(attention.Count).Append("):\n");
                foreach (var s in attention)
                    sb.Append("- **").Append(s.Name).Append("** — ").Append(s.Status)
                      .Append(": ").Append(s.Why.Length > 0 ? s.Why : "частковий результат").Append('\n');
                sb.Append('\n');
            }
        }

        sb.Append("Файлів на диску ").Append(Num(r.FilesOnDisk))
          .Append(", у каталозі ").Append(Num(r.FilesInCatalog))
          .Append(", розбіжність ").Append(Num(r.Discrepancy)).Append('\n');

        if (r.Discrepancy > 0)
            sb.Append("Розбіжність ").Append(Num(r.Discrepancy)).Append(r.FilesOnDisk > r.FilesInCatalog
                ? " — на диску більше файлів, ніж записів у каталозі: дерево лише проєкція (§5), істина в каталозі, ці файли треба долучити (`--reindex`).\n"
                : " — у каталозі більше записів, ніж файлів на диску: це записи без файлу, `--verify` позначить їх `missing`.\n");

        if (r.NewIds.Count > 0) Section(sb, "Нові документи", r.NewIds, x => "`" + x + "`");
        if (r.Superseded.Count > 0) Section(sb, "Нові редакції", r.Superseded, x => "`" + x + "`");
        if (r.SourceFixes.Count > 0) Section(sb, "Виправлення джерел", r.SourceFixes, x => x);

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, List<string> items, Func<string, string> fmt)
    {
        sb.Append("\n## ").Append(title).Append(" (").Append(Num(items.Count)).Append(")\n");
        foreach (var x in items) sb.Append("- ").Append(fmt(x)).Append('\n');
    }

    private static string Num(int n) => n.ToString("N0", Uk);

    // «1 джерело», «2 джерела», «87 джерел» — інакше число й іменник сваряться
    private static string Sources(int n) =>
        n % 100 is >= 11 and <= 14 ? "джерел"
        : (n % 10) switch { 1 => "джерело", 2 or 3 or 4 => "джерела", _ => "джерел" };

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var checks = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail) => checks.Add((name, ok, detail));

        var now = DateTime.UtcNow;
        var a = NewRunId(now);
        var b = NewRunId(now);
        Check("NewRunId двічі поспіль різні", a != b && a.Length > 0, a + " / " + b);

        var r = new RunReport
        {
            RunId = a,
            StartedAt = now.AddMinutes(-4),
            FinishedAt = now,
            Checked = 87,
            Fetched = 41,
            Skipped = 1204,
            FailedByCode = new() { ["NETWORK"] = 7, ["PAYWALL"] = 5 },
            NewIds = new() { "a3f1c8e09b47d251" },
            FilesOnDisk = 412,
            FilesInCatalog = 411
        };

        var tmp = Path.Combine(Path.GetTempPath(), "dtic-report-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var path = Write(r, tmp);
            var want = "reports/" + a + ".json";
            Check("Write створює reports/<run_id>.json", File.Exists(path) && path.Replace('\\', '/').EndsWith(want), path);

            var o = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var keys = new[] { "run_id", "started_at", "finished_at", "files_on_disk", "files_in_catalog", "discrepancy" };
            var missing = keys.Where(k => o[k] is null).ToList();
            var counts = o["counts"] as JsonObject;
            if (counts is null) missing.Add("counts");
            else foreach (var k in new[] { "checked", "fetched", "skipped", "failed_by_code" })
                if (counts[k] is null) missing.Add("counts." + k);
            var byCode = (o["counts"] as JsonObject)?["failed_by_code"] as JsonObject;
            Check("JSON парситься і містить усі лічильники",
                missing.Count == 0 && byCode?["NETWORK"]?.GetValue<int>() == 7
                && o["new_ids"] is JsonArray ids && ids.Count == 1,
                missing.Count == 0 ? "6 ключів + counts{4} + new_ids" : "немає: " + string.Join(",", missing));

            var md = ToMarkdown(r);
            Check("ToMarkdown містить числа RunReport",
                md.Contains(Num(87)) && md.Contains(Num(1204)) && md.Contains(Num(412)) && md.Contains("NETWORK 7"),
                "перевірено/пропущено/диск/коди");

            Check("порожні списки не потрапляють у Markdown",
                md.Contains("Нові документи (1)") && !md.Contains("Нові редакції") && !md.Contains("Виправлення джерел"),
                "superseded і source_fixes порожні");

            Check("Discrepancy рахується правильно", r.Discrepancy == 1 && md.Contains("розбіжність 1"),
                "412 − 411 = " + r.Discrepancy);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }

        return checks.ToArray();
    }
}
