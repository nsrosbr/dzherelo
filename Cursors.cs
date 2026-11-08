using System.Text.Json;

namespace Dzherelo;

// Курсор джерела: куди дійшов попередній прогін. Перерваний прогін починав джерело
// з початку й заново тягнув уже пройдені сторінки (OAI-журнал може віддавати сторінку
// ~35 с). Курсор зберігає точку відновлення, щоб наступний прогін продовжив з неї.
// Значення — непрозорий рядок: resumptionToken для oai, номер сторінки для dspace/wp.
public sealed class Cursors
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public Cursors(string stateDir)
    {
        Directory.CreateDirectory(stateDir);
        _path = Path.Combine(stateDir, "cursors.json");
        if (!File.Exists(_path)) return;
        try { _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), Opts) ?? new(); }
        catch { _map = new(); }
    }

    public string? Get(string id) => _map.TryGetValue(id, out var v) ? v : null;

    // порожній курсор = джерело пройдено до кінця, точку скидаємо
    public void Set(string id, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) _map.Remove(id);
        else _map[id] = cursor!;
        Save();
    }

    private void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_map, Opts));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch (Exception ex) { Console.WriteLine("  ! курсор не записано: " + ex.Message); }
    }

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var checks = new List<(string, bool, string)>();
        void Check(string name, bool ok, string detail = "") => checks.Add((name, ok, detail));

        var dir = Path.Combine(Path.GetTempPath(), "dtic-cursors-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var c = new Cursors(dir);
            Check("новий стан порожній", c.Get("oai-x") is null, c.Get("oai-x") ?? "-");

            c.Set("oai-x", "resume-token-42");
            Check("записаний курсор читається одразу", c.Get("oai-x") == "resume-token-42", c.Get("oai-x") ?? "-");

            var reload = new Cursors(dir);
            Check("курсор переживає перезапуск", reload.Get("oai-x") == "resume-token-42",
                reload.Get("oai-x") ?? "-");

            c.Set("oai-x", null);
            Check("порожній курсор стирає точку", new Cursors(dir).Get("oai-x") is null);

            c.Set("dspace-y", "7");
            c.Set("wp-z", "13");
            var again = new Cursors(dir);
            Check("кілька джерел не змішуються",
                again.Get("dspace-y") == "7" && again.Get("wp-z") == "13" && again.Get("oai-x") is null,
                $"{again.Get("dspace-y")}/{again.Get("wp-z")}");
        }
        catch (Exception ex)
        {
            Check("selfcheck без винятків", false, ex.Message);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        return checks.ToArray();
    }
}
