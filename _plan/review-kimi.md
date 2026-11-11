# Рев'ю коду — moonshotai/kimi-k3.1-pro

_створено 2026-11-11 17:54 · токенів: вхід 136 897, вихід 6 000_

I need produce code review findings list Ukrainian concise only findings format file:line — problem — why dangerous — fix. Should thoroughly inspect code facts. Should prioritize real bugs, data loss/races, false reports, leaks, network fragility, duplication dead code. Should line numbers approximate from provided. Should not have general advice/praise. Should be accurate avoid speculative. I have huge code ~ many files. Should identify strongest issues. I can output perhaps 25-40 findings with line numbers approximate based snippets. Should not overdo weak points. Should carefully analyze.

I need inspect each module for concrete bugs.

Catalog critical:
- MaxLineBytes=4096 and Add throws if record >4KB. Many records could exceed due title/path etc but normal under. More important CompactIfNeeded uses d.Raw.ToJsonString no max and raw maintained. Load FromJson does not populate Date, Section, Orig? It populates many but omissions: d.Date never set! d.Section never set! d.EditionKey yes, etc. Also d.Url acquisition. d.Date omitted; d.Section omitted. ToJson does not write "date" nor "section" at all! DocRecord Date assigned throughout but ToJson lacks o["date"], o["section"]. Data loss on every write/reload: date blank, section blank, year stored. This affects Dir? relayout uses r.Date after reload => blank, moves to unknown. Topic? section report etc. Also DocRecord.Status etc. d.RetrievedAt read. d.TopicSource read? FromJson sets TypeSource but not TopicSource! It sets d.Classification, etc but not TopicSource. ToJson writes topic_source. After reload defaults "rule", losing model/user. Date and section definite. Also d.Size etc.
- ToJson Raw deepclone preserving unknown fields but known fields omitted stay stale in Raw? If d.Raw has date from newer schema, not touch. But current not write. OrigName etc.
- Load tombstones: if deleted record, adds tombstone remove byKey, continue, does not update _watermark based on tombstone rev. Delete increments _watermark separately. On reload watermark from live only; compact tombstones rev = _watermark+1 possibly duplicates/rev lower? Less severe.
- Load chooses d.Rev >= cur.Rev, so later line wins ties good.
- Add: d.Rev = prev.Rev+1 else Math.Max(1,d.Rev). If record was tombstoned, _byKey no prev, d.Rev maybe old rev -> Math.Max(1,d.Rev), potentially not > tombstone rev. Load processes sequential lines: tombstone then re-add record always removes tombstone and byKey set regardless rev, so okay within same file; compaction preserves latest byKey and all tombstones? _tombstones removed on readd. Fine.
- Add writes file then updates memory; WriteMeta each add non-atomic pair but meta optimization not used.
- CompactIfNeeded called after Load in constructor, _append opened FileMode.Append FileShare.Read. It disposes _append then File.Replace(tmp, _catalogPath,null) on Windows. File.Replace requires destination exists. If _lines>0 destination exists yes. Good. But tmp maybe from previous crash overwritten FileMode.Create.
- RepairTail called before Load. It treats any file not ending \n as torn and cuts after last newline, even if valid single line without newline acceptable but their writes always newline. Fine.
- Catalog read-only constructor: _append FileMode.OpenOrCreate FileAccess.Read FileShare.ReadWrite. Load. Does not Compact. Dispose flush(true) on read stream: _append.Flush(true) likely NotSupportedException caught then _append.Dispose in same try? Code `try { _append.Flush(true); _append.Dispose(); } catch { }` if Flush throws, Dispose not called! This leaks file stream and catalog lock? lock disposed second try. In read-only mode _append read stream Flush(true) may throw NotSupportedException/IOException? FileStream.Flush(true) on read perhaps flush? Should .NET FileStream.Flush(bool) if !CanWrite perhaps? likely flush does nothing? Could be not sure. Not strong.
- InUseException catches only IOException, FileStream may throw Unauthorized etc not relevant.
- Catalog file lock FileShare.None works.
- Compact with d.Raw might be null? From load raw. Add raw. Fine.
- HasUrl O(N) each doc, performance not correctness.
- Canonical removes query entirely, contrary AdapterHeal explicit query distinct. This causes documents download?id=1 and ?id=2 treated same for seen and HasUrl, dropped. Concrete data loss. Also seen in Harvester uses Canonical to dedupe discovery. Should flag. Also lowercases entire path (URLs path case-sensitive) and strips trailing slash; can collapse distinct docs. Most serious query.

