using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

// Каталог — джерело істини (SPEC §6). Append-only JSONL, один рядок = одна версія документа:
// перемагає max(rev), видалення через tombstone, компакція лишає рівно одну лінію на object_id.
// Дерево на диску — лише проєкція, тому все, що про документ треба знати, лежить тут.
public sealed class InUseException : Exception
{
    public InUseException(string message) : base(message) { }
}

public sealed class DocRecord
{
    public string Url { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Sha { get; set; } = "";           // повний sha256 == object_id
    public long Size { get; set; }
    public string Path { get; set; } = "";          // відносний від кореня бібліотеки
    public string OrigName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Lang { get; set; } = "";
    public string Type { get; set; } = "";
    public string TypeSource { get; set; } = "rule";
    public string Classification { get; set; } = "open";
    public string Topic { get; set; } = "";
    public string Form { get; set; } = "";          // форма документа: наказ, інструкція, чек-лист…
    public string TopicSource { get; set; } = "rule";
    public List<string> SubjectPath { get; set; } = new();
    public string Series { get; set; } = "";
    public string EditionKey { get; set; } = "";
    public string Source { get; set; } = "";
    public string Section { get; set; } = "";
    public string Status { get; set; } = "ok";      // ok | suspect | superseded | missing
    public string Run { get; set; } = "";
    public int? Year { get; set; }
    public int Pages { get; set; }
    public string Mime { get; set; } = "";
    public string Magic { get; set; } = "";
    public string TrustLevel { get; set; } = "";
    public string LicenseClaim { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string Method { get; set; } = "http_get";
    public int HttpStatus { get; set; }
    public string RetrievedAt { get; set; } = "";
    public List<string> Mirrors { get; set; } = new();
    public string RevisionOf { get; set; } = "";
    public int Rev { get; set; }

    internal JsonObject Raw { get; set; } = new();
    public string Key => Sha;
}

public sealed class Catalog : IDisposable
{
    public const int Schema = 3;
    private const int MaxLineBytes = 4096;

    private readonly string _catalogPath;
    private readonly string _metaPath;
    private readonly FileStream _lockStream;
    private FileStream _append;
    private readonly Dictionary<string, DocRecord> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tombstones = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private long _watermark;
    private long _lines;
    private readonly bool _readOnly;

    public string Root { get; }
    public string Library => System.IO.Path.Combine(Root, "library");
    public string State => System.IO.Path.Combine(Library, ".state");
    public bool ReadOnly => _readOnly;
    public int Count { get { lock (_sync) return _byKey.Count; } }

    public Catalog(string root)
    {
        Root = System.IO.Path.GetFullPath(root);
        Directory.CreateDirectory(Library);
        Directory.CreateDirectory(State);
        _catalogPath = System.IO.Path.Combine(State, "catalog.jsonl");
        _metaPath = System.IO.Path.Combine(State, "catalog.meta.json");

        try
        {
            _lockStream = new FileStream(System.IO.Path.Combine(State, ".lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new InUseException("цю теку вже відкрив інший екземпляр: " + State);
        }

        var meta = ReadMeta();
        _readOnly = meta?["schema"] is JsonNode sn && sn.GetValueKind() == JsonValueKind.Number && sn.GetValue<int>() > Schema;

        if (_readOnly)
        {
            _append = new FileStream(_catalogPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
        }
        else
        {
            RepairTail();
            _append = new FileStream(_catalogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        }

        Load();
        if (!_readOnly) { CompactIfNeeded(); WriteMeta(); }
    }

    // ---------- читання ----------

    private void Load()
    {
        _byKey.Clear();
        _tombstones.Clear();
        _lines = 0;
        _watermark = 0;
        if (!File.Exists(_catalogPath)) return;

        using var rd = new StreamReader(new FileStream(_catalogPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite), Encoding.UTF8);
        foreach (var line in EnumerateLines(rd))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            _lines++;
            var o = TryParse(line);
            if (o is null) continue;
            var key = Str(o, "object_id");
            if (string.IsNullOrEmpty(key)) continue;

            if (o["deleted"] is JsonNode del && del.GetValueKind() == JsonValueKind.True)
            {
                _tombstones.Add(key);
                _byKey.Remove(key);
                continue;
            }
            var d = FromJson(o, key);
            _tombstones.Remove(key);
            if (!_byKey.TryGetValue(key, out var cur) || d.Rev >= cur.Rev) _byKey[key] = d;
            if (d.Rev > _watermark) _watermark = d.Rev;
        }
    }

    private static IEnumerable<string> EnumerateLines(StreamReader rd)
    {
        string? l;
        while ((l = rd.ReadLine()) is not null) yield return l;
    }

    private static JsonObject? TryParse(string line)
    {
        try { return JsonNode.Parse(line) as JsonObject; } catch { return null; }
    }

    private static DocRecord FromJson(JsonObject o, string key)
    {
        var d = new DocRecord { Raw = o, Sha = key };
        d.Rev = Int(o, "rev");
        d.Title = Str(o, "title");
        d.OrigName = Str(o, "orig_name");
        d.Lang = Str(o, "lang");
        d.Source = Str(o, "library");
        d.Type = Str(o, "type");
        d.TypeSource = Str(o, "type_source", "rule");
        d.Classification = Str(o, "classification", "open");
        d.Series = Str(o, "series");
        d.EditionKey = Str(o, "edition_key");
        d.Status = Str(o, "status", "ok");
        if (o["relations"] is JsonObject rel && rel["wasRevisionOf"] is JsonNode rv
            && rv.GetValueKind() == JsonValueKind.String)
            d.RevisionOf = rv.GetValue<string>();
        d.Path = Str(o, "path");
        d.TrustLevel = Str(o, "trust_level");
        d.LicenseClaim = Str(o, "license_claim");
        d.Run = Str(o, "run");
        if (o["date"] is JsonNode dt && dt.GetValueKind() == JsonValueKind.String) d.Date = dt.GetValue<string>() ?? "";
        if (o["section"] is JsonNode sec && sec.GetValueKind() == JsonValueKind.String) d.Section = sec.GetValue<string>() ?? "";
        if (o["years"] is JsonNode y && y.GetValueKind() == JsonValueKind.Number) d.Year = y.GetValue<int>();
        if (o["topic"] is JsonArray ta && ta.Count > 0) d.Topic = ta[0]?.ToString() ?? "";
        if (o["form"] is JsonNode fm && fm.GetValueKind() == JsonValueKind.String) d.Form = fm.GetValue<string>() ?? "";
        if (o["subject_path"] is JsonArray sa) foreach (var t in sa) if (t is not null) d.SubjectPath.Add(t.ToString());
        if (o["mirrors"] is JsonArray ma) foreach (var t in ma) if (t is not null) d.Mirrors.Add(t.ToString());
        if (o["checks"] is JsonObject ck)
        {
            d.Size = (long)Num(ck, "size");
            d.Mime = Str(ck, "mime");
            d.Magic = Str(ck, "magic");
            d.Pages = (int)Num(ck, "pages");
        }
        if (o["acquisition"] is JsonArray acq && acq.Count > 0 && acq[0] is JsonObject a)
        {
            d.Url = Str(a, "source_url");
            d.PageUrl = Str(a, "page_url");
            d.HttpStatus = (int)Num(a, "http_status");
            d.RetrievedAt = Str(a, "retrieved_at");
            d.ChannelId = Str(a, "channel_id");
            d.Method = Str(a, "method", "http_get");
        }
        return d;
    }

    private static string Str(JsonObject o, string k, string dflt = "") =>
        o[k] is JsonNode n && n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : dflt;

    private static double Num(JsonObject o, string k) =>
        o[k] is JsonNode n && n.GetValueKind() == JsonValueKind.Number ? n.GetValue<double>() : 0;

    private static int Int(JsonObject o, string k) => (int)Num(o, k);

    // сторінка-джерело теж пропускається: інакше повторний прогін перекачує файл, який уже маємо
    public bool HasUrl(string url)
    {
        var c = Canonical(url);
        lock (_sync) return _byKey.Values.Any(d => Canonical(d.Url) == c || Canonical(d.PageUrl) == c);
    }
    public bool HasSha(string sha) { lock (_sync) return !string.IsNullOrEmpty(sha) && _byKey.ContainsKey(sha); }
    public DocRecord? BySha(string sha) { lock (_sync) return _byKey.TryGetValue(sha, out var d) ? d : null; }
    public IReadOnlyList<DocRecord> All() { lock (_sync) return _byKey.Values.ToList(); }

    public string DirFor(string source, string? type, string? date) =>
        Naming.Dir(Library, source, type, date);

    // Рев'ю 2026-11-11: зрізаний query злипав download?id=1 і ?id=2 в один документ,
    // а ToLowerInvariant() ламав регістрозалежні шляхи. Тепер: query лишаємо (крім трекінгу),
    // нижній регістр — лише схема й хост.
    public static string Canonical(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var raw = url.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return raw;

        var query = u.Query.TrimStart('?');
        if (query.Length > 0)
        {
            static bool Tracking(string kv) =>
                kv.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
                kv.StartsWith("fbclid=", StringComparison.OrdinalIgnoreCase) ||
                kv.StartsWith("gclid=", StringComparison.OrdinalIgnoreCase) ||
                kv.StartsWith("ref=", StringComparison.OrdinalIgnoreCase) ||
                kv.StartsWith("_ga=", StringComparison.OrdinalIgnoreCase);

            var kept = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Where(kv => !Tracking(kv)).ToList();
            query = kept.Count > 0 ? "?" + string.Join("&", kept) : "";
        }

        // регістр лишаємо нечутливим, як було: інакше наявні записи перестануть знаходитись
        return (u.Scheme + "://" + u.Host + u.AbsolutePath.TrimEnd('/') + query).ToLowerInvariant();
    }

    // ---------- запис ----------

    public void Add(DocRecord d)
    {
        if (_readOnly) throw new InvalidOperationException("каталог новіший за цю версію застосунку — запис заборонено");
        lock (_sync)
        {
            var key = d.Key;
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("документ без sha256 не можна записати в каталог");
            d.Rev = _byKey.TryGetValue(key, out var prev) ? prev.Rev + 1 : Math.Max(1, d.Rev);

            var line = ToJson(d).ToJsonString(new JsonSerializerOptions
                { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            if (bytes.Length > MaxLineBytes)
                throw new InvalidOperationException($"запис {bytes.Length} > {MaxLineBytes} байт (object_id={key[..Math.Min(8, key.Length)]})");

            _append.Write(bytes, 0, bytes.Length);
            _append.Flush(true);           // FlushFileBuffers: інакше обрив живлення лишає рваний хвіст
            d.Raw = JsonNode.Parse(line)!.AsObject();
            _byKey[key] = d;
            _tombstones.Remove(key);
            _lines++;
            if (d.Rev > _watermark) _watermark = d.Rev;
            WriteMeta();
        }
    }

    public void Delete(DocRecord d)
    {
        if (_readOnly) return;
        lock (_sync)
        {
            var o = new JsonObject
            {
                ["schema"] = Schema, ["rev"] = d.Rev + 1, ["object_id"] = d.Key, ["deleted"] = true,
                ["deleted_at"] = DateTime.UtcNow.ToString("o")
            };
            var bytes = Encoding.UTF8.GetBytes(o.ToJsonString() + "\n");
            _append.Write(bytes, 0, bytes.Length);
            _append.Flush(true);
            _byKey.Remove(d.Key);
            _tombstones.Add(d.Key);
            _lines++;
            _watermark++;
            WriteMeta();
        }
    }

    private JsonObject ToJson(DocRecord d)
    {
        // read-modify-write: невідомі поля з новішої схеми переживають наш запис
        var o = d.Raw.DeepClone().AsObject();
        o["schema"] = Schema;
        o["rev"] = d.Rev;
        o["object_id"] = d.Key;
        o["id"] = d.Key.Length >= 16 ? d.Key[..16] : d.Key;
        o["title"] = d.Title;
        o["title_key"] = SearchKey(d.Title);
        o["title_latin"] = d.Title;
        o["orig_name"] = d.OrigName;
        o["lang"] = d.Lang;
        o["date"] = d.Date;
        o["section"] = d.Section;
        o["library"] = d.Source;
        o["type"] = d.Type;
        o["type_source"] = d.TypeSource;
        o["classification"] = d.Classification;
        o["years"] = d.Year;
        o["edition_key"] = d.EditionKey;
        o["series"] = d.Series;
        o["status"] = d.Status;
        o["path"] = d.Path.Replace('\\', '/');
        o["topic"] = string.IsNullOrWhiteSpace(d.Topic) ? new JsonArray() : new JsonArray(d.Topic);
        o["form"] = d.Form;
        o["topic_source"] = d.TopicSource;
        o["subject_path"] = ToArr(d.SubjectPath);
        o["mirrors"] = ToArr(d.Mirrors);
        o["trust_level"] = string.IsNullOrEmpty(d.TrustLevel) ? null : JsonValue.Create(d.TrustLevel);
        o["license_claim"] = string.IsNullOrEmpty(d.LicenseClaim) ? null : JsonValue.Create(d.LicenseClaim);
        if (!string.IsNullOrEmpty(d.RevisionOf))
            o["relations"] = new JsonObject { ["wasRevisionOf"] = d.RevisionOf };
        o["checks"] = new JsonObject
        {
            ["sha256"] = d.Key, ["size"] = d.Size, ["mime"] = d.Mime,
            ["magic"] = d.Magic, ["pages"] = d.Pages
        };
        var acq = new JsonObject
        {
            ["channel_id"] = d.ChannelId, ["method"] = d.Method,
            ["source_url"] = d.Url, ["final_url"] = d.Url, ["http_status"] = d.HttpStatus,
            ["retrieved_at"] = string.IsNullOrEmpty(d.RetrievedAt) ? DateTime.UtcNow.ToString("o") : d.RetrievedAt,
            ["retrieved_by"] = "local"
        };
        if (!string.IsNullOrEmpty(d.PageUrl)) acq["page_url"] = d.PageUrl;
        o["acquisition"] = new JsonArray(acq);
        o["run"] = d.Run;
        return o;
    }

    private static JsonArray ToArr(IEnumerable<string> xs)
    {
        var a = new JsonArray();
        foreach (var x in xs) if (!string.IsNullOrWhiteSpace(x)) a.Add(x);
        return a;
    }

    // латинізований ключ пошуку: транслітерація й нормалізація, без словників
    public static string SearchKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var map = new Dictionary<char, string>
        {
            ['а']="a",['б']="b",['в']="v",['г']="h",['ґ']="g",['д']="d",['е']="e",['є']="ie",['ж']="zh",
            ['з']="z",['и']="y",['і']="i",['ї']="i",['й']="i",['к']="k",['л']="l",['м']="m",['н']="n",
            ['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",['ф']="f",['х']="kh",['ц']="ts",
            ['ч']="ch",['ш']="sh",['щ']="shch",['ь']="",['ю']="iu",['я']="ia",
            ['ы']="y",['э']="e",['ё']="e",['ъ']=""
        };
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var tr)) { sb.Append(tr); continue; }
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    // ---------- обслуговування ----------

    private void RepairTail()
    {
        if (!File.Exists(_catalogPath)) return;
        var bytes = File.ReadAllBytes(_catalogPath);
        if (bytes.Length == 0 || bytes[^1] == (byte)'\n') return;
        int last = Array.LastIndexOf(bytes, (byte)'\n');
        long keep = last + 1;
        using (var fs = new FileStream(_catalogPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            fs.SetLength(keep);
            fs.Flush(true);
        }
        Console.WriteLine($"[catalog] ремонт хвоста: відрізано {bytes.Length - keep} байт");
    }

    private void CompactIfNeeded()
    {
        if (_lines == 0) return;
        long live = _byKey.Count + _tombstones.Count;
        if (_lines <= live * 2) return;

        var tmp = _catalogPath + ".tmp";
        using (var w = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var d in _byKey.Values)
            {
                var b = Encoding.UTF8.GetBytes(d.Raw.ToJsonString() + "\n");
                w.Write(b, 0, b.Length);
            }
            // tombstone переживає компакцію: інакше видалений документ воскресне при повторному скані
            foreach (var t in _tombstones)
            {
                var o = new JsonObject { ["schema"] = Schema, ["rev"] = _watermark + 1, ["object_id"] = t, ["deleted"] = true };
                var b = Encoding.UTF8.GetBytes(o.ToJsonString() + "\n");
                w.Write(b, 0, b.Length);
            }
            w.Flush(true);
        }
        _append.Dispose();
        File.Replace(tmp, _catalogPath, null);
        _lines = _byKey.Count + _tombstones.Count;
        _append = new FileStream(_catalogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        Console.WriteLine($"[catalog] компакція: {_lines} ліній, живих {_byKey.Count}");
    }

    private JsonObject? ReadMeta()
    {
        try { return File.Exists(_metaPath) ? JsonNode.Parse(File.ReadAllText(_metaPath)) as JsonObject : null; }
        catch { return null; }
    }

    private void WriteMeta()
    {
        if (_readOnly) return;
        var o = new JsonObject
        {
            ["schema"] = Schema, ["min_reader"] = Schema, ["updated_at"] = DateTime.UtcNow.ToString("o"),
            ["watermark_rev"] = _watermark, ["live_count"] = _byKey.Count, ["line_count"] = _lines
        };
        var tmp = _metaPath + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(), new UTF8Encoding(false));
        if (File.Exists(_metaPath)) File.Replace(tmp, _metaPath, null);
        else File.Move(tmp, _metaPath);
    }

    // Похідний експорт для Excel (SPEC §6, п.4): джерело — каталог, не цей файл.
    public async Task ExportCsvAsync()
    {
        var rows = new List<IEnumerable<string>>
        {
            new[] { "object_id", "id", "title", "library", "type", "year", "classification",
                    "status", "size", "trust_level", "lang", "series", "edition_key", "topic", "path", "url" }
        };
        foreach (var d in All().OrderBy(x => x.Source).ThenBy(x => x.Type).ThenByDescending(x => x.Year))
        {
            rows.Add(new[]
            {
                d.Key, d.Key.Length >= 16 ? d.Key[..16] : d.Key, d.Title, d.Source, d.Type,
                d.Year?.ToString() ?? "", d.Classification, d.Status, d.Size.ToString(),
                d.TrustLevel, d.Lang, d.Series, d.EditionKey, d.Topic, d.Path, d.Url
            });
        }
        await Csv.WriteAsync(Path.Combine(Library, "index.csv"), rows);
    }

    public void Dispose()
    {
        try { _append.Flush(true); _append.Dispose(); } catch { }
        try { _lockStream.Dispose(); } catch { }
    }
}
