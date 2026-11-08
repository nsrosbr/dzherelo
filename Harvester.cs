using System.Text;

namespace Dzherelo;

public enum SourceStatus { Ok, Partial, Empty, Blocked, Network, Manual, Cancelled, Failed }

public sealed class SourceResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    public int Expected { get; set; } = -1;
    public int Found { get; set; }
    public int New { get; set; }
    public int Html { get; set; }
    public int Skipped { get; set; }
    public int Duplicates { get; set; }
    public int Failed { get; set; }
    public int LoginGated { get; set; }
    public int BlockedDocs { get; set; }
    public List<string> NewDocIds { get; } = new();
    public long Bytes { get; set; }
    public SourceStatus Status { get; set; } = SourceStatus.Ok;
    public string Note { get; set; } = "";
    // first few concrete reasons, so the report says what to fix rather than only a count
    public List<string> Failures { get; } = new();
    public TimeSpan Elapsed { get; set; }

    public string StatusText => Status switch
    {
        SourceStatus.Ok => "ok",
        SourceStatus.Partial => "частково",
        SourceStatus.Empty => "порожньо",
        SourceStatus.Blocked => "заблоковано",
        SourceStatus.Network => "мережа",
        SourceStatus.Manual => "вручну",
        SourceStatus.Cancelled => "зупинено",
        _ => "помилка",
    };
}

public readonly record struct IngestResult(DocRecord? Doc, bool Duplicate, string Error);

public sealed class Harvester
{
    public Action<string>? OnLog { get; set; }
    public Action<int, int>? OnProgress { get; set; }
    public Action<string>? OnSourceState { get; set; }
    public Action<SourceResult>? OnSourceDone { get; set; }
    /// <summary>Жива стрічка документів: джерело, адреса, стан, байти. Для екрана «Документи».</summary>
    public Action<string, string, string, long>? OnDoc { get; set; }
    private Reacquire? _queue;
    private Cursors? _cursors;
    private AdapterHeal.State? _heal;