- ExportCsvAsync writes index.csv in Library. CountFiles excludes only filename ending index.csv anywhere but source .source.html counted as files on disk while catalog only one record per canon pdf plus .source.html not cataloged; discrepancy always for canon pages. Also fallback HTML catalog record has Sha empty? Catalog.Add rejects empty Sha! SavePageAsCanonAsync fallback store.Add DocRecord without Sha -> ArgumentException "document without sha256" not caught within catch? In catch exception ex from canon rendering: it writes raw then store.Add with no Sha. That throws, escapes HandleAsync/RunAsync and whole source catches generic -> Failed, r.Html not incremented. The HTML file exists unindexed. This is definite and serious. I'll flag. Similarly discovered html fallback claims "лишаю чистий HTML" but then fails to catalog and source fails. Fix hash HTML bytes and set Sha/Mime etc or allow non-sha id (but contract hash) and proper path.
- SavePageAsCanonAsync PDF skip: if pdfPath exists, r.Skipped++ and return, even if file isn't in catalog (crash after PDF before Add) => never catalogs existing orphan. This means crash gap permanent unless force? NewOnly store.HasUrl false, call SavePageAsCanon then exists skip forever. Should verify file/catalog and ingest/recover.
- SavePageAsCanon PDF path collision and Naming.Unique: name computed based on pdfPath only. It creates PDF, then source .source.html. Fine.
- duplicate handling: store.HasSha saved.Sha -> known.Path relative to library vs saved.Path absolute. They compare Path.GetFullPath(known.Path) with Path.GetFullPath(saved.Path). known.Path relative like `army/file.pdf`, resolved against current working directory, not store.Library. Always unequal except coincidence. If --force same file redownloaded to same path, samePath false, deletes saved.Path, i.e. existing library file! Comment explicitly intends avoid but bug. This is critical data loss. Should compare Path.Combine(store.Library, known.Path) except in-place absolute. For ImportInPlace known.Path absolute, Path.GetFullPath works. So fix helper resolve rooted vs combine. This also affects queue duplicates: RetryPending always deletes saved path after HasSha, could be same existing path if Naming.Unique? It uses dir other, name base, fetcher File.Move(temp,final,true), final path same; if catalog already references it, deletes library file and closes queue "already". Should same path safeguard there too. Could flag combined.
- Duplicate final files in Fetcher: DownloadHttpAsync File.Move(temp, final, true) overwrites existing final file before duplicate check. Naming.Unique ensures if base file exists uses hashed suffix based on URL. Same URL => same hashed name, overwrite same expected content? if content changed at same URL, new sha -> store.Add new record with same path and old record still in catalog points same path. Old document's file overwritten, old hash invalid. New and old records both point same file. Data loss of old content and false catalog. Similarly canon name existing skip prevents new. Download normal: `FileName(title,url,s.Name,dir)` returns Naming.Unique(dir,base,url), checks existence. If existing same base from another URL -> hash by URL. For same URL existing => same hash path, overwrite. If remote content changed new SHA, adds new record and old record path overwritten. Should unique by content or update/replace record semantics and remove/mark old; don't overwrite catalog referenced file. This is very important.
- Harvester.RelayoutAsync File.Move(abs,target,true) overwrite any target existing file. Could destroy distinct document and then update r.Path. If target exists with other content, data loss. Also same filename collisions not resolved. Use no-overwrite and unique/hashing, verify.
- ImportInPlace stores absolute path in DocRecord.Path while Catalog convention relative; other methods Path.Combine(store.Library,r.Path) returns absolute if second rooted (Path.Combine discards first) so okay. Export report counts discrepancy by design in-place outside library: FilesInCatalog increases but FilesOnDisk doesn't => report "missing" false recommendation. Could flag false report: report always discrepancy for external in-place records and source.html companions. Should differentiate.

