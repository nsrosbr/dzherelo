using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dzherelo;

// A source is a declaration, not a class. Adding a library = one JSON row.
public sealed class Source
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    // oai | dspace | wp | sitemap | html | static
    public string Kind { get; set; } = "";
    public string Url { get; set; } = "";

    public string? Set { get; set; }
    public string? Query { get; set; }
    public string? Prefix { get; set; }
    // ojs: journal path segment (site-wide OAI emits /index.php/index/... which asks for a login)
    public string? Slug { get; set; }

    public string? Include { get; set; }
    public string? Exclude { get; set; }
    // Only accept a PDF whose URL matches this regex (armypubs: blank forms share the page
    // with the real publication, which always sits under /epubs/DR_pubs/).
    public string? PdfInclude { get; set; }
    // Group 1 of this regex names the publication type the library itself uses
    // (research-papers, monographs, engineer manuals). Drives the disk tree.
    public string? Taxonomy { get; set; }
    public bool SinceFilter { get; set; } = true;
    // Last-resort host->IP pin for sources whose DNS is broken more often than not.
    public string? Resolve { get; set; }
    public int Depth { get; set; }
    public int MaxPages { get; set; }
    // скільки сторінок переліку обійти (WordPress: /page/N/) — інакше видно лише першу
    public int Pages { get; set; }
    // які дочірні мапи sitemap_index брати (regex по адресі мапи), напр. "post-sitemap"
    public string? MapsInclude { get; set; }
    public bool KeepHtml { get; set; }
    public string Lang { get; set; } = "en";
    // honest flags: never report success for these
    public bool Manual { get; set; }
    public string? Note { get; set; }
    public int Rps { get; set; } = 1;

    // Плiткий клон під кандидатний метод (SPEC §19): той самий профіль джерела, інший kind.
    public Source WithKind(string kind) { var c = (Source)MemberwiseClone(); c.Kind = kind; return c; }

    [JsonIgnore] public string Key => Id;
    public override string ToString() => Name;
}

public sealed class SourceList
{
    public List<Source> Sources { get; set; } = new();
}

public static class Registry
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static List<Source> Load(string? overridePath = null)
    {
        var candidates = new List<string>();
        if (overridePath != null) candidates.Add(overridePath);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "sources.json"));

        foreach (var p in candidates)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var list = JsonSerializer.Deserialize<SourceList>(File.ReadAllText(p), Opts);
                if (list?.Sources is { Count: > 0 }) return Normalize(list.Sources);
            }
            catch { /* fall back to the embedded copy */ }
        }

        return LoadEmbedded();
    }

    // The shipped default, regardless of any sources.json next to the exe.
    public static List<Source> LoadEmbedded()
    {
        using var stream = typeof(Registry).Assembly.GetManifestResourceStream("Dzherelo.sources.json")
            ?? throw new InvalidOperationException("вбудований sources.json відсутній");
        var embedded = JsonSerializer.Deserialize<SourceList>(stream, Opts)!;
        return Normalize(embedded.Sources);
    }

    private static List<Source> Normalize(List<Source> list)
    {
        foreach (var s in list)
        {
            s.Url = s.Url.Trim();
            if (s.Rps <= 0) s.Rps = 1;
            if (s.MaxPages <= 0) s.MaxPages = 400;
        }
        return list;
    }
}