    public int SinceYear { get; set; } = 2022;
    // A single slow host must not eat the whole night: sit.nuou.org.ua answers one OAI
    // page in ~35 s, so without a budget a 600-record journal never finishes.
    public TimeSpan SourceBudget { get; set; } = TimeSpan.FromMinutes(10);
    public bool NewOnly { get; set; } = true;
    public int MaxDocsPerSource { get; set; } = Adapters.MaxDocs;
    public string RunId { get; set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");

    private void Log(string s) => OnLog?.Invoke(s);
    private readonly HashSet<string> _warmed = new(StringComparer.OrdinalIgnoreCase);

    private async Task<Saved?> DownloadAsync(string url, string dir, string name, Source s, Fetcher f, CancellationToken ct)
    {
        try
        {
            return await f.DownloadAsync(url, dir, name, s.Rps, ct);
        }
        catch (BlockedException)
        {
            var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
            if (!_warmed.Add(host)) throw;
            Log("  ↻ " + host + ": прогрів сесії перед повторною спробою");
            await f.WarmUpAsync(url, s.Rps, ct);
            return await f.DownloadAsync(url, dir, name, s.Rps, ct);
        }
    }

    public async Task<List<SourceResult>> RunAsync(IEnumerable<Source> sources, Catalog store, Fetcher fetch,
        CancellationToken ct)
    {
        var results = new List<SourceResult>();
        try { _queue = new Reacquire(store.State); } catch { _queue = null; }
        try { _cursors = new Cursors(store.State); } catch { _cursors = null; }
        try { _heal = new AdapterHeal.State(store.State); } catch { _heal = null; }

        // Самолікування (SPEC §19): override адаптера з попереднього прогону лишається чинним.
        var srcList = sources.ToList();
        _heal?.Apply(srcList);
        var known = srcList.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);
        if (_queue is not null) await RetryPendingAsync(store, fetch, known, ct);

        foreach (var s in srcList)
        {
            ct.ThrowIfCancellationRequested();
            var r = new SourceResult { Id = s.Id, Name = s.Name, Section = s.Section };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            results.Add(r);

            if (s.Manual)
            {
                r.Status = SourceStatus.Manual;
                r.Note = string.IsNullOrWhiteSpace(s.Note) ? "потрібен вхід або платний доступ" : s.Note!;
                Log("· " + s.Name + ": вручну — " + r.Note);
                OnSourceState?.Invoke(s.Id);
                continue;
            }

            using var srcCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            srcCts.CancelAfter(SourceBudget);
            var sct = srcCts.Token;

            if (!string.IsNullOrWhiteSpace(s.Resolve) && Uri.TryCreate(s.Url, UriKind.Absolute, out var su))
                fetch.Pin(su.Host, s.Resolve!);

            Log("▶ " + s.Name + " — " + s.Kind + " " + s.Url);
            try
            {
                var catalogDocs = new List<DocRef>();
                if (s.Kind == "catalog")
                {
                    // The series page carries the whole catalogue; each row links to a detail
                    // page that holds the real PDF under /epubs/DR_pubs/.
                    var html = await fetch.GetTextAsync(s.Url, s.Rps, sct);
                    var rows = Adapters.ParseCatalog(html, s.Url);
                    var cdir = Path.Combine(store.Root, "catalogues");
                    Directory.CreateDirectory(cdir);
                    var csv = new List<IEnumerable<string>>
                    {
                        new[] { "номер", "статус", "дата", "назва", "розробник", "посилання", "джерело" },
                    };
                    csv.AddRange(rows.Select(x => new[] { x.Number, x.Status, x.Date, x.Title, x.Proponent, x.Url, s.Name }));
                    await Csv.WriteAsync(Path.Combine(cdir, s.Id + ".csv"), csv);
                    await MergeCataloguesAsync(store.Root, cdir);
                    catalogDocs.AddRange(rows.Take(MaxDocsPerSource).Select(x => new DocRef(
                        x.Url, (x.Number + " " + x.Title).Trim(), x.Date, true)));
                    Log("  каталог: " + rows.Count + " записів");
                }

                var docs = new List<DocRef>(catalogDocs);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cd in catalogDocs) seen.Add(Catalog.Canonical(cd.Url));
                var stats = new DiscoveryStats();
                Log("  розбираю список: " + s.Url + " (" + s.Kind + ")");
                using var listCts = CancellationTokenSource.CreateLinkedTokenSource(sct);
                listCts.CancelAfter(TimeSpan.FromMinutes(3));   // сайт, що мовчить, не тримає прогін
                if (s.Kind != "catalog")
                {
                    await foreach (var d in Adapters.DiscoverAsync(s, fetch, SinceYear, stats, _cursors, listCts.Token))
                    {
                        if (!seen.Add(Catalog.Canonical(d.Url))) continue;
                        docs.Add(d);
                        if (docs.Count >= MaxDocsPerSource) { stats.Capped = true; break; }
                    }
                }
                r.Expected = stats.Expected;
                r.Found = docs.Count;
                if (OnDoc != null)
                    foreach (var d in docs) OnDoc(r.Name, d.Url, "знайдено", 0);
                if (stats.Capped) r.Note = "обмежено " + MaxDocsPerSource + " документами";
                Log("  знайдено " + docs.Count + (stats.Expected > 0 ? " з " + stats.Expected : "") + ", обробляю…");

                if (docs.Count == 0)
                {
                    r.Status = SourceStatus.Empty;
                    await TryHealAsync(s, r, store, fetch, sct);
                    Log("  порожньо");
                    OnSourceState?.Invoke(s.Id);
                    continue;
                }

                int done = 0, gatedInRow = 0;
                foreach (var d in docs)
                {
                    sct.ThrowIfCancellationRequested();
                    OnProgress?.Invoke(++done, docs.Count);
                    var before = r.LoginGated;
                    await HandleAsync(s, d, store, fetch, r, sct);
                    gatedInRow = r.LoginGated > before ? gatedInRow + 1 : 0;
                    if (gatedInRow >= 3)
                    {
                        r.Note = "PDF за логіном (" + r.LoginGated + " підряд)";
                        Log("  ⛔ " + r.Note + " — решта джерела пропущена");
                        break;
                    }
                }

                r.Status = DecideStatus(r, stats);
                if (r.LoginGated > 0 && r.New == 0) { r.Status = SourceStatus.Blocked; if (r.Note.Length == 0) r.Note = "PDF за логіном"; }
                if (r.BlockedDocs > 0 && r.New == 0 && r.Html == 0)
                {
                    r.Status = SourceStatus.Blocked;
                    if (r.Note.Length == 0) r.Note = $"усі документи відмовили (403), {r.BlockedDocs} шт.";
                }
                Log("  " + r.StatusText + ": нових " + r.New + ", html " + r.Html + ", дублів " + r.Duplicates
                    + ", пропущено " + r.Skipped + ", помилок " + r.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                r.Status = SourceStatus.Cancelled;
                r.Note = "зупинено користувачем";
                r.Elapsed = sw.Elapsed;
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                r.Status = SourceStatus.Network;
                r.Note = "сайт не віддав список за 3 хвилини";
                Log("  ⚠ " + r.Note);
            }
            catch (OperationCanceledException)
            {
                r.Status = SourceStatus.Partial;
                r.Note = "бюджет часу " + (int)SourceBudget.TotalMinutes + " хв вичерпано";
                Log("  ⏱ " + r.Note);
            }
            catch (BlockedException ex)
            {
                r.Status = SourceStatus.Blocked;
                // не затираємо «обмежено N документами»: обидві причини мають лишитись у звіті
                r.Note = r.Note.Length == 0 ? ex.Message : r.Note + "; " + ex.Message;
                Log("  ⛔ " + ex.Message);
            }
            catch (NetworkDownException ex)
            {
                r.Status = SourceStatus.Network;
                r.Note = ex.Message;
                Log("  ⚠ " + ex.Message);
            }
            catch (Exception ex)
            {
                r.Status = SourceStatus.Failed;
                r.Note = ex.GetType().Name + ": " + ex.Message;
                Log("  ✖ " + r.Note);
                await TryHealAsync(s, r, store, fetch, sct);
            }
            finally
            {
                r.Elapsed = sw.Elapsed;
                OnSourceState?.Invoke(s.Id);
                OnSourceDone?.Invoke(r);
            }
        }
        try
        {
            var revived = Editions.Revalidate(store);
            if (revived > 0) Log("✻ ревалідація редакцій: " + revived + " змiн");
        }
        catch (Exception ex) { Log("  ! ревалідація редакцій: " + ex.Message); }

        return results;
    }