- ReindexAsync skips any file where parts.Length<4; requires path `<lib>/<type>/<year>/<file>` correct. It enumerates .source.html too and index.csv? It skips .state but not index.csv (parts length1 skip), .source.html in canonical path parts length4 => imports as independent document text/html with Sha. This leads duplicate content catalog entries for .source.html, not necessarily bug but undesired. Also MagicOf returns "" for nonpdf and Mime set text/html based solely extension pdf vs everything else. .docx if present gets text/html. Reindex accepts all files, and sets Status ok Trust L5, Mime from extension not bytes. Could false info. It computes sha and gate not applied. Perhaps less priority.
- Reindex yearText = parts[^2] good, type parts1.
- IngestLocal: gate permits only PDF, ZIP docx, OLE. File filter ImportFolder includes djvu/docx/epub but FormatGate Check for djvu and epub? epub PK accepted unless mime PDF ext not ooxml: condition mime==pdf false so PK returns true "zip" so epub accepted. djvu magic unknown BAD_FORMAT. Docx works. .pdf with tiny bytes e.g. selftest %PDF accepted size >0. IngestLocal `File.Copy(srcPath,target,overwrite:true)` with Naming.Unique if existing same base gets URL hash based on src path. If source already imported same file path but changed content, same target and overwrite referenced old. Similar.
- IngestLocal computes date via local last write, okay.
- IngestLocal no cleanup target if store.Add throws (e.g. >4k) leaving orphan. Moderate.
- ImportInPlace calls ShaOfAsync.GetAwaiter().GetResult within sync; no UI (Task.Run). Fine. Catch includes OperationCanceled? `ct.ThrowIf...` outside try; inside sha cancellation OperationCanceledException caught as generic and skipped, so cancellation during SHA is swallowed per file, continues next and then thrown top. okay.
- AppSettings SelfCheck side effects on real openrouter key and proxy credentials: test saves current key, sets test key, in finally `new AppSettings { OpenRouterKey = prev };` setter works. If reading prev fails? fine. Parallel selftest with app can race Secrets writes causing actual key loss? Gate only per process and operations ReadRaw+Write lock so sequential. But test alters secret globally and other threads may see test key. Selftest only CLI usually. Could flag not needed.
- Secrets concurrency: ReadRaw on unreadable/corrupt file returns empty and creates .bad, next Set writes new dict, losing all existing secrets despite comment "instead of silently overwriting it on next Set" — comment false: backup then overwrites original with only new entry. This is data loss and contradiction. Should fail Set on read error or merge after recovery. Strong. Also ReadRaw used Get -> corrupt file copied .bad each read. Set wipes others. File.Replace atomic.
- AppSettings.Load corrupted settings -> defaults silently then next Save overwrites, typical.
- AdapterHeal.State Save swallows all exceptions. Switch updates _map and returns even if Save failed, report says self-healing saved and next run new method but not persisted. Also Cursors.Save logs, okay. AdapterHeal Apply mutates Source.Kind, then TryHeal prev = s.Kind currently healed not base; after switch maybe Prev current. Revert uses base registry. Fine.
- State deserialization Dictionary default comparer? `_map = JsonSerializer.Deserialize<Dictionary...>` yields default case-sensitive, while initial OrdIgnoreCase. After reload IDs case-sensitive. Minor.
- HealAsync baseline Take(20) arbitrary catalog order. got stops if >baseline count. SameUrlSet exact. If candidate only yields first 20 out of >20 and no extra? It breaks when got.Count>20 at 21, so won't accept. Good.

- CandidateKinds static/catalog not candidates, but current "catalog" can heal to others. okay.
- SelfCheck AdapterHeal Switch writes state in temp.
- Adapters Oai:
  - `sep` determined before; good.
  - `int.Parse(sz)` can overflow => generic source fail. okay.
  - regex parsing brittle but known.
  - `cursors?.Set(s.Id, next is null ? null : token)` after each page. If cancellation during page, cursor remains previous page token from prior iteration? Start resume token then after page set next token. good.
  - Cursor completion only when next null. If maxPages reached, cursor remains token for next page, good. If error after some records yield but before cursor update, repeat duplicates okay.
  - Date Old: if no date never old. okay.
