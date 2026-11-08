using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dzherelo;

// Logins/keys: DPAPI blob in data\, never in sources.json and never in the release (SPEC §18).
// No logging at all — there is no safe version of a value to log.
public static class Secrets
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dzherelo", "data");

    private static readonly string FilePath = Path.Combine(Dir, "secrets.dat");

    // Not a secret (it ships in the binary); it just stops another app from Unprotect()ing the blob blindly.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dzherelo.secrets.v1");

    private static readonly object Gate = new();

    public static void Set(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("secret name required", nameof(name));
        if (string.IsNullOrEmpty(value)) { Remove(name); return; }

        lock (Gate)
        {
            var all = ReadRaw();
            all[name] = Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            Write(all);
        }
    }

    // null on absent, corrupt or foreign blob — fail closed.
    public static string? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        lock (Gate)
        {
            if (!ReadRaw().TryGetValue(name, out var b64)) return null;
            return Unprotect(b64);
        }
    }

    // "present and usable": an entry that no longer decrypts does not count.
    public static bool Has(string name) => Get(name) is not null;

    public static void Remove(string name)
    {
        if (string.IsNullOrEmpty(name)) return;

        lock (Gate)
        {
            var all = ReadRaw();
            if (all.Remove(name)) Write(all);
        }
    }

    public static string[] Names()
    {
        lock (Gate) return ReadRaw().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var results = new List<(string, bool, string)>();
        void Check(string name, bool ok, string detail = "") => results.Add((name, ok, detail));

        var name = "selftest-" + Guid.NewGuid().ToString("N");
        var value = "S3cret-" + Guid.NewGuid().ToString("N");
        try
        {
            Set(name, value);
            Check("Set→Get повертає те саме", Get(name) == value, "round-trip");
            Check("Has бачить наявний", Has(name));
            Check("Get невідомого = null", Get(name + "-absent") is null, "без винятку");

            var tampered = ReadRaw();
            tampered[name] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            Write(tampered);
            Check("невідомий ключ = null", Get(name) is null, "fail-closed");

            var onDisk = File.ReadAllBytes(FilePath);
            Check("на диску немає відкритого значення",
                !Encoding.UTF8.GetString(onDisk).Contains(value, StringComparison.Ordinal));

            Remove(name);
            Check("Remove прибирає", !Has(name) && Array.IndexOf(Names(), name) < 0);
        }
        catch (Exception ex) { Check("сховище секретів", false, ex.GetType().Name); }
        finally { try { Remove(name); } catch { } }

        return results.ToArray();
    }

    private static string? Unprotect(string b64)
    {
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ReadRaw()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, string>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(FilePath))
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            // Unreadable file: keep the ciphertext around instead of silently overwriting it on the next Set.
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void Write(Dictionary<string, string> all)
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(all)); // no BOM
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
        else File.Move(tmp, FilePath);
    }
}