    // Лікує лише жорсткі збої й лише за збігом набору URL (SPEC §19). Легальний нуль
    // (немає бази відомих-добрих URL) не лікується. On success перемикає kind і зберігає
    // override у state/adapters.json; цей прогін лишається Failed, наступний піде по-новому.
    private async Task<bool> TryHealAsync(Source s, SourceResult r, Catalog store, Fetcher fetch, CancellationToken ct)
    {
        if (_heal is null) return false;
        if (AdapterHeal.KnownUrls(store, s).Count == 0) return false;
        if (r.Duplicates > 0 || r.Skipped > 0) return false;   // документи БУЛИ, просто вже наявні — це не збій
        try
        {
            var (candidate, proof) = await AdapterHeal.HealAsync(s, fetch, store, SinceYear, ct);
            if (candidate is null) return false;
            var prev = s.Kind;
            s.Kind = candidate;
            _heal.Switch(s.Id, prev, candidate, proof);
            var msg = $"самолікування: {prev}→{candidate} (збіг {proof} URL)";
            r.Note = r.Note.Length > 0 ? r.Note + "; " + msg : msg;
            Log("  ♻ " + s.Name + ": " + msg + " — наступний прогін по-новому");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch { return false; }
    }

    // Сторінка без PDF → аркуш канону (T7, SPEC §24): HTML зберігається як PDF у стилі канону,
    // без бланка (без емблеми/реквізитів/підпису). Вихідник HTML лишається поруч (.source.html) —
    // canon file.keep_with_pdf. Якщо WebView2 недоступний, не губимо сторінку: fallback на чистий HTML.
    private async Task SavePageAsCanonAsync(Source s, DocRef d, string title, string date, string type,
        string topic, string dir, string html, Catalog store, SourceResult r, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        var name = FileName(title, d.Url, s.Name, dir);
        var pdfPath = Path.Combine(dir, name + ".pdf");
        if (File.Exists(pdfPath)) { r.Skipped++; return; }

        try
        {
            // спершу — сама адреса: JS-сторінки (RUSI тощо) віддають тіло статті лише після гідрації;
            // якщо не вийшло — сирий HTML як запасний варіант
            var rendered = false;
            if (!string.IsNullOrWhiteSpace(d.Url) && d.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try { await CanonPdf.RenderUrlToPdfAsync(d.Url, title, s.Name, date, pdfPath, ct); rendered = true; }
                catch (OperationCanceledException) { throw; }
                catch { /* падаємо на сирий HTML нижче */ }
            }
            if (!rendered)
            {
                var doc = CanonPdf.Document(title, s.Name, topic, date, CanonPdf.ExtractText(html));
                await CanonPdf.RenderToPdfAsync(doc, pdfPath, ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log("  ! канон-pdf: " + ex.Message + " — лишаю чистий HTML");
            var raw = Path.Combine(dir, name + ".source.html");
            if (!File.Exists(raw)) await File.WriteAllTextAsync(raw, html, Encoding.UTF8, ct);
            store.Add(new DocRecord
            {
                Url = d.Url, Path = Path.GetRelativePath(store.Library, raw), Size = Encoding.UTF8.GetByteCount(html),
                Title = title, Date = date, Type = type, Topic = topic, Lang = s.Lang, Source = s.Name, Section = s.Section,
                Status = "html", Run = RunId,
            });
            r.Html++;
            Log("  + html " + Short(d.Url));
            return;
        }

        var sha = await ShaOfAsync(pdfPath, ct);
        if (store.HasSha(sha)) { try { File.Delete(pdfPath); } catch { } r.Duplicates++; return; }

        // вихідник HTML зберігаємо поруч — база для перегенерації, не для читання
        var src = Path.Combine(dir, name + ".source.html");
        if (!File.Exists(src)) await File.WriteAllTextAsync(src, html, Encoding.UTF8, ct);

        store.Add(new DocRecord
        {
            Url = d.Url, PageUrl = d.Url, Sha = sha,
            Path = Path.GetRelativePath(store.Library, pdfPath),
            Size = new FileInfo(pdfPath).Length, Title = title, Date = date, Type = type, Topic = topic,
            Lang = s.Lang, Source = s.Name, Section = s.Section, Status = "ok", Run = RunId,
            Magic = "%PDF", Mime = "application/pdf", TrustLevel = "L2",
            EditionKey = Editions.Key(null, title, int.TryParse(Naming.Year(date), out var y) ? y : null, sha),
        });
        r.New++;
        r.NewDocIds.Add(sha.Length >= 16 ? sha[..16] : sha);
        r.Bytes += new FileInfo(pdfPath).Length;
        Log("  + canon " + Short(d.Url) + " (" + (new FileInfo(pdfPath).Length / 1024) + " КБ)");
    }

    private static async Task<string> ShaOfAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var h = await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    private static SourceStatus DecideStatus(SourceResult r, DiscoveryStats stats)
    {
        if (r.New == 0 && r.Html == 0 && r.Skipped == 0 && r.Failed > 0) return SourceStatus.Partial;
        if (r.Failed > 0) return SourceStatus.Partial;
        if (stats.Capped) return SourceStatus.Partial;
        if (stats.Expected > 0 && r.Found < stats.Expected) return SourceStatus.Partial;
        return SourceStatus.Ok;
    }

    private async Task HandleAsync(Source s, DocRef d, Catalog store, Fetcher fetch, SourceResult r, CancellationToken ct)
    {
        // рішення черги остаточне: не тягнемо знову те, що вже позначено closed/gave_up/needs_human
        if (_queue?.Get(d.Url) is { IsTerminal: true }) { r.Skipped++; Log("  · закрито чергою " + Short(d.Url)); return; }
        if (NewOnly && store.HasUrl(d.Url))
        {
            r.Skipped++;
            return;
        }

        var title = d.Title;
        var date = d.Date;
        if (date.Length == 0) date = YearFromUrl(d.Url);
        var type = TypeOf(s, d.Url);
        var topic = TopicOf(title);
        string dir; // computed once the date is final: page metadata can still supply it
        string targetUrl;
        Saved? saved;

        if (d.IsPage)
        {
            string html;
            try { html = await fetch.GetTextAsync(d.Url, s.Rps, ct); }
            catch (LoginRequiredException) { Gate(r, d.Url, "🔒 потрібен вхід", "PAYWALL"); return; }
            catch (BlockedException ex) { GateBlocked(r, d.Url, ex.Message); return; }
            catch (NetworkDownException) { throw; }
            catch (Exception ex)
            {
                Fail(r, d.Url, ex.Message);
                return;
            }

            if (title.Length == 0) title = Adapters.PageTitle(html);
            if (date.Length == 0) date = Adapters.PageDate(html);
            if (topic.Length == 0) topic = TopicOf(title);
            dir = store.DirFor(s.Name, type, date);
            var pdf = Adapters.FirstPdf(html, d.Url);
            if (pdf != null && !string.IsNullOrWhiteSpace(s.PdfInclude)
                && !System.Text.RegularExpressions.Regex.IsMatch(pdf, s.PdfInclude!, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                pdf = null;
            if (pdf == null)
            {
                if (!s.KeepHtml)
                {
                    // A catalogue row without a public file is normal for armypubs (CAC),
                    // and a crawled page was never promised to be a document.
                    if (d.Crawled || s.Kind == "catalog") r.Skipped++;
                    else Fail(r, d.Url, "на сторінці немає PDF", "NO_PDF");
                    return;
                }
                await SavePageAsCanonAsync(s, d, title, date, type, topic, dir, html, store, r, ct);
                return;
            }

            targetUrl = pdf;
            try { saved = await DownloadAsync(pdf, dir, FileName(title, pdf, s.Name, dir), s, fetch, ct); }
            catch (LoginRequiredException) { Gate(r, pdf, "🔒 потрібен вхід", "PAYWALL"); return; }
            catch (NotFoundException ex) { Fail(r, pdf, ex.Message, "NOT_FOUND"); return; }
            catch (BlockedException ex) { GateBlocked(r, pdf, ex.Message); return; }
            if (saved == null)
            {
                Fail(r, pdf, "не PDF", "BAD_FORMAT");
                return;
            }
        }
        else
        {
            targetUrl = d.Url;
            dir = store.DirFor(s.Name, type, date);
            try { saved = await DownloadAsync(d.Url, dir, FileName(title, d.Url, s.Name, dir), s, fetch, ct); }
            catch (LoginRequiredException) { Gate(r, d.Url, "🔒 потрібен вхід", "PAYWALL"); return; }
            catch (NotFoundException ex) { Fail(r, d.Url, ex.Message, "NOT_FOUND"); return; }
            catch (BlockedException ex) { GateBlocked(r, d.Url, ex.Message); return; }
            if (saved == null)
            {
                Fail(r, d.Url, "не PDF");
                return;
            }
        }

        // Перед-гейт формату (SPEC §11, BAD_FORMAT): HTML-сторінка з кодом 200 не стає документом.
        var gate = FormatGate.Check(saved.Path, null, null);
        if (!gate.Ok)
        {
            try { File.Delete(saved.Path); } catch { }
            Fail(r, targetUrl, gate.Detail, gate.Code);
            return;
        }
        var magic = FormatGate.Magic(saved.Path);

        if (store.HasSha(saved.Sha))
        {
            // Delete only a copy that landed somewhere new. With --force the file is
            // re-downloaded over the same path, and deleting it would empty the library.
            var known = store.BySha(saved.Sha);
            var samePath = known != null && string.Equals(
                Path.GetFullPath(known.Path), Path.GetFullPath(saved.Path), StringComparison.OrdinalIgnoreCase);
            if (!samePath) { try { File.Delete(saved.Path); } catch { } }
            r.Duplicates++;
            Log("  = дубль " + Short(targetUrl));
            return;
        }

        store.Add(new DocRecord
        {
            Url = targetUrl, PageUrl = d.IsPage && d.Url != targetUrl ? d.Url : "", Sha = saved.Sha,
            Size = saved.Size, Path = Path.GetRelativePath(store.Library, saved.Path), Title = title, Date = date, Type = type, Topic = topic,
            Lang = s.Lang, Source = s.Name, Section = s.Section, Status = "ok", Run = RunId,
            Magic = magic, Mime = "application/pdf", TrustLevel = "L2",
            EditionKey = Editions.Key(null, title, int.TryParse(Naming.Year(date), out var y) ? y : null, saved.Sha),
        });
        r.New++;
        r.NewDocIds.Add(saved.Sha.Length >= 16 ? saved.Sha[..16] : saved.Sha);
        r.Bytes += saved.Size;
        Log("  + " + Short(targetUrl) + " (" + (saved.Size / 1024 / 1024.0).ToString("0.0") + " МБ)");
        OnDoc?.Invoke(s.Name, targetUrl, "завантажено", saved.Size);
    }


    // ---- вибірковий режим: спершу оглянути список, потім качати лише позначене ----

    /// <summary>Розібрати список джерела й НІЧОГО не качати (для екрана «Документи»).</summary>
    public async Task<List<DocRef>> PreviewAsync(Source s, Catalog store, CancellationToken ct)
    {
        if (s.Manual)
        {
            Log("  ручне джерело: автоматичний обхід вимкнено (" + (string.IsNullOrWhiteSpace(s.Note) ? "див. sources.json" : s.Note) + ")");
            return new List<DocRef>();
        }
        var stats = new DiscoveryStats();
        using var fetch = new Fetcher(Path.Combine(store.Root, "state")) { MinIntervalMs = 1000 };
        var list = new List<DocRef>();
        try
        {
            await foreach (var d in Adapters.DiscoverAsync(s, fetch, SinceYear, stats, _cursors, ct))
            {
                if (list.Any(x => Catalog.Canonical(x.Url) == Catalog.Canonical(d.Url))) continue;
                list.Add(d);
                if (list.Count >= MaxDocsPerSource) { stats.Capped = true; break; }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log("  огляд зупинився: " + ex.Message);   // частковий список краще за падіння
        }
        Log("  огляд: " + list.Count + " документів, нічого не завантажено");
        return list;
    }

    /// <summary>Завантажити рівно ці документи джерела (вибране користувачем).</summary>
    public async Task<(int saved, int failed)> FetchDocsAsync(Source s, IReadOnlyList<DocRef> docs, Catalog store, CancellationToken ct)
    {
        using var fetch = new Fetcher(Path.Combine(store.Root, "state")) { MinIntervalMs = 1000 };
        var r = new SourceResult { Id = s.Id, Name = s.Name, Section = s.Section };
        foreach (var d in docs)
        {
            ct.ThrowIfCancellationRequested();
            await HandleAsync(s, d, store, fetch, r, ct);
        }
        var saved = r.New + r.Html;
        Log("  вибране: збережено " + saved + ", помилок " + r.Failed);
        return (saved, r.Failed);
    }

    // Full URLs go to the log file: a truncated one cannot be re-checked or fixed later.


    // Спершу — те, що впало минулого разу: без цього кожна невдача коштує повного проходу джерела.
    private async Task RetryPendingAsync(Catalog store, Fetcher fetch, Dictionary<string, Source> known,
        CancellationToken ct)
    {
        var pending = _queue!.Pending();
        if (pending.Count == 0) return;
        Log($"↻ черга: {pending.Count} невдач із минулих прогонів");

        foreach (var e in pending)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!Uri.TryCreate(e.QueueKey, UriKind.Absolute, out var uri))
                { _queue.Close(e.QueueKey, "auto", "не URL"); continue; }

                known.TryGetValue(e.Library, out var src);
                var name = Naming.BaseName(null, Path.GetFileNameWithoutExtension(uri.AbsolutePath));
                var dir = store.DirFor(src?.Name ?? e.Library, "other", null);
                Directory.CreateDirectory(dir);

                var saved = await fetch.DownloadAsync(e.QueueKey, dir, name, src?.Rps ?? 1, ct);
                if (saved is null) { _queue.Attempt(e.QueueKey, "NETWORK"); continue; }
                if (store.HasSha(saved.Sha))
                {
                    try { File.Delete(saved.Path); } catch { }
                    _queue.Close(e.QueueKey, "auto", "уже в каталозі");
                    Log("  ✓ черга: " + Short(e.QueueKey) + " — уже мали");
                    continue;
                }

                var gate = FormatGate.Check(saved.Path, null, null);
                if (!gate.Ok)
                {
                    try { File.Delete(saved.Path); } catch { }
                    _queue.Attempt(e.QueueKey, gate.Code);
                    continue;
                }

                store.Add(new DocRecord
                {
                    Url = e.QueueKey, Sha = saved.Sha, Size = saved.Size,
                    Path = Path.GetRelativePath(store.Library, saved.Path),
                    Title = Naming.CleanTitle(name), OrigName = Path.GetFileName(saved.Path),
                    Type = "other", TypeSource = "rule", Status = "ok", TrustLevel = "L2",
                    ChannelId = src?.Id ?? e.Library, Method = "http_get", RetrievedAt = DateTime.UtcNow.ToString("o"),
                    Magic = FormatGate.Magic(saved.Path), Mime = "application/pdf",
                    Source = src?.Name ?? e.Library, Section = src?.Section ?? "",
                    Lang = src?.Lang ?? "", Topic = TopicOf(Naming.CleanTitle(name)),
                    EditionKey = Editions.Key(null, name, null, saved.Sha)
                });
                _queue.Close(e.QueueKey, "auto", "взято з другої спроби");
                Log("  ✓ черга: " + Short(e.QueueKey) + " — добрано");
            }
            catch (OperationCanceledException) { throw; }
            catch (LoginRequiredException) { _queue.Attempt(e.QueueKey, "PAYWALL"); }
            catch (BlockedException) { _queue.Attempt(e.QueueKey, "BLOCKED"); }
            catch (NotFoundException) { _queue.Attempt(e.QueueKey, "NOT_FOUND"); }
            catch (Exception ex)
            {
                Log("  ! черга: " + Short(e.QueueKey) + " — " + ex.GetType().Name);
                _queue.Attempt(e.QueueKey, "NETWORK");
            }
        }
    }

    private void Remember(string url, SourceResult r, string? code)
    {
        if (_queue is null || string.IsNullOrEmpty(code)) return;
        // закрите лишається закритим: інакше кожен прогін плодить новий запис на те саме мертве посилання
        if (_queue.Get(url)?.IsTerminal == true) return;
        try { _queue.Enqueue(url, code, r.Id); }
        catch (Exception ex) { Log("  ! черга: " + ex.GetType().Name + " — " + ex.Message); }
    }

    private static string Short(string url) => url;

    private void Fail(SourceResult r, string url, string reason, string? code = null)
    {
        Remember(url, r, code);
        r.Failed++;
        if (r.Failures.Count < 5) r.Failures.Add(reason);
        Log("  ! " + Short(url) + " — " + reason);
        OnDoc?.Invoke(r.Name, url, "помилка: " + reason, 0);
    }

    // 403 від ОКРЕМОГО документа (часто чужий хост у посиланні) — це провал документа, не джерела.
    private void GateBlocked(SourceResult r, string url, string reason)
    {
        Remember(url, r, "BLOCKED");
        r.BlockedDocs++;
        r.Failed++;
        if (r.Failures.Count < 5) r.Failures.Add(reason);
        Log("  ⛔ " + reason + " " + Short(url));
        OnDoc?.Invoke(r.Name, url, reason, 0);
    }

    private void Gate(SourceResult r, string url, string reason, string? code = null)
    {
        Remember(url, r, code);
        r.LoginGated++;
        r.Failed++;
        if (r.Failures.Count < 5) r.Failures.Add(reason);
        Log("  " + reason + " " + Short(url));
    }

    // Conservative: only an explicit /20XX/ or 20XX- token in the path counts. It keeps
    // CALL-style links (…/downloads/2026/02/…) out of the "без дати" folder.
    public static string YearFromUrl(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url, @"[/_-](20[0-3]\d)[/_\-.]");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static readonly string[] GenericNames = { "file", "download", "index", "document", "view", "content", "full", "article", "pdf" };

    // Ім'я за конвенцією §5: серія + заголовок, без «__хеша», поки назва вільна.
    public static string FileName(string? title, string url, string? fallback = null, string? dir = null)
    {
        var baseName = Naming.BaseName(null, title, fallback);
        return string.IsNullOrEmpty(dir) ? baseName : Naming.Unique(dir, baseName, url);
    }

    // The library names its own sections in the URL; take that name instead of inventing one.
    public static string TypeOf(Source s, string url)
    {
        if (string.IsNullOrWhiteSpace(s.Taxonomy)) return "other";
        if (s.Taxonomy!.StartsWith('=')) return s.Taxonomy[1..].ToLowerInvariant();
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(url, s.Taxonomy!,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
                for (int g = 1; g < m.Groups.Count; g++)
                    if (m.Groups[g].Success && m.Groups[g].Value.Length > 0)
                        return Naming.Type(m.Groups[g].Value);
        }
        catch { }
        return "other";
    }

    // A guess, clearly marked as one: it goes into index.csv, never into a folder name.
    private static readonly (string Topic, string[] Words)[] TopicMap =
    {
        ("Розмінування", new[] { "мін", "mine", "clearance", "eod", "ied", "uxo", "сапер", "демининг" }),
        ("Фортифікація", new[] { "фортифікац", "fortific", "trench", "бліндаж", "bunker", "позиційн", "укріплен" }),
        ("Переправи", new[] { "переправ", "міст", "bridge", "bridging", "ford", "gap cross", "river", "pontoon", "ferry" }),
        ("Дрони", new[] { "дрон", "uav", "uas", "fpv", "безпілот", "unmanned", "loitering", "шахед", "shahed" }),
        ("РЕБ", new[] { "реб", "electronic warfare", " jamming", "ew ", "спектр", "radio-electronic" }),
        ("Доктрина", new[] { "doctrine", "доктрин", "field manual", "fm 3", "atp ", "jp ", "concept", "концепц", "статут" }),
        ("Логістика", new[] { "логіст", "logist", "sustain", "забезпечення", "supply", "постачан" }),
        ("Медицина", new[] { "медич", "medic", "casualt", "triage", "евакуац", "поранен" }),
        ("Підготовка", new[] { "навчан", "training", "exercise", "підготовк", "курс", "school" }),
        ("Розвідка", new[] { "розвід", "recon", "intelligence", "isr", "surveil" }),
        ("Кібер", new[] { "кібер", "cyber", "informaтion warfare", "іпсо", "information operation" }),
        ("Артилерія", new[] { "артилер", "artiller", "fires", "himars", "counter-battery", "снаряд" }),
        ("Броня", new[] { "брон", "tank", "armor", "armour", "бмп", "бтр", "afv" }),
        ("Авіація", new[] { "авіац", "aviation", "air force", "helicopter", "гелікоптер", "aircraft" }),
        ("Флот", new[] { "флот", "naval", "navy", "кораб", "maritime", "судн" }),
        ("ППО", new[] { "ппо", "air defence", "air defense", "sam ", "防空", "missile defence" }),
    };

    public static string TopicOf(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = title.ToLowerInvariant();
        foreach (var (topic, words) in TopicMap)
            if (words.Any(w => t.Contains(w, StringComparison.Ordinal))) return topic;
        return "";
    }

    // Moves an existing collection into the current tree and re-derives type/topic from the
    // registry, so an old 90 GB folder does not stay split across two layouts.
    public async Task<(int Moved, int Missing)> RelayoutAsync(Catalog store, IEnumerable<Source> sources, Action<string> log)
    {
        var byName = sources.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First());
        int moved = 0, missing = 0;
        foreach (var r in store.All().ToList())
        {
            var src = byName.TryGetValue(r.Source, out var found) ? found : null;
            var type = string.IsNullOrWhiteSpace(r.Type) ? (src != null ? TypeOf(src, r.Url) : "інше") : r.Type;
            var topic = string.IsNullOrWhiteSpace(r.Topic) ? TopicOf(r.Title) : r.Topic;
            var abs = Path.Combine(store.Library, r.Path);          // у каталозі шлях відносний
            var target = Path.Combine(store.DirFor(r.Source, type, r.Date), Path.GetFileName(abs));
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(abs), StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(abs)) { missing++; continue; }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(abs, target, true);
                    moved++;
                }
                catch (Exception ex) { log("  ! " + r.Path + " — " + ex.Message); missing++; continue; }
            }
            r.Path = Path.GetRelativePath(store.Library, target);
            r.Type = type;
            r.Topic = topic;
            store.Add(r);
        }
        await Task.CompletedTask;
        return (moved, missing);
    }

    private static async Task MergeCataloguesAsync(string root, string dir)
    {
        var files = Directory.GetFiles(dir, "*.csv").OrderBy(f => f).ToList();
        if (files.Count == 0) return;
        var merged = new List<IEnumerable<string>>();
        foreach (var f in files)
        {
            var rows = Csv.ReadAll(f);
            if (rows.Count == 0) continue;
            merged.AddRange(rows.Skip(1));
        }
        var head = new List<IEnumerable<string>> { new[] { "номер", "статус", "дата", "назва", "розробник", "посилання", "джерело" } };
        head.AddRange(merged);
        await Csv.WriteAsync(Path.Combine(root, "catalogue.csv"), head);
    }

    private static string Escape(string s) => s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

    // Звіт один: RunReport -> reports/<run_id>.json + .md (SPEC §21). Окремої таблиці-обліку більше немає.
    public static Task<string> WriteReportAsync(string root, string runId, List<SourceResult> results,
        int sinceYear, Catalog? store = null)
    {
        var started = DateTime.UtcNow;
        var m = System.Text.RegularExpressions.Regex.Match(runId ?? "", @"^(\d{8})-(\d{6})");
        if (m.Success) started = DateTime.SpecifyKind(
            DateTime.ParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", null), DateTimeKind.Local);

        var rep = new RunReport
        {
            RunId = string.IsNullOrWhiteSpace(runId) ? ReportWriter.NewRunId(DateTime.UtcNow) : runId!,
            StartedAt = started,
            FinishedAt = DateTime.UtcNow,
            Checked = results.Count,
            Fetched = results.Sum(r => r.New + r.Html),
            Skipped = results.Sum(r => r.Skipped + r.Duplicates),
            FilesOnDisk = store is null ? 0 : CountFiles(store.Library),
            FilesInCatalog = store?.Count ?? 0
        };

        var failed = results.Sum(r => r.Failed);
        if (failed > 0) rep.FailedByCode["ІНШЕ"] = failed;   // коди причин підключимо разом із чергою (§11)
        foreach (var r in results) rep.NewIds.AddRange(r.NewDocIds.Take(200));

        foreach (var r in results)
        {
            var why = r.Note;
            if (r.Failures.Count > 0)
                why = (why.Length > 0 ? why + "; " : "") + string.Join("; ", r.Failures.Distinct().Take(3));
            rep.Sources.Add(new SourceRow
            {
                Id = r.Id, Name = r.Name, Section = r.Section, Status = r.StatusText,
                Found = r.Found, Expected = r.Expected, New = r.New, Html = r.Html,
                Dupes = r.Duplicates, Failed = r.Failed, Elapsed = r.Elapsed, Why = why
            });
        }

        var json = ReportWriter.Write(rep, store?.State ?? Path.Combine(root, "state"));
        var md = Path.ChangeExtension(json, ".md");
        try { File.Copy(md, Path.Combine(root, "report.md"), true); } catch { }
        return Task.FromResult(md);
    }

    private static int CountFiles(string library)
    {
        if (!Directory.Exists(library)) return 0;
        return Directory.EnumerateFiles(library, "*", SearchOption.AllDirectories)
                        .Count(f => !f.Contains(Path.DirectorySeparatorChar + ".state" + Path.DirectorySeparatorChar,
                                                StringComparison.OrdinalIgnoreCase)
                                 && !f.EndsWith("index.csv", StringComparison.OrdinalIgnoreCase));
    }

    // Файл, кинутий у вікно (drag&drop): той самий вхід, що й завантаження, тільки джерело — локальний диск.
    public static IngestResult IngestLocal(Catalog store, string srcPath, DateTime? dateHint = null)
    {
        try
        {
            var gate = FormatGate.Check(srcPath, null, null);
            if (!gate.Ok) return new IngestResult(null, false, gate.Code + ": " + gate.Detail);

            var sha = ShaOfAsync(srcPath, CancellationToken.None).GetAwaiter().GetResult();
            if (store.HasSha(sha)) return new IngestResult(null, true, "");

            var title = Naming.CleanTitle(Path.GetFileNameWithoutExtension(srcPath));
            if (title.Length == 0) title = "document";
            var date = (dateHint ?? File.GetLastWriteTime(srcPath)).ToString("yyyy-MM-dd");
            var type = "other";
            var topic = TopicOf(title);
            var dir = store.DirFor("Вручну", type, date);
            Directory.CreateDirectory(dir);
            var name = Naming.Unique(dir, Naming.BaseName(null, title), srcPath);
            var target = Path.Combine(dir, name + Path.GetExtension(srcPath));
            File.Copy(srcPath, target, overwrite: true);

            var doc = new DocRecord
            {
                Url = "file:" + Path.GetFullPath(srcPath), Sha = sha, Size = new FileInfo(target).Length,
                Path = Path.GetRelativePath(store.Library, target), OrigName = Path.GetFileName(srcPath),
                Title = title, Date = date, Type = type, Topic = topic, Source = "Вручну", Section = "вручну",
                Status = "ok", Method = "local_file", ChannelId = "manual",
                RetrievedAt = DateTime.UtcNow.ToString("o"), Magic = FormatGate.Magic(target),
                Mime = FormatGate.MimeOf(FormatGate.Magic(target)), TrustLevel = "L2",
                EditionKey = Editions.Key(null, title, int.TryParse(Naming.Year(date), out var y) ? y : null, sha),
            };
            store.Add(doc);
            return new IngestResult(doc, false, "");
        }
        catch (Exception ex)
        {
            return new IngestResult(null, false, ex.Message);
        }
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var r = new List<(string, bool, string)>();
        void Check(string n, bool ok, string d = "") => r.Add((n, ok, d));
        var tmp = Path.Combine(Path.GetTempPath(), "dtic-ingest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var store = new Catalog(tmp))
            {
                var pdf = Path.Combine(tmp, "src.pdf");
                File.WriteAllBytes(pdf, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%x\n1 0 obj\nendobj\n"));
                var (doc, dup, err) = IngestLocal(store, pdf);
                Check("ingest: локальний PDF прийнято", doc != null && !dup && err == "", err);
                Check("ingest: файл ліг у бібліотеку", doc != null && File.Exists(Path.Combine(store.Library, doc.Path)));
                Check("ingest: джерело «вручну»", doc?.Source == "Вручну" && doc?.Method == "local_file");
                var again = IngestLocal(store, pdf);
                Check("ingest: повторний файл — дубль", again.Doc == null && again.Duplicate, again.Error);
            }
        }
        catch (Exception ex) { Check("ingest: виняток", false, ex.GetType().Name + ": " + ex.Message); }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        return r.ToArray();
    }
}
