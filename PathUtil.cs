using System.Text;

namespace Dzherelo;

public static class PathUtil
{
    private static readonly HashSet<char> Forbidden = new("\\/:*?\"<>|".ToCharArray());

    // Replaces only the Windows-forbidden characters (\\ / : * ? " < > |).
    public static string SanitizeName(string name, string fallback = "_")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Forbidden.Contains(c) || c < 32 ? '_' : c);
        var result = sb.ToString().Trim().TrimEnd('.', ' ');
        return result.Length == 0 ? fallback : result;
    }
}