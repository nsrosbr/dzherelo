using System.Diagnostics;

namespace Dzherelo;

// Unattended mode: Dzherelo.exe --collect --sources all --since 2022 --out D:\library
public static class CollectCli
{
    public static int Run(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        string? Arg(string name)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        var all = Registry.Load(Arg("--sources-file"));
        if (Has("--list"))
        {
            foreach (var g in all.GroupBy(s => s.Section))
            {
                Console.WriteLine(g.Key + ":");
                foreach (var lib in g.GroupBy(s => s.LibraryHost).OrderBy(x => x.Key))
                {
                    Console.WriteLine("  " + lib.Key + "  (" + lib.Count() + " джерел)");
                    foreach (var s in lib) Console.WriteLine("    " + s.Id.PadRight(24) + s.Kind.PadRight(9) + s.Name);
                }
            }
            return 0;
        }

        var revertId = Arg("--revert-adapter");
        if (revertId != null)
        {
            var dir0 = App.Root(Arg("--out"));
            var st = new AdapterHeal.State(Path.Combine(dir0, "library", ".state"));
            var o = st.Get(revertId);
            if (o is null)
            {
                Console.WriteLine("Для " + revertId + " немає override адаптера — нічого відкочувати");
                return 0;
            }
            var src = all.FirstOrDefault(x => x.Id.Equals(revertId, StringComparison.OrdinalIgnoreCase));
            st.Revert(revertId);
            Console.WriteLine(revertId + ": відкочено kind " + o.Cur + " → " + (src?.Kind ?? o.Prev));
            return 0;
        }

        var probeUrl = Arg("--probe");
        if (probeUrl != null)
        {
            using var pf = new Fetcher(null) { MinIntervalMs = 0 };
            try
            {
                var body = pf.GetTextAsync(probeUrl, 1, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"{body.Length} символів");
                Console.WriteLine("Транспорт: " + (pf.UsesCurl(probeUrl) ? "curl (TLS-відбиток .NET блокують)" : "HttpClient"));
                var head = new string(body.Take(300).ToArray()).Replace("\n", " ").Replace("\r", " ");
                Console.WriteLine("Початок: " + head);
                var pdfs = System.Text.RegularExpressions.Regex.Matches(body,
                    @"href=""([^""]*\.pdf[^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                Console.WriteLine("PDF-посилань на сторінці: " + pdfs.Count);
                foreach (var m in pdfs.Take(5)) Console.WriteLine("  " + m.Groups[1].Value);
            }
            catch (Exception ex) { Console.WriteLine("ПОМИЛКА: " + ex.GetType().Name + " — " + ex.Message); }
            return 0;
        }

        var canonArg = Arg("--canon");
        if (canonArg != null)
        {
            int exit = CanonCliAsync(canonArg, Arg("--out")).GetAwaiter().GetResult();
            return exit;
        }

        var topicsArg = Arg("--topics");
        var hasTopics = args.Any(a => a.Equals("--topics", StringComparison.OrdinalIgnoreCase));
        if (hasTopics)
        {
            // --topics без значення → тека з налаштувань (як у вікні), а не запуск збору
            var dir0 = App.Root(topicsArg == null || topicsArg == "-" || topicsArg.StartsWith("--") ? Arg("--out") : topicsArg);
            Catalog cat;
            try { cat = new Catalog(dir0); }
            catch (InUseException ex) { Console.WriteLine("ВІДМОВА: " + ex.Message); return 3; }
            using var _owner = cat;

            var docs = cat.All();
            Console.WriteLine($"Каталог: {docs.Count} записів у {dir0}");
            Console.WriteLine();
            foreach (var t in Topics.Load(cat.State))
            {
                var cov = Topics.Compute(t, docs);
                var mark = cov.State switch
                {
                    TopicState.Ok => "ок      ",
                    TopicState.Gap => "прогалин",
                    _ => "застаріл"
                };
                Console.WriteLine($"{mark}  {t.Name,-34} {cov.Basis}");
            }
            return 0;
        }

        var reindexArg = Arg("--reindex");
        if (reindexArg != null)
        {
            var dir0 = App.Root(reindexArg == "-" ? null : reindexArg);
            Catalog cat;
            try { cat = new Catalog(dir0); }
            catch (InUseException ex) { Console.WriteLine("ВІДМОВА: " + ex.Message); return 3; }
            using var _owner = cat;
            var (seen, added, skipped) = ReindexAsync(cat, all).GetAwaiter().GetResult();
            cat.ExportCsvAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Файлів у дереві: {seen}; додано в каталог: {added}; уже були: {skipped}");
            Console.WriteLine($"У каталозі всього: {cat.Count}");
            return 0;
        }

        if (Has("--relayout"))
        {
            var dir0 = App.Root(Arg("--out"));
            Catalog st;
            try { st = new Catalog(dir0); }
            catch (InUseException ex) { Console.WriteLine("ВІДМОВА: " + ex.Message); return 3; }
            using var _owner = st;
            Console.WriteLine("Перебудова дерева в " + dir0 + " …");
            var (moved, missing) = new Harvester().RelayoutAsync(st, all, Console.WriteLine).GetAwaiter().GetResult();
            st.ExportCsvAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Переміщено: {moved}; записів без файлу: {missing}");
            return 0;
        }

        var want = Arg("--sources") ?? "all";
        var picked = want.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? all.Where(s => !s.Manual).ToList()
            : all.Where(s => want.Split(',').Any(x => x.Trim().Equals(s.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (picked.Count == 0)
        {
            Console.WriteLine("Не вибрано жодного джерела. Список: --list");
            return 2;
        }

        var root = App.Root(Arg("--out"));
        var since = int.TryParse(Arg("--since"), out var y) ? y : 2022;
        var rps = int.TryParse(Arg("--rps"), out var rpsVal) ? Math.Clamp(rpsVal, 1, 5) : 1;
        var max = int.TryParse(Arg("--max"), out var maxVal) ? Math.Max(1, maxVal) : Adapters.MaxDocs;
        Console.WriteLine($"Куда: {root} | джерел: {picked.Count} | з {since} року | {(Has("--force") ? "усі" : "лише нове")}");

        Catalog store;
        try { store = new Catalog(root); }
        catch (InUseException ex)
        {
            Console.WriteLine("ВІДМОВА: " + ex.Message);
            return 3;
        }
        using var _storeOwner = store;
        using var fetch = new Fetcher(Path.Combine(root, "state")) { MinIntervalMs = Math.Max(250, 1000 / rps) };
        var harvester = new Harvester
        {
            SinceYear = since,
            NewOnly = !Has("--force"),
            MaxDocsPerSource = max,
            SourceBudget = TimeSpan.FromMinutes(int.TryParse(Arg("--budget"), out var bmin) ? bmin : 10),
            OnLog = Console.WriteLine,
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var sw = Stopwatch.StartNew();
        List<SourceResult> results;
        try
        {
            results = harvester.RunAsync(picked, store, fetch, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("зупинено користувачем");
            return 130;
        }

        store.ExportCsvAsync().GetAwaiter().GetResult();
        var report = Harvester.WriteReportAsync(root, harvester.RunId, results, since, store).GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine($"{"джерело",-34}{"стан",-12}{"нових",7}{"html",7}{"дубл",7}{"помилок",9}");
        foreach (var r in results)
            Console.WriteLine($"{r.Name,-34}{r.StatusText,-12}{r.New,7}{r.Html,7}{r.Duplicates,7}{r.Failed,9}");
        Console.WriteLine();
        Console.WriteLine($"Разом нових: {results.Sum(r => r.New)} | html: {results.Sum(r => r.Html)} | "
                          + $"{(results.Sum(r => r.Bytes) / 1024 / 1024)} МБ | {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine("Звіт: " + report);
        return results.Any(r => r.Status is SourceStatus.Blocked or SourceStatus.Failed) ? 1 : 0;
    }

    // Міграція наявної колекції: дерево -> каталог. Файли не рухаються, лише отримують опис.
    // Все, чого не знаємо (url, провенанс), лишається порожнім — вигадувати походження не можна.
    public static async Task<(int Seen, int Added, int Skipped)> ReindexAsync(Catalog cat, List<Source> sources)
    {
        var bySlug = sources.GroupBy(s => Naming.Slug(s.Name, "source"))
                            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        int seen = 0, added = 0, skipped = 0;
        if (!Directory.Exists(cat.Library)) return (0, 0, 0);

        foreach (var file in Directory.EnumerateFiles(cat.Library, "*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + ".state" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = Path.GetRelativePath(cat.Library, file).Replace('\\', '/');
            var parts = rel.Split('/');
            if (parts.Length < 4) continue;                 // <бібліотека>/<тип>/<рік>/<файл>
            seen++;

            string sha;
            await using (var fs = File.OpenRead(file))
                sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (cat.HasSha(sha)) { skipped++; continue; }

            var slug = parts[0];
            var yearText = parts[^2];
            var name = Path.GetFileNameWithoutExtension(file);
            bySlug.TryGetValue(slug, out var src);

            var d = new DocRecord
            {
                Sha = sha,
                Path = rel,
                OrigName = Path.GetFileName(file),
                Title = Naming.CleanTitle(name),
                Source = src?.Name ?? slug,
                Type = Naming.Type(parts[1]),
                TypeSource = "rule",
                Year = int.TryParse(yearText, out var y) ? y : null,
                Size = new FileInfo(file).Length,
                Mime = file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "text/html",
                Magic = MagicOf(file),
                Lang = src?.Lang ?? "",
                Section = src?.Section ?? "",
                ChannelId = "reindex",
                Method = "migration",
                RetrievedAt = File.GetLastWriteTimeUtc(file).ToString("o"),
                Status = "ok", TrustLevel = "L5",
                Topic = Harvester.TopicOf(Naming.CleanTitle(name))
            };
            cat.Add(d);
            added++;
        }
        return (seen, added, skipped);
    }

    private static string MagicOf(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var buf = new byte[4];
            int n = fs.Read(buf, 0, 4);
            return n >= 4 && buf[0] == 0x25 && buf[1] == 0x50 && buf[2] == 0x44 && buf[3] == 0x46 ? "%PDF" : "";
        }
        catch { return ""; }
    }

    // --canon <вхід.html> [--out вихід.pdf]: сторінку без PDF конвертуємо в аркуш канону.
    private static async Task<int> CanonCliAsync(string input, string? outArg)
    {
        if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var titleU = input.Split('/').LastOrDefault(s => s.Length > 4) ?? "page";
            try
            {
                await CanonPdf.RenderUrlToPdfAsync(input, titleU, "Dzherelo", "2026-11-08",
                                                   Path.GetFullPath(outArg ?? (titleU + ".pdf")));
                Console.WriteLine("Канон-PDF: " + Path.GetFullPath(outArg ?? (titleU + ".pdf")));
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("ЗБІЙ: " + ex.Message); return 1; }
        }
        if (!File.Exists(input)) { Console.WriteLine("Немає файлу: " + input); return 2; }
        var html = await File.ReadAllTextAsync(input);
        var title = Path.GetFileNameWithoutExtension(input).Replace(".source", "");
        var outPath = outArg ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, title + ".pdf");
        try
        {
            await CanonPdf.RenderPageToPdfAsync(html, title, "Dzherelo", "2026-11-07", outPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ПОМИЛКА: " + ex.GetType().Name + " — " + ex.Message);
            return 1;
        }
        var size = new FileInfo(outPath).Length;
        Console.WriteLine($"Канон-PDF: {outPath} ({size / 1024} КБ)");
        return 0;
    }
}

