using System.Text;

namespace Dzherelo;

// Assert-based: exit 1 means something real broke. The old selftest logged and always returned 0.
public static class Selftest
{
    private static int _failed;

    /// <summary>Головне вікно будується, тема перемикається dark→light, кожен контрол мусить
    /// взяти колір поточної палітри (жодних залишків попередньої).</summary>
    // Рев'ю 2026-11-11: ToJson не писав date/section, FromJson їх не читав — дані губились.
    private static bool CatalogRoundTripKeepsDateAndSection()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dzherelo-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "library"));
            using (var cat = new Catalog(Path.Combine(dir, "library")))
            {
                cat.Add(new DocRecord
                {
                    Url = "https://example.org/x.pdf", Sha = new string('a', 64), Size = 10,
                    Path = "x/y.pdf", Title = "Проба", Date = "2023-05-17", Section = "Уроки війни",
                    Type = "report", Topic = "lohistyka", Lang = "uk", Source = "тест", Status = "ok",
                });
            }
            using var again = new Catalog(Path.Combine(dir, "library"));
            var d = again.All().FirstOrDefault();
            return d is not null && d.Date == "2023-05-17" && d.Section == "Уроки війни";
        }
        catch { return false; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static bool UiThemeConsistent()
    {
        bool ok = true;
        var th = new System.Threading.Thread(() =>
        {
            try
            {
                using var form = new MainForm();
                _ = form.Handle;                       // створюємо нативні ручки
                foreach (var mode in new[] { "dark", "light" })
                {
                    Theme.Init(mode);
                    Theme.Refresh();
                    form.Refresh();

                    var page = Theme.PageBg; var card = Theme.CardBg; var input = Theme.ControlBg;
                    foreach (var c in Walk(form))
                    {
                        switch (c)
                        {
                            case Form f:
                                if (f.BackColor != page) { ok = false; return; } break;
                            case TreeView tv:
                                if (tv.BackColor != input) { ok = false; return; } break;
                            case ListView lv:
                                if (lv.BackColor != input) { ok = false; return; } break;
                            case TextBox tb:
                                if (tb.BackColor != input) { ok = false; return; } break;
                            case NumericUpDown nu:
                                if (nu.BackColor != input) { ok = false; return; } break;
                            case Card cd:
                                if (cd.BackColor != card) { ok = false; return; } break;
                        }
                    }
                }
            }
            catch { ok = false; }
        });
        th.SetApartmentState(System.Threading.ApartmentState.STA);
        th.Start();
        th.Join(30000);
        return ok;
    }

    private static IEnumerable<Control> Walk(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var d in Walk(c)) yield return d; }
    }

    public static int Run()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        var report = new StringBuilder();
        Check("UI: перемикання теми дає один набір кольорів", UiThemeConsistent());
        Check("каталог: date і section переживають перезавантаження", CatalogRoundTripKeepsDateAndSection());

        void Log(string s) { try { Console.WriteLine(s); } catch { } report.AppendLine(s); }
        void Check(string name, bool ok, string detail = "")
        {
            Log((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " — " + detail : ""));
            if (!ok) _failed++;
        }

        Log("== Dzherelo selftest ==");

        var sources = Registry.Load();
        Check("реєстр завантажено", sources.Count > 0, sources.Count + " джерел (локальний файл або вбудований)");

        var shipped = Registry.LoadEmbedded();
        Check("вбудований реєстр", shipped.Count >= 50, shipped.Count + " джерел");
        Check("розділи у вбудованому", shipped.Select(s => s.Section).Distinct().Count() >= 6,
            shipped.Select(s => s.Section).Distinct().Count() + " розділів");
        var badKind = sources.Where(s => s.Kind is not ("oai" or "dspace" or "wp" or "sitemap" or "html" or "static" or "catalog")).Select(s => s.Id).ToList();
        Check("усі мають kind", badKind.Count == 0, string.Join(",", badKind));

        Check("санітизація імені", PathUtil.SanitizeName("a/b:c*?\"<>|") == "a_b_c______", PathUtil.SanitizeName("a/b:c*?\"<>|"));
        Check("ім'я файлу стабільне", Harvester.FileName("Тест", "https://x/y") == Harvester.FileName("Тест", "https://x/y"));
        Check("pdf з query — це файл", Adapters.IsPdfUrl("https://x/a.pdf?v=1"));
        Check("pdf з якорем — це файл", Adapters.IsPdfUrl("https://x/a.pdf#page=2"));
        Check("html не файл", !Adapters.IsPdfUrl("https://x/a.html"));
        Check("pdf у query — це файл", Adapters.IsPdfUrl("https://x/download.php?spec=ADS-10C-SP.003612.pdf"));
        Check("дата зі сторінки", Adapters.PageDate("<meta property=\"article:published_time\" content=\"2025-02-11T09:00:00Z\">") == "2025-02");
        Check("рік з URL", Harvester.YearFromUrl("https://api.army.mil/e2/c/downloads/2026/02/x.pdf") == "2026");
        Check("без року в URL", Harvester.YearFromUrl("https://x/y/z.pdf") == "");
        Check("загальна назва замінюється", Harvester.FileName("file", "https://x/file.pdf", "Біла книга") == "Біла книга");

        var temp = Path.Combine(Path.GetTempPath(), "dtic-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var store = new Catalog(temp))
            {
                store.Add(new DocRecord { Url = "https://Example.org/A/", Sha = "abc", Size = 10, Path = "p", Status = "ok" });
                store.Add(new DocRecord { Url = "https://example.org/x.pdf", PageUrl = "https://example.org/page", Sha = "def", Size = 10, Path = "q" });
                Check("журнал бачить запис одразу", store.HasUrl("https://example.org/a"), "canonical");
                Check("дедуп по sha", store.HasSha("abc"));
                Check("пошук запису за sha", store.BySha("abc")?.Url == "https://Example.org/A/");
                Check("сторінка-джерело пропускається", store.HasUrl("https://example.org/page"));
            }
            using (var reopened = new Catalog(temp))
            {
                Check("журнал переживає перезапуск", reopened.HasUrl("https://example.org/a"), "jsonl reload");
                try
                {
                    var second = new Catalog(temp);
                    second.Dispose();
                    Check("другий екземпляр на тій самій теці відмовляє", false, "відкрився");
                }
                catch (InUseException ex) { Check("другий екземпляр на тій самій теці відмовляє", true, ex.Message); }
            }
        }
        finally { try { Directory.Delete(temp, true); } catch { } }

        using var fetch = new Fetcher { MinIntervalMs = 500 };
        try
        {
            using var resp = fetch.SendAsync("https://www.army.mil/CALL", 1, CancellationToken.None).GetAwaiter().GetResult();
            var code = (int)resp.StatusCode;
            Check("браузерний профіль заголовків проходить .mil-гейт", code == 200, "HTTP " + code);
        }
        catch (Exception ex)
        {
            Check("браузерний профіль заголовків проходить .mil-гейт", false, ex.Message);
        }

        try
        {
            fetch.DownloadAsync("https://www.mces.marines.mil/Portals/239/Docs/Doctrine/15%20May%2026%20DUT.pdf",
                Path.Combine(Path.GetTempPath(), "dtic-404"), "probe", 1, CancellationToken.None).GetAwaiter().GetResult();
            Check("мертве посилання дає NotFoundException", false, "помилки не було");
        }
        catch (NotFoundException ex) { Check("мертве посилання дає NotFoundException", true, ex.Message); }
        catch (Exception ex) { Check("мертве посилання дає NotFoundException", false, ex.GetType().Name); }

        try
        {
            fetch.DownloadAsync("https://apps.dtic.mil/sti/trecms/pdf/AD1206576.pdf",
                Path.Combine(Path.GetTempPath(), "dtic-maint"), "probe", 1, CancellationToken.None).GetAwaiter().GetResult();
            Check("заглушка DTIC розпізнається", false, "помилки не було");
        }
        catch (BlockedException ex) { Check("заглушка DTIC розпізнається", true, ex.Message); }
        catch (Exception ex) { Check("заглушка DTIC розпізнається", false, ex.GetType().Name); }

        // ---- каталог (§6): rev, tombstone, ремонт хвоста, компакція ----
        var tmp = Path.Combine(Path.GetTempPath(), "dtic-cat-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string sha1 = new string('a', 64), sha2 = new string('b', 64), sha3 = new string('c', 64);
            using (var cat = new Catalog(tmp))
            {
                cat.Add(new DocRecord { Sha = sha1, Title = "Перший", Source = "rusi", Type = "report", Year = 2024, Size = 10 });
                cat.Add(new DocRecord { Sha = sha1, Title = "Перший (виправлено)", Source = "rusi", Type = "report", Year = 2024, Size = 11 });
                cat.Add(new DocRecord { Sha = sha2, Title = "Другий", Source = "rusi", Type = "doctrine", Year = 2025, Size = 20 });
                cat.Add(new DocRecord { Sha = sha3, Title = "Третій", Source = "call", Type = "lessons", Year = 2023, Size = 30 });
                Check("каталог: один документ — один запис", cat.Count == 3, cat.Count + " записів");
                var first = cat.BySha(sha1);
                Check("каталог: перемагає остання версія", first != null && first.Title.Contains("виправлено") && first.Rev == 2,
                    "rev=" + (first?.Rev.ToString() ?? "-"));

                var second = cat.BySha(sha2)!;
                cat.Delete(second);
                Check("каталог: tombstone прибирає запис", cat.Count == 2 && cat.BySha(sha2) == null, cat.Count + " записів");
            }

            // рваний хвіст: дописуємо половину лінії й перевіряємо, що каталог її відрізав
            var catalogPath = Path.Combine(tmp, "library", ".state", "catalog.jsonl");
            File.AppendAllText(catalogPath, "{\"schema\":3,\"rev\":99,\"object_id\":\"");
            using (var cat2 = new Catalog(tmp))
            {
                Check("каталог: рваний хвіст відрізано", cat2.Count == 2, cat2.Count + " записів після ремонту");
                cat2.Add(new DocRecord { Sha = sha1, Title = "Після ремонту", Source = "rusi", Type = "report", Year = 2024 });
                Check("каталог: пише після ремонту", cat2.BySha(sha1)!.Title == "Після ремонту");
            }

            var metaPath = Path.Combine(tmp, "library", ".state", "catalog.meta.json");
            var meta = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metaPath))!;
            Check("каталог: meta несе watermark", meta["watermark_rev"] != null && meta["schema"]!.GetValue<int>() == 3,
                meta.ToJsonString());

            // каталог новішої схеми — лише читання
            var newer = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
            newer["schema"] = 99;
            File.WriteAllText(metaPath, newer.ToJsonString());
            using (var ro = new Catalog(tmp))
                Check("каталог новішої схеми — read-only", ro.ReadOnly, ro.ReadOnly ? "запис заборонено" : "запис дозволено");
        }
        catch (Exception ex) { Check("каталог: виняток", false, ex.GetType().Name + ": " + ex.Message); }
        finally { try { Directory.Delete(tmp, true); } catch { } }

        // ---- конвенція імен (§5) ----
        Check("naming: тип лише з семи", Naming.Type("fm") == "other" && Naming.Type("doctrine") == "doctrine");
        Check("naming: невідомий рік = unknown", Naming.Year("") == "unknown" && Naming.Year("2025-12-16") == "2025");
        Check("naming: slug ASCII", Naming.Slug("БНТУ засідки") == "bntu-zasidky", Naming.Slug("БНТУ засідки"));
        Check("naming: назва без сайту",
            Naming.CleanTitle("Defence Futures - GOV.UK") == "Defence Futures", Naming.CleanTitle("Defence Futures - GOV.UK"));
        Check("naming: шлях ≤ 120", Naming.Dir("D:\\lib", "army-pubs", "doctrine", "2025").Length <= 120);

        // ---- незалежні модулі, кожен зі своєю самоперевіркою ----
        foreach (var (name, ok, detail) in new[]
                 {
                     Reacquire.SelfCheck(), FormatGate.SelfCheck(), Secrets.SelfCheck(), AppSettings.SelfCheck(),
                     ReportWriter.SelfCheck(), Editions.SelfCheck(), Topics.SelfCheck(), Cursors.SelfCheck(),
                     AdapterHeal.SelfCheck(), CanonPdf.SelfCheck(), Setup.SelfCheck(), Harvester.SelfCheck()
                 }.SelectMany(x => x))
            Check("модуль: " + name, ok, detail);

        Log(_failed == 0 ? "УСІ ПЕРЕВІРКИ ПРОЙДЕНО" : "ПРОВАЛЕНО: " + _failed);
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-report.txt"), report.ToString()); } catch { }
        return _failed == 0 ? 0 : 1;
    }
}