- Dspace:
  - for page condition `page < Math.Min(totalPages,2000)` totalPages initially1, first page parse.
  - catch JSON errors yield break without clearing cursor -> resume same page next run (good retry) but silent no error: Discover returns no documents, Harvester marks Empty and maybe healing. This can lie about source: malformed response silently yields empty rather than failed. Similar DspacePdfAsync catches everything incl OperationCanceledException and returns null! This is serious: cancellation (budget/user) swallowed per item, then loop continues? ct remains canceled, next GetJsonAsync throws cancellation outside DspacePdf? Each item call catches cancellation and returns null quickly; loops items, then cursors.Set advances page despite PDFs unresolved due cancellation/network/errors. It will skip docs permanently, cursor moves. Network/JSON errors swallowed and item treated no pdf. Should only catch expected parse/HTTP? Fetcher exceptions should propagate and no cursor advance. Strong.
  - Dspace parse catch only JsonException/KeyNotFound/InvalidOperation; GetProperty can throw etc included. NullReference? no. te GetInt32 overflow etc FormatException not caught.
  - cursor set after processing all items. If one pdf null due transient error, still advance => permanent skip. Mention.
- WpAsync:
  - `if json.TrimStart().StartsWith("{") { cursors.Set(null); yield break; }` treats WP error/rate-limit JSON object (e.g. rest_post_invalid_page_number at normal end, but also errors) as successful completion and clears cursor. Http SendAsync doesn't ensure success for 400 (only special 403/429/503 and >=500 retry logic) so GetJsonAsync returns 400 body. This can silently truncate; for invalid page at end expected. Other error object e.g. rest_forbidden should be failure not clear. Should distinguish code.
  - `batch.Count==0` with posts all no link clears cursor and stops, even later pages? Probably okay if array nonempty but all no link, should not treat end.
  - `if page <=10000`, after loop naturally hits 10000 and doesn't clear cursor? cursor set null at page==10000 due `page <10000 ? ... : null`. fine.
- Sitemap:
  - Expected = found.Count when below cap, then Harvester DecideStatus `Expected >0 && Found<Expected` fine. But found filtered by Match/since, expected source only list count not claim actual total.
  - `found.Count >= MaxDocs` cap can be triggered exactly equal even if no more docs, marks capped though exact total, minor false partial.
  - `stats.Capped` set by Harvester when max docs per source reached. expected found count if discovery stops early? Sitemap fully reads before yields.
- HtmlAsync:
  - listing pagination `links.Contains(l)` uses default case-sensitive and exact string vs Canonical, minor.

  - pdfs `.Distinct()` default case-sensitive.
  - If `pdfs.Count == 0 && links.Count(Match) <=3 && !backgrounders` yields listing page itself as document. With KeepHtml true and no pdf, it will canon-PDF a listing/navigation page even if clearly listing <=3. Comment says single article only but heuristic weak. Not necessarily report lie, design.
  - stats.Expected remains -1 always for html, fine.
  - date unused.
- StaticAsync ignores sinceYear (signature), so static old documents included regardless filter. Since filter explicit selected? static source maybe expected filter? likely bug if spec. Could flag lower.
- FirstPdf `citation_pdf_url` regex first assumes quote order content after name; second handles. Good.
- PageDate text fallback Clean entire html then first 6000 chars, includes nav; comments claims header area but no header extraction. Can wrong date. Not central.
- YearFromUrl regex requires trailing delimiter after year; `/2026` end no match. okay.
- Report:
  - WriteReportAsync started parsing RunId local and SpecifyKind Local then ToUniversalTime. RunId created DateTime.Now. Good.
  - RunReport.Checked = results.Count = sources processed not docs checked, spec maybe checked sources. Markdown Sources(r.Checked) so okay.
  - FailedByCode all "ІНШЕ", no codes despite reason queue has codes. This makes report less actionable but comment admits.
  - NewIds per source Take(200) then each all sources potentially many.
  - files_on_disk counts .source.html, log? library includes .state excluded only paths with directory separator + ".state" + separator. On Linux? Windows. It counts source companions. InCatalog count includes external in-place, records with no files and duplicate old path. discrepancy unreliable.
  - WriteReportAsync passes store only in CLI, MainForm passes no store: `WriteReportAsync(root,..., harvester.SinceYear)` so FilesOnDisk=0, FilesInCatalog=0 and report always claims 0 even collection non-empty. Definite false report UI. Fix pass store. Very strong.
  - `report.md` copy failures swallowed; GUI log says report path actual md exists. okay.
  - MainForm finally `_lblState.Text = CountCollection()` overwrites "готово" summary or "зупинено" immediate, not false just UX.
  - CollectCli cancellation: on OperationCanceled returns 130 before ExportCsv/report, results not written. Logs console only. Claim "зупинено користувачем". okay.
  - Harvester.RunAsync cancellation: r status set canceled, elapsed then throw; finally executes OnSourceDone etc; results lost caller. okay.
  - `DecideStatus`: first condition redundant. More serious: Source with docs all already existing (Skipped>0, New=0, Failed=0, Expected=0 etc) returns Ok good. Source with docs but every handle fails and BlockedDocs=0 returns Partial (not Failed) but counts. okay.
  - Empty source calls TryHeal then `continue` inside try; finally runs. Status Empty even healing succeeded note appended. okay.
  - OperationCanceled catches ordering bug:
