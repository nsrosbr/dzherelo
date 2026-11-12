using System.Text;

namespace Dzherelo;

// Конвенція дерева (SPEC §5): людина читає очима, машина — з каталогу.
// Правило одне на всі рівні: теки ASCII-slug, тип лише з семи, рік або unknown,
// ім'я файлу = серія + заголовок, обрізане по слову, з __хешем лише при збігу.
public static class Naming
{
    public const int MaxPath = 120;
    public const int MaxName = 90;

    public static readonly string[] Types = { "doctrine", "lessons", "journal", "report", "book", "manual", "other" };

    public static string Type(string? raw)
    {
        var t = (raw ?? "").Trim().ToLowerInvariant();
        return Types.Contains(t) ? t : "other";
    }

    public static string Year(string? dateOrUrl)
    {
        if (string.IsNullOrWhiteSpace(dateOrUrl)) return "unknown";
        var m = System.Text.RegularExpressions.Regex.Match(dateOrUrl, @"(19|20)\d{2}");
        return m.Success ? m.Value : "unknown";
    }

    // кирилиця -> латиниця, решта -> дефіси; один детермінований прохід, без словників
    public static string Slug(string? s, string fallback = "source", int max = 40)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var map = new Dictionary<char, string>
        {
            ['а']="a",['б']="b",['в']="v",['г']="h",['ґ']="g",['д']="d",['е']="e",['є']="ie",['ж']="zh",
            ['з']="z",['и']="y",['і']="i",['ї']="i",['й']="i",['к']="k",['л']="l",['м']="m",['н']="n",
            ['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="kh",['ц']="ts",
            ['ч']="ch",['ш']="sh",['щ']="shch",['ь']="",['ю']="iu",['я']="ia",
            ['ы']="y",['э']="e",['ё']="e",['ъ']="",['і']="i"
        };
        var sb = new StringBuilder();
        foreach (var ch in s.ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var tr)) sb.Append(tr);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var outp = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        if (outp.Length == 0) return fallback;
        if (outp.Length > max) outp = outp[..max].TrimEnd('-');
        return outp;
    }

    // прибирає те, що додав сайт, а не документ: «- GOV.UK», «_ RAND», «Download», рік у кінці
    public static string CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(raw, @"__[0-9a-fA-F]{6,8}(?=\.[a-z0-9]+$|$)", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[|]\s*[^|]{0,40}$", "");          // «Title | Site»
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+[-–—]\s+(GOV\.UK|RAND|RUSI|The Cove)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[_]\s*(RAND|Royal United Services Institute|GOV\.UK)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*[-–—]?\s*(Download|PDF|Full text)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[_\s]+", " ");
        s = s.Replace(':', ' ').Replace('"', ' ').Replace('«', ' ').Replace('»', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '.', '-', '_');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+(19|20)\d{2}([-./ ]\d{1,2}){0,2}$", "");  // дата в кінці — вона в теці року
        return s.Trim(' ', '.', '-', '_');
    }

    public static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = s[..max];
        var sp = cut.LastIndexOf(' ');
        if (sp > max / 2) cut = cut[..sp];
        return cut.TrimEnd(' ', '.', '-', '_');
    }

    // <СЕРІЯ> <Заголовок>; серії немає — файл починається з заголовка
    private static readonly string[] Generic =
        { "file", "download", "index", "document", "view", "content", "full", "article", "pdf" };

    public static string BaseName(string? series, string? title, string? fallback = null)
    {
        var t = CleanTitle(title);
        if (Generic.Contains(t.Trim().ToLowerInvariant())) t = "";   // «file.pdf» — не назва документа
        if (string.IsNullOrWhiteSpace(t)) t = CleanTitle(fallback);
        if (string.IsNullOrWhiteSpace(t)) t = "document";
        var ser = (series ?? "").Trim();
        var name = string.IsNullOrEmpty(ser) ? t : ser + " " + t;
        return Truncate(PathUtil.SanitizeName(name, "document"), MaxName);
    }

    // __<хеш8> додається лише тоді, коли така назва вже зайнята іншим файлом
    public static string Unique(string dir, string baseName, string url)
    {
        var plain = baseName + ".pdf";
        if (!File.Exists(Path.Combine(dir, plain))) return baseName;
        var h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8].ToLowerInvariant();
        return baseName + "__" + h;
    }

    public static string Dir(string libraryRoot, string? library, string? type, string? date)
    {
        var d = Path.Combine(libraryRoot, Slug(library, "source"), Type(type), Year(date));
        if (d.Length > MaxPath) d = d[..MaxPath].TrimEnd('\\', '/');
        return d;
    }

    // Форма документа — це не тема (про що), а жанр (що це за папір).
    // Визначається з назви, без моделі: довідники й чек-листи не мають теми, але мають місце в базі.
    public static string FormOf(string title)
    {
        var t = (title ?? "").ToLowerInvariant();
        if (t.Length == 0) return "";
        if (t.Contains("чек")) return "checklist";
        if (t.Contains("наказ")) return "nakaz";
        if (t.Contains("директив")) return "dyrektyva";
        if (t.Contains("розпорядж")) return "rozporiadzhennia";
        if (t.Contains("інструкц") || t.Contains("инструкц")) return "instruktsiia";
        if (t.Contains("настанова") || t.Contains("настанов")) return "nastanova";
        if (t.Contains("посібник") || t.Contains("пособие")) return "posibnyk";
        if (t.Contains("керівництв") || t.Contains("руководств")) return "kerivnytstvo";
        if (t.Contains("статут")) return "statut";
        if (t.Contains("методичн")) return "metodychni";
        if (t.Contains("програм")) return "prohrama";
        if (t.Contains("план")) return "plan";
        if (t.Contains("перелік") || t.Contains("перечень")) return "perelik";
        if (t.Contains("обов'язк") || t.Contains("обов’язк") || t.Contains("обязанност")) return "oboviazky";
        if (t.Contains("довідник") || t.Contains("справочник")) return "dovidnyk";
        if (t.Contains("донесен") || t.Contains("звіт") || t.Contains("отчет")) return "zvit";
        if (t.Contains("додаток") || t.Contains("приложение")) return "dodatok";
        if (t.Contains("характеристик")) return "tekhnichni-kharakterystyky";
        return "";
    }
}
