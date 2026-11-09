using System.Runtime.InteropServices;

namespace Dzherelo;

public static class App
{
    public static string Root(string? fromArgs)
    {
        if (!string.IsNullOrWhiteSpace(fromArgs)) return Path.GetFullPath(fromArgs!);
        var s = AppSettings.Load();
        if (!string.IsNullOrWhiteSpace(s.OutputDir)) return Path.GetFullPath(s.OutputDir!);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DTIC");
    }
}

internal static class Program
{
    /// <summary>Версія зі збірки (2.0.0).</summary>
    public static string Version => Updater.Current.ToString(3);

    /// <summary>Іконка застосунку — та сама, що вшита в exe.</summary>
    public static Icon? AppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { return null; }
    }

    private const string Usage = """
        Dzherelo — збирач відкритих документів

        Вікно:      Dzherelo.exe
        Встановлення: Dzherelo.exe --setup   (або Setup.exe)
        Видалення:  --uninstall [--purge] [--remove-library]

        Збір:       --collect --out <тека> [--sources all|id,id] [--max N] [--since РРРР] [--budget хв] [--rps N] [--force]
        Огляд:      --list      перелік бібліотек із sources.json
        Теми:       --topics [тека]   покриття тем із каталогу
        Міграція:   --reindex <тека>   дерево → каталог
        Перекладка: --relayout <тека>  вирівняти дерево за конвенцією
        Канон:      --canon <файл.html> [--out файл.pdf]
        Самоперевірка: --selftest
        Довідка:    --help

        Коди виходу: 0 гаразд · 1 джерело впало · 2 нічого не вибрано / поганий ключ · 3 тека зайнята · 130 зупинено людиною
        """;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)   // CLI-режим: консольна кодування UTF-8, інакше кирилиця в cmd стає «?????»
        {
            AttachConsole(-1);
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        }
        if (args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            return Setup.Run();
        }
        if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("/?", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            Console.WriteLine(Usage);
            return 0;
        }
        if (args.Length > 0 && (args[0].StartsWith("--") || args[0].StartsWith("/")))
        {
            string[] known = { "--setup", "--install", "--uninstall", "--selftest", "--collect", "--list",
                               "--relayout", "--probe", "--reindex", "--revert-adapter", "--topics", "--canon", "--help", "-h", "/?", "--set-secret", "--get-secret-check", "--secret-b64", "--update", "--check", "--force", "--about", "--preview", "--models", "--judge", "--model", "--fetch", "--import" };
            if (!known.Contains(args[0], StringComparer.OrdinalIgnoreCase))
            {
                AttachConsole(-1);
                Console.WriteLine("Невідомий ключ: " + args[0]);
                Console.WriteLine(Usage);
                return 2;
            }
        }
        if (args.Any(a => a.Equals("--set-secret", StringComparison.OrdinalIgnoreCase)))
        {
            // без AttachConsole: вивід має йти в pipe, інакше git-credential helper нічого не читає
            var i = Array.FindIndex(args, a => a.Equals("--set-secret", StringComparison.OrdinalIgnoreCase));
            var name = i + 1 < args.Length ? args[i + 1] : "";
            var value = Console.In.ReadToEnd().Trim();     // значення зі stdin: не потрапляє в історію команд
            if (string.IsNullOrWhiteSpace(name) || value.Length == 0)
            {
                Console.WriteLine("Вжиток: echo <значення> | Dzherelo.exe --set-secret <імʼя>");
                return 2;
            }
            Secrets.Set(name, value);
            Console.WriteLine($"{name}: збережено в DPAPI (CurrentUser), {value.Length} символів");
            return 0;
        }
        if (args.Any(a => a.Equals("--get-secret-check", StringComparison.OrdinalIgnoreCase)))
        {
            // без AttachConsole: вивід має йти в pipe, інакше git-credential helper нічого не читає
            var name = args.Length > 1 ? args[1] : "";
            var v = Secrets.Get(name);
            Console.WriteLine(v is null ? $"{name}: немає" : $"{name}: є, {v.Length} символів");
            return 0;
        }
        if (args.Any(a => a.Equals("--update", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
            try
            {
                var m = Updater.FetchManifestAsync().GetAwaiter().GetResult();
                Console.WriteLine($"поточна: {Updater.Current} | у каналі: {m.Version}");
                var force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
                if (!Updater.IsNewer(m) && !force) { Console.WriteLine("оновлення не потрібне"); return 0; }
                Console.WriteLine($"є новіша: {m.Version} — {m.Notes}");
                if (check) return 0;
                Console.WriteLine(Updater.ApplyAsync(m).GetAwaiter().GetResult());
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("ЗБІЙ оновлення: " + ex.Message); return 1; }
        }
        if (args.Any(a => a.Equals("--secret-b64", StringComparison.OrdinalIgnoreCase)))
        {
            // без AttachConsole: вивід має йти в pipe, інакше git-credential helper нічого не читає
            var name = args.Length > 1 ? args[1] : "";
            var v = Secrets.Get(name);
            if (v is null) return 2;
            Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(v)));
            return 0;
        }
        if (args.Any(a => a.Equals("--judge", StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = AppSettings.Load();
            var model = args.IndexOf("--model") is var mi and >= 0 && mi + 1 < args.Length ? args[mi + 1] : cfg.Model;
            if (!Judge.Ready) { Console.WriteLine("немає ключа OpenRouter"); return 2; }
            if (string.IsNullOrWhiteSpace(model)) { Console.WriteLine("не вибрана модель: --model <id> або Параметри"); return 2; }

            var ji = Array.FindIndex(args, a => a.Equals("--judge", StringComparison.OrdinalIgnoreCase));
            var limit = ji >= 0 && ji + 1 < args.Length && int.TryParse(args[ji + 1], out var n) ? n : 3;

            var jroot = App.Root(null);
            using var jstore = new Catalog(jroot);
            var targets = jstore.All().Where(d => d.Type == "other" || string.IsNullOrWhiteSpace(d.Topic)).Take(limit).ToList();
            Console.WriteLine("модель: " + model + " | кандидатів: " + targets.Count);
            int ok = 0;
            foreach (var d in targets)
            {
                var j = Judge.ClassifyAsync(model, string.IsNullOrWhiteSpace(d.Title) ? Path.GetFileNameWithoutExtension(d.Path) : d.Title, d.Path, default).GetAwaiter().GetResult();
                if (j == null) { Console.WriteLine("  ? " + d.Title); continue; }
                d.Type = j.Type; d.Topic = j.Topic == "none" ? "" : j.Topic; d.Lang = j.Lang;
                jstore.Add(d); ok++;
                Console.WriteLine($"  ✓ {d.Title} → {j.Type} / {(d.Topic.Length == 0 ? "—" : d.Topic)} / {j.Lang} (впевненість {j.Confidence:0.00})");
            }
            Console.WriteLine("застосовано: " + ok);
            return 0;
        }

        // Підключити наявну теку: індексуємо на місці, без копіювання
        if (args.Any(a => a.Equals("--import", StringComparison.OrdinalIgnoreCase)))
        {
            var ii = Array.FindIndex(args, a => a.Equals("--import", StringComparison.OrdinalIgnoreCase));
            if (ii < 0 || ii + 1 >= args.Length) { Console.WriteLine("--import <тека> [--out <бібліотека>]"); return 2; }
            var folder = args[ii + 1];
            if (!Directory.Exists(folder)) { Console.WriteLine("немає теки: " + folder); return 2; }

            var oi2 = Array.FindIndex(args, a => a.Equals("--out", StringComparison.OrdinalIgnoreCase));
            var root = oi2 >= 0 && oi2 + 1 < args.Length ? Path.GetFullPath(args[oi2 + 1]) : App.Root(null);
            Directory.CreateDirectory(root);
            using var cat = new Catalog(root);
            Console.WriteLine("імпорт: " + folder + " → " + root);
            var res = ImportInPlace.Run(folder, cat, (seen, added, known) =>
                Console.WriteLine($"  {seen} оброблено · нових {added} · уже було {known}"), default);
            Console.WriteLine($"готово: нових {res.Added}, уже в каталозі {res.Known}, відсіяно {res.Skipped}");
            foreach (var e in res.Errors.Take(5)) Console.WriteLine("  ! " + e);
            return 0;
        }

        // діагностика: узяти одну адресу тим самим шляхом, що й рушій (з обходом, якщо треба)
        if (args.Any(a => a.Equals("--fetch", StringComparison.OrdinalIgnoreCase)))
        {
            var fi = Array.FindIndex(args, a => a.Equals("--fetch", StringComparison.OrdinalIgnoreCase));
            if (fi < 0 || fi + 1 >= args.Length) { Console.WriteLine("--fetch <url> [--out файл]"); return 2; }
            var target = args[fi + 1];
            var oi = Array.FindIndex(args, a => a.Equals("--out", StringComparison.OrdinalIgnoreCase));
            using var f = new Fetcher(Path.Combine(Path.GetTempPath(), "dzherelo-fetch"));
            string body;
            try { body = f.GetTextAsync(target, 1, default).GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.WriteLine("не вдалося: " + ex.GetType().Name + " — " + ex.Message); return 1; }
            Console.WriteLine("отримано: " + body.Length + " символів");
            if (oi >= 0 && oi + 1 < args.Length) { File.WriteAllText(args[oi + 1], body); Console.WriteLine("→ " + args[oi + 1]); }
            else Console.WriteLine(body.Length > 400 ? body[..400] : body);
            return 0;
        }

        if (args.Any(a => a.Equals("--models", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Judge.Ready) { Console.WriteLine("ключа OpenRouter немає — Параметри → «Ключі й доступи»"); return 2; }
            var mi = Array.FindIndex(args, a => a.Equals("--models", StringComparison.OrdinalIgnoreCase));
            var filter = mi >= 0 && mi + 1 < args.Length && !args[mi + 1].StartsWith("--") ? args[mi + 1] : "";
            var list = Judge.ModelsAsync(default).GetAwaiter().GetResult();
            var shown = string.IsNullOrWhiteSpace(filter)
                ? list.Take(15).ToList()
                : list.Where(m => m.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
            Console.WriteLine("моделей доступно: " + list.Count + (filter.Length > 0 ? " | збігів «" + filter + "»: " + shown.Count : ""));
            foreach (var m in shown) Console.WriteLine("  " + m);
            return 0;
        }

        // Огляд джерела без завантаження — те саме, що кнопка «Оглянути джерело» у вікні.
        if (args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase)))
        {
            var i = Array.FindIndex(args, a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
            var id = i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
            var root = App.Root(null);
            Directory.CreateDirectory(root);
            var src = Registry.Load().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (src == null) { Console.WriteLine("немає джерела: " + id); return 2; }
            var saveIdx = Array.FindIndex(args, a => a.Equals("--save-html", StringComparison.OrdinalIgnoreCase));
            if (saveIdx >= 0 && saveIdx + 1 < args.Length)
            {
                using var f = new Fetcher(Path.Combine(root, "state"));
                var text = f.GetTextAsync(src.Url, src.Rps > 0 ? src.Rps : 1, default).GetAwaiter().GetResult();
                File.WriteAllText(args[saveIdx + 1], text);
                Console.WriteLine("html: " + text.Length + " символів → " + args[saveIdx + 1]);
                return 0;
            }

            using var cat = new Catalog(root);
            var h = new Harvester { SinceYear = AppSettings.Load().SinceYear, OnLog = Console.WriteLine };
            var docs = h.PreviewAsync(src, cat, default).GetAwaiter().GetResult();
            Console.WriteLine("джерело: " + src.Name + " | документів: " + docs.Count);
            foreach (var d in docs.Take(12))
                Console.WriteLine("  " + (string.IsNullOrWhiteSpace(d.Title) ? "(без назви)" : d.Title.Trim()) + " — " + d.Url);
            return 0;
        }

        if (args.Any(a => a.Equals("--about", StringComparison.OrdinalIgnoreCase)))
        {
            Theme.Init(AppSettings.Load().UiTheme);
            ApplicationConfiguration.Initialize();
            Application.Run(new AboutForm());
            return 0;
        }

        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            return Selftest.Run();
        }
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            return Setup.Uninstall(args);
        }
        if (args.Any(a => a.Equals("--collect", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--list", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--relayout", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--probe", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--reindex", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--revert-adapter", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--topics", StringComparison.OrdinalIgnoreCase)
                          || a.Equals("--canon", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(-1);
            return CollectCli.Run(args);
        }

        // Setup.exe — той самий exe під installer-ім'ям: без прапорців запускає встановлення.
        if (args.Length == 0 && Setup.IsSetupExe())
        {
            AttachConsole(-1);
            return Setup.Run();
        }

        // Один екземпляр: друга копія виходить одразу, не відкриваючи друге вікно.
        using var mutex = new Mutex(initiallyOwned: true, "Dzherelo.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Dzherelo вже запущено (знайди його в панелі завдань).", "Dzherelo",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        Theme.Init(AppSettings.Load().UiTheme);   // тема з параметрів, як у Valera
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
