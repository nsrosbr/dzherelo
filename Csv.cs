using System.Text;

namespace Dzherelo;

// Minimal RFC4180-style CSV helpers (no external dependency).
public static class Csv
{
    public static string Quote(string f) =>
        f.Contains(',') || f.Contains('"') || f.Contains('\n') || f.Contains('\r')
            ? "\"" + f.Replace("\"", "\"\"") + "\""
            : f;

    public static string WriteRow(IEnumerable<string> cells) => string.Join(",", cells.Select(Quote));

    public static async Task WriteAsync(string path, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(WriteRow(row));
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    // shortcut: treats the file as line-based; a field containing a raw newline is unsupported.
    public static List<string[]> ReadAll(string path)
    {
        var rows = new List<string[]>();
        if (!File.Exists(path))
            return rows;
        foreach (var line in File.ReadAllLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
                rows.Add(ParseLine(line));
        }
        return rows;
    }

    public static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQ = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQ = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}