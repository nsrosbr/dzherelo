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
                               "--relayout", "--probe", "--reindex", "--revert-adapter", "--topics", "--canon", "--help", "-h", "/?", "--set-secret", "--get-secret-check" };
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
            AttachConsole(-1);
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
            AttachConsole(-1);
            var name = args.Length > 1 ? args[1] : "";
            var v = Secrets.Get(name);
            Console.WriteLine(v is null ? $"{name}: немає" : $"{name}: є, {v.Length} символів");
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

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