```
catch (OperationCanceledException) when (ct.IsCancellationRequested) { ... throw; }
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
 r.Status=Network; r.Note="сайт не віддав список за 3 хвилини";
}
catch (OperationCanceledException)
{
 r.Status=Partial; budget...
}
```
Second catches ALL cancellations not user, including SourceBudget expiration and listCts 3min. Third unreachable. So source budget always mislabeled Network "сайт не віддав список за 3 хвилини" even if 10min budget while downloading. This is concrete false report. Should check `listCts.IsCancellationRequested` vs srcCts and phase or third based srcCts. Also if OCE thrown for other reason. Fix second `when (listCts.IsCancellationRequested)` but listCts scoped inside try (can restructure) and third src budget. Very strong.
  - On manual continue before try/finally, sw elapsed never set and OnSourceDone not invoked, only OnSourceState. Results UI MainForm won't show manual source result (OnSourceDone not called) but final report includes. Could flag minor.
  - HandleAsync network errors: GetText NetworkDownException throws to abort entire source. DownloadAsync can throw NetworkDownException, IOException etc not caught in HandleAsync, aborts whole source rather than Fail one doc. Source generic catch failed. Could be design to avoid network. But a single doc network timeout stops entire source, remaining unprocessed but r.Found full; status Network and no unattempted count. False? perhaps.
  - `_warmed` shared across runs Harvester instance only one run.

  - gatedInRow resets only on LoginGated difference. If other successful doc between? before same then resets 0 yes.
  - no `OnDoc` for skipped or duplicates except found initial.
  - SavePageAsCanon canonical fallback when RenderUrl succeeds, doc built with unit s.Name topic "" instead of `topic` argument: RenderUrlToPdfAsync does `Document(title, unit, "", date,text)` so kicker Dzherelo, but catalog Topic set. Not important.
  - Canon fallback record status "html" violates allowed status enum ok/suspect/superseded/missing, and no TrustLevel -> not coverage. But Add rejects first. Could mention together.
- CanonPdf:
  - RenderLoop creates webView UserDataFolder unique temp `dzherelo-canvas-xxxx` and never deletes. Every PDF/text render leaves cache dirs. Disk leak. Strong resource.
  - `using var form`, webView not explicitly disposed but form dispose controls should dispose? Control collection disposal when form disposed yes.
  - On cancellation: `ct.Register(() => nav.TrySetCanceled())`, but after navigation and during 5s delay uses Task.Delay without ct, PrintToPdfAsync no ct. If canceled after nav, it still prints and tcs success, cancellation not honored. Worse RenderToPdf Task no timeout for PrintToPdfAsync or ExecuteScriptAsync. Can hang forever; tcs never completes if PrintToPdf hangs, but form stays. `RenderCoreAsync` no WaitAsync overall unlike BrowserSolve. Strong network/timeout.
  - If `form.Load` never fires? Application.Run(form) should.
  - `Task.Delay(60s, ct)` if canceled, done = delay task, then throw TimeoutException instead of OperationCanceledException? nav registration should cancel nav task almost same time; race. If done != nav.Task => TimeoutException "60s" even ct canceled, misreport. Then Harvester catches generic and fallback HTML maybe on cancellation? SavePageAsCanon catches OperationCanceled separately only. Could note not needed.
  - `tcs.TrySetException` then finally form.Close, Application.Run returns. Good.
  - Multiple concurrent renders: each own unique user data folder, EnsureFonts writes shared fonts with no synchronization; File.Create same target race if two tasks. Mostly sequential.
  - EnsureFonts if resource missing or copy fails, target partially created remains and next run sees exists and skips => corrupt font persistent. Could be moderate.
  - RenderUrlToPdfAsync first extracts DOM text then renders another WebView with script disabled; good.
  - ExtractDomTextFromUrl can access remote, no network timeout except navigation 60 sec. After nav, script body innerText immediate.
  - `Task.Delay(textOnly ?5000:250)` fixed wait may yield empty for slow JS, then creates PDF with no body and reports success. No validation nonempty or output file. This is significant false success: canon PDF can be blank; SavePageAsCanon increments New. PrintToPdf true but text blank. Should validate text and PDF size/content.
  - Canon PDF generated content hash will differ each render? same text date etc stable perhaps PDF metadata includes date causing sha changes? NewOnly HasUrl prevents unless force. Force could generate new sha and duplicate content? less.
  - `RenderPageToPdfAsync` raw HTML into NavigateToString with JS enabled for textOnly, arbitrary scripts from remote pages run in WebView and virtual host mapping Allow fonts directory only. External network loads; possible expected.
  - Security not requested except leaks.
