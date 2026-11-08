using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinReg = Microsoft.Win32.Registry;

namespace Dzherelo;

public static class Setup
{
    public const string WebView2ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    public const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    public const long MinFreeBytes = 1024L * 1024 * 1024;

    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo");
    public static string AppDir(string version) => Path.Combine(RootDir, "app", version);
    public static string DataDir => Path.Combine(RootDir, "data");
    public static bool Portable => File.Exists(Path.Combine(DataDir, "portable.txt"));

    public static string Version()
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion;
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        }
        catch { }
        return typeof(Setup).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static bool IsSetupExe() =>
        string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "Setup", StringComparison.OrdinalIgnoreCase);

    public static string? WebView2Version()
    {
        foreach (var key in new[] {
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid,
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid })
        {
            var pv = WinReg.LocalMachine.OpenSubKey(key)?.GetValue("pv") as string;
            if (!string.IsNullOrWhiteSpace(pv)) return pv;
        }
        var cu = WinReg.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2ClientGuid)?.GetValue("pv") as string;
        return string.IsNullOrWhiteSpace(cu) ? null : cu;
    }

    public static string? DotNetDesktopVersion()
    {
        try
        {
            var v = WinReg.LocalMachine.OpenSubKey(
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App")
                ?.GetValue("LATEST_VERSION") as string;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    public static bool CanWriteLocalAppData()
    {
        try
        {
            var t = Path.Combine(RootDir, ".wtest-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(RootDir);
            File.WriteAllText(t, "x");
            File.Delete(t);
            return true;
        }
        catch { return false; }
    }

    public static long FreeBytes(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace; }
        catch { return -1; }
    }

    public static bool EnoughSpace(long bytes) => bytes >= MinFreeBytes;

    public static string UninstallString(string exePath) => "\"" + exePath + "\" --uninstall";

    public static bool EnsureWebView2()
    {
        var boot = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Setup.exe");
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            File.WriteAllBytes(boot, hc.GetByteArrayAsync(BootstrapperUrl).GetAwaiter().GetResult());
            var p = Process.Start(new ProcessStartInfo(boot) { UseShellExecute = true });
            p?.WaitForExit();
            try { File.Delete(boot); } catch { }
            if (WebView2Version() is not null) return true;
            Console.WriteLine("  ! WebView2 не зявився після bootstrapper (потрібні права або перезапуск).");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  ! не вдалося докотити WebView2: " + ex.Message);
            return false;
        }
    }

    public static int Run()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        var version = Version();
        Console.WriteLine("== Dzherelo Setup v" + version + " ==");

        var wv = WebView2Version();
        Console.WriteLine("WebView2 Runtime: " + (wv ?? "ВІДСУТНІЙ - докочую"));
        var wvOk = wv != null || EnsureWebView2();

        var dn = DotNetDesktopVersion();
        Console.WriteLine(".NET Desktop Runtime: " + (dn ?? "не знайдено") + " (не потрібен - застосунок self-contained)");

        var write = CanWriteLocalAppData();
        Console.WriteLine("Права запису в %LOCALAPPDATA%: " + (write ? "OK" : "НЕМА"));

        var free = FreeBytes(RootDir);
        var room = EnoughSpace(free);
        Console.WriteLine("Вільне місце: {0:N1} ГБ {1}", free / 1073741824.0, room ? "OK" : "МАЛО (потрібно >=1 ГБ)");

        if (!wvOk) { Console.WriteLine("ЗБІЙ: WebView2 Runtime не встановлено - PDF канону не запрацює."); return 1; }
        if (!write) { Console.WriteLine("ЗБІЙ: немає прав запису в " + RootDir); return 1; }
        if (!room)  { Console.WriteLine("ЗБІЙ: замало місця на диску."); return 1; }

        var targetDir = AppDir(version);
        var targetExe = Path.Combine(targetDir, "Dzherelo.exe");
        try
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(DataDir);
            // Single-file публікація: один exe. Framework-dependent збірка (bin/Release): exe + dll + *.json,
            // інакше встановлений застосунок не стартує — «Dzherelo.dll not found».
            var srcDir = AppContext.BaseDirectory;
            if (string.Equals(Path.GetFullPath(srcDir).TrimEnd('\\'), targetDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Уже встановлено в цій теці — копіювання пропущено: " + targetDir);
            }
            else
            {
            var frameworkDependent = File.Exists(Path.Combine(srcDir, "Dzherelo.dll"));
            if (frameworkDependent)
            {
                foreach (var f in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                    var dst = Path.Combine(targetDir, Path.GetRelativePath(srcDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(f, dst, overwrite: true);
                }
            }
            else
            {
                File.Copy(Environment.ProcessPath!, targetExe, overwrite: true);
            }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ЗБІЙ копіювання: " + ex.Message);
            return 1;
        }
        Console.WriteLine("Встановлено: " + targetExe);

        if (Portable)
        {
            Console.WriteLine("portable.txt знайдено - реєстр (ARP) не змінюється.");
        }
        else
        {
            try
            {
                using var un = WinReg.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Dzherelo");
                un.SetValue("DisplayName", "Dzherelo");
                un.SetValue("DisplayVersion", version);
                un.SetValue("Publisher", "Dzherelo");
                un.SetValue("InstallLocation", targetDir);
                un.SetValue("DisplayIcon", targetExe);
                un.SetValue("UninstallString", UninstallString(targetExe));
                un.SetValue("NoModify", 1, RegistryValueKind.DWord);
                un.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                un.SetValue("EstimatedSize", new FileInfo(targetExe).Length / 1024, RegistryValueKind.DWord);
                Console.WriteLine("ARP: zapisano (HKCU -> Software -> Microsoft -> Windows -> CurrentVersion -> Uninstall -> Dzherelo).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ЗБІЙ запису ARP: " + ex.Message);
                return 1;
            }
        }

        if (!Portable)
        {
            if (CreateShortcuts(targetExe))
                Console.WriteLine("Ярлики: меню «Пуск» і робочий стіл.");
            else
                Console.WriteLine("  ! ярлики не створено (WScript.Shell недоступний).");
        }

        Console.WriteLine("ГОТОВО. Застосунок у " + targetExe);

        // Подвійний клік по Setup.exe: консолі немає, тому без цього користувач бачить «тишу».
        // Показуємо результат і відкриваємо застосунок, як робить нормальний інсталятор.
        if (IsSetupExe())
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    "Dzherelo 2.0.0 встановлено.\n\nТека: " + targetDir +
                    "\nЯрлики: меню «Пуск» і робочий стіл.\n\nВідкрити застосунок?",
                    "Dzherelo — встановлення завершено",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Information);
                System.Diagnostics.Process.Start(targetExe);
            }
            catch { /* якщо GUI недоступний — вже є консольний вивід */ }
        }
        return 0;
    }

    public static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    public static int Uninstall(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        var purge = HasFlag(args, "--purge");
        var removeLibrary = HasFlag(args, "--remove-library");

        Console.WriteLine("== Dzherelo Uninstall v" + Version() + " ==");
        Console.WriteLine(purge
            ? "режим: БЕЗ СЛІДІВ (налаштування, секрети, журнали й кеш підуть геть)"
            : "режим: ЛИШИТИ ДАНІ (програму й запис у реєстрі приберу, дані залишаться)");

        // бібліотеку читаю ДО видалення налаштувань: OutputDir живе в settings.json
        var lib = App.Root(null);
        var appRootPath = Path.Combine(RootDir, "app");
        if (IsUnder(lib, appRootPath))
        {
            Console.WriteLine("ЗБІЙ: бібліотека лежить усередині теки застосунку — видалення заборонене: " + lib);
            return 1;
        }
        if (purge && IsUnder(lib, RootDir))
        {
            Console.WriteLine("ЗБІЙ: --purge стерло б і саму бібліотеку " + lib + ". Винеси її поза " + RootDir);
            return 1;
        }

        RemoveArp();
        RemoveShortcuts();

        var appRoot = Path.Combine(RootDir, "app");
        if (Directory.Exists(appRoot))
        {
            foreach (var ver in Directory.GetDirectories(appRoot))
                TryDeleteTree(ver);
            var okApp = Directory.Exists(appRoot) ? TryDeleteTree(appRoot) : true;
            try { Directory.Delete(appRoot); } catch { }
            Console.WriteLine("Прибрано програму: " + appRoot);
            if (!okApp) { Console.WriteLine("ЗБІЙ: не вдалося прибрати " + appRoot); return 1; }
        }
        else Console.WriteLine("Програми не знайдено в " + appRoot);

        if (purge)
        {
            var okPurge = TryDeleteTree(RootDir);
            Console.WriteLine("Прибрано налаштування й секрети: " + RootDir);
            if (!okPurge) { Console.WriteLine("ЗБІЙ: не все видалено з " + RootDir); return 1; }
            // logs/state бібліотеки тут НЕ видаляються: це чужі дані, а не налаштування застосунку.
            // Вони йдуть лише разом із явним --remove-library (і лише всередині самої бібліотеки).
        }

        if (removeLibrary)
        {
            if (!SafeToDelete(lib))
            {
                Console.WriteLine("ЗБІЙ: не видаляю бібліотеку — підозрілий шлях " + lib);
                return 1;
            }
            TryDeleteTree(lib);
            Console.WriteLine("Прибрано бібліотеку: " + lib);
        }
        else Console.WriteLine("Тека бібліотеки не змінена (потрібен --remove-library): " + lib);

        Console.WriteLine("ГОТОВО.");
        return 0;
    }

    static void RemoveArp()
    {
        try
        {
            WinReg.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Dzherelo", throwOnMissingSubKey: false);
            Console.WriteLine("ARP: запис видалено");
        }
        catch (Exception ex) { Console.WriteLine("! ARP не видалено: " + ex.Message); }
    }

    // не дає видалити корінь диска чи шлях-сам-собі
    static bool IsUnder(string child, string parent) =>
        Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);

    // Дозволяє видаляти лише теку бібліотеки, і ніколи — системні теки, наш застосунок чи його батьків.
    static bool SafeToDelete(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(full)) return false;
        var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\', '/');
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;      // корінь диска
        if (string.Equals(full, RootDir, StringComparison.OrdinalIgnoreCase)) return false;   // сам застосунок
        if (IsUnder(RootDir, full)) return false;                                           // батько застосунку
        // блокуємо лише САМІ системні корені, а не будь-яку теку з цим словом у шляху:
        // застосунок сам живе в %LOCALAPPDATA%, тобто під C:\Users — і це законно.
        string[] banned = {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        foreach (var b in banned)
            if (!string.IsNullOrEmpty(b) && string.Equals(full, b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    static bool TryDeleteTree(string path)
    {
        try { Directory.Delete(path, recursive: true); return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch { }
        // файл-за-файлом: замкнені (сам запущений exe) — на вилучення після перезапуску
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { File.Delete(f); } catch { DelayDelete(f); }
            foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                try { Directory.Delete(d); } catch { }
            try { Directory.Delete(path); } catch { }
            if (Directory.Exists(path))
            {
                Console.WriteLine("  ! не вдалося видалити все в " + path);
                return false;
            }
            return true;
        }
        catch (Exception ex) { Console.WriteLine("  ! видалення " + path + ": " + ex.Message); return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? newName, int flags);

    static void DelayDelete(string path)
    {
        try { MoveFileEx(path, null, 0x4 /*MOVEFILE_DELAY_UNTIL_REBOOT*/); } catch { }
    }


    // Ярлики через WScript.Shell (IDispatch через пізнє зв'язування — без залежності на Interop):
    // меню «Пуск» завжди, робочий стіл — якщо тека існує. Лінукси/без оболонки → просто false.
    public static bool CreateShortcuts(string exePath)
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dzherelo.lnk");
            CreateShortcut(lnk, exePath, "Dzherelo — збір джерел");
            var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desk) && Directory.Exists(desk))
                CreateShortcut(Path.Combine(desk, "Dzherelo.lnk"), exePath, "Dzherelo — збір джерел");
            return true;
        }
        catch { return false; }
    }

    public static void RemoveShortcuts()
    {
        foreach (var p in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dzherelo.lnk"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Dzherelo.lnk"),
                 })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    static void CreateShortcut(string lnk, string target, string desc)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell")!;
        var shell = Activator.CreateInstance(t)!;
        var sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk })!;
        var st = sc.GetType();
        st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target)! });
        st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { desc });
        st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
    }

    static bool ShortcutSelfTest()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "dtic-shortcut-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "t.exe");
            File.WriteAllText(target, "x");
            var lnk = Path.Combine(dir, "t.lnk");
            try
            {
                CreateShortcut(lnk, target, "t");
                return File.Exists(lnk) && new FileInfo(lnk).Length > 0;
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
        catch { return false; }
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var r = new List<(string, bool, string)>();
        void Check(string n, bool ok, string d = "") => r.Add((n, ok, d));

        var v = Version();
        Check("setup: версія не порожня", !string.IsNullOrWhiteSpace(v), v);
        var la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check("setup: ціль per-user у %LOCALAPPDATA%",
            AppDir("2.0.0").StartsWith(la, StringComparison.OrdinalIgnoreCase), AppDir("2.0.0"));
        Check("setup: дані окремо від програми", !AppDir("x").StartsWith(DataDir, StringComparison.OrdinalIgnoreCase));
        var exe = Path.Combine(AppDir("2.0.0"), "Dzherelo.exe");
        Check("setup: ARP UninstallString", UninstallString(exe) == "\"" + exe + "\" --uninstall");
        Check("setup: поріг місця", EnoughSpace(MinFreeBytes) && !EnoughSpace(MinFreeBytes - 1));
        Check("setup: WebView2 GUID стабільний", WebView2ClientGuid == "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");

        Check("uninstall: --purge розпізнається",
            HasFlag(new[] { "--uninstall", "--purge" }, "--purge") && !HasFlag(new[] { "--uninstall" }, "--purge"));
        Check("uninstall: --remove-library — окреме підтвердження",
            !HasFlag(new[] { "--uninstall", "--purge" }, "--remove-library"));
        Check("uninstall: відмова видаляти корінь диска",
            !SafeToDelete(Path.GetPathRoot(Environment.SystemDirectory)!) && SafeToDelete(Path.Combine(RootDir, "app")));
        Check("setup: ярлик створюється (WScript.Shell)", ShortcutSelfTest());

        return r.ToArray();
    }
}