- BrowserSolve:
  - Cache and Busy static dictionaries accessed without locks from possibly multiple threads. `CookiesForAsync` initial Cache.TryGetValue outside Gate, Forget Remove, TryBrowser _browserSolved HashSet in Fetcher also possibly across tasks. Harvester sequential per source, but UI could multiple Browser? MainForm only one op. Yet background threads and different fetchers? selftest? Could flag race "сумнівно".

  - `RunAsync`: loop 90*500ms=45sec, plus operations; WaitAsync 120 secs. If timeout/cancellation, returned task canceled but thread continues and WebView form runs until internal end. In RunAsync ct only checked in for loop, then still GetCookies etc. GetTextAsync: `Task.WhenAny(arrived.Task, Task.Delay(45000,ct))` ignores which completed; then Task.Delay(1200,ct) throws if ct canceled. If 45s timeout without ct, continues ExecuteScript on perhaps unfinished nav. Returns whatever challenge page. FetchRaw similarly. On outer WaitAsync timeout, background thread and WebView may live indefinitely if EnsureCoreWebView2Async/ExecuteScriptAsync hangs. Repeated attempts spawn unbounded STA threads/processes and use same user data folder causing locks. Strong.
  - WebView2 environment data same path `dzherelo-webview2/main` across all three methods and concurrent threads. Only one process can use user data folder; simultaneous FetchRaw etc can fail. Gate only CookiesForAsync, other SolvePage/GetText/FetchRaw not gated. Concrete.
  - Form Shown async void and finally Close. If Ensure hangs, no close. Background IsBackground means process exit okay, but resource leak while running.
  - LastUserAgent static set only FetchRaw and RunAsync, not GetTextAsync. TryBrowserAsync calls CookiesForAsync->RunAsync so set. okay.
  - `RunAsync` challenge title returned JSON string e.g. `"Just a moment..."` works. If title null for 45 sec, exits loop, captures html and cookies and considers success if jar count >0 (even no cf_clearance) and Cache stores any cookies. `CookiesForAsync` returns fresh if count>0 and caches. TryBrowserAsync true if any cookie, even challenge not solved. Fetcher may then claim? It may later fail blocked. But cache prevents reattempt, stale bad cookies until Forget. Could flag weak proof.
  - CookieCollection adding domain cookies with leading "." may throw swallowed, okay.
- Fetcher critical:
  - CreateDoh disables certificate validation (`RemoteCertificateValidationCallback = true`) and connects by IP to 1.1.1.1/8.8.8.8. DoH response unauthenticated: MITM can inject IP addresses, then main HTTP TLS still validates hostname so can't redirect unless cert? It can cause availability, not full content compromise due TLS. For http sources could content injection. Flag security? user focus network, not specifically security but yes real.
  - `ResolveAsync` order comment says Pins -> disk cache -> system -> DoH but actual system -> cache -> DoH -> pins. `Pin` for broken DNS only used after DoH. More critical: system DNS success calls Remember and returns possibly stale? expected.
  - `_cache` and `_pins` dictionaries accessed from HttpClient ConnectCallbacks concurrently; no locks. Remember writes JSON from multiple threads possible race/corruption. Harvester sequential mostly, Doh? multiple connections. Could mention.
  - DNS cache persistence: `changed = TryGetValue && old != ip`; if new host old null, condition `(!changed && old != null)` false so write. If same old no write. Good.
  - static Doh HttpClient never disposed (static process lifetime okay).
  - `handler.ConnectCallback`: custom socket with