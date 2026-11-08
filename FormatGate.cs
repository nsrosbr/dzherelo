using System.Text;

namespace Dzherelo;

public readonly record struct FormatVerdict(bool Ok, string Code, string Detail);

// Перед-гейт формату (SPEC §11): HTML-сторінка з кодом 200 не має стати «документом».
// Порівнюються байти, а не заголовок Content-Type — сервер бреше частіше, ніж файл.
public static class FormatGate
{
    private const string Ole = "\u00D0\u00CF";
    private static readonly string[] Ooxml = { ".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".xltx", ".pptx", ".pptm", ".potx" };

    public static FormatVerdict Check(string path, string? declaredMime, long? contentLength)
    {
        long size;
        byte[] head;
        try
        {
            var fi = new FileInfo(path);
            size = fi.Length;
            head = size == 0 ? Array.Empty<byte>() : Head(path, 8);
        }
        catch (Exception ex)
        {
            return new(false, "BAD_FORMAT", $"читання: {ex.Message}");
        }

        if (size == 0) return new(false, "BAD_FORMAT", "порожній");
        if (contentLength is long want && want > size)
            return new(false, "TRUNCATED", $"недокачано: {size} із {want}");

        var mime = (declaredMime ?? "").Trim().ToLowerInvariant();
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return Sniff(head) switch
        {
            "%PDF" => new(true, "", "pdf"),
            "<!DO" => new(false, "BAD_FORMAT", "сторінка замість файла"),
            "PK" => mime == "application/pdf" && !Ooxml.Contains(ext)
                ? new(false, "BAD_FORMAT", "zip-обгортка замість pdf")
                : new(true, "", "zip"),
            Ole => new(true, "", "msword"),
            _ => new(false, "BAD_FORMAT", "невідомий формат")
        };
    }

    public static string Magic(string path)
    {
        try { return Sniff(Head(path, 8)); }
        catch { return ""; }
    }

    public static string MimeOf(string magic) => magic switch
    {
        "%PDF" => "application/pdf",
        "PK" => "application/zip",
        "<!DO" => "text/html",
        Ole => "application/msword",
        _ => ""
    };

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var list = new List<(string, bool, string)>();
        var dir = Path.Combine(Path.GetTempPath(), "dtic-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        void Say(string name, bool ok, string detail) => list.Add((name, ok, detail));
        void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(dir, name), bytes);
        string P(string name) => Path.Combine(dir, name);

        try
        {
            Write("a.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n%\u00E2\u00E3\n1 0 obj\n"));
            var pdf = Check(P("a.pdf"), "application/pdf", null);
            Say("справжній PDF проходить", pdf.Ok && pdf.Code == "", pdf.Code + pdf.Detail);

            Write("lie.html", Encoding.ASCII.GetBytes("%PDF-1.4\n"));
            var lie = Check(P("lie.html"), "text/html", null);
            Say("PDF-байти перемагають заголовок text/html", lie.Ok, lie.Code + lie.Detail);

            Write("b.pdf", Encoding.ASCII.GetBytes("<!DOCTYPE html><html><body>404</body></html>"));
            var html = Check(P("b.pdf"), "application/pdf", null);
            Say("HTML під іменем .pdf не проходить", !html.Ok && html.Code == "BAD_FORMAT" && html.Detail.Contains("сторінка"), html.Detail);

            Write("empty.pdf", Array.Empty<byte>());
            var empty = Check(P("empty.pdf"), "application/pdf", 0);
            Say("порожній не проходить", !empty.Ok && empty.Detail.Contains("порожній"), empty.Detail);

            Write("cut.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n"));
            var cut = Check(P("cut.pdf"), "application/pdf", 5000);
            Say("обрізаний дає TRUNCATED", cut.Code == "TRUNCATED" && cut.Detail.Contains("недокачано"), cut.Detail);

            Write("c.pdf", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 });
            var zip = Check(P("c.pdf"), "application/pdf", null);
            Say("ZIP без OOXML не проходить", !zip.Ok && zip.Detail.Contains("zip"), zip.Detail);

            Write("d.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 });
            var docx = Check(P("d.docx"), "application/pdf", null);
            Say("ZIP з OOXML-розширенням проходить", docx.Ok, docx.Code + docx.Detail);

            bool noThrow;
            string missing;
            try
            {
                var r = Check(P("nope.pdf"), "application/pdf", null);
                noThrow = !r.Ok && r.Detail.Length > 0;
                missing = r.Detail;
            }
            catch (Exception ex)
            {
                noThrow = false;
                missing = ex.GetType().Name;
            }
            Say("відсутній файл не кидає виняток", noThrow, missing);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        return list.ToArray();
    }

    private static byte[] Head(string path, int n)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[n];
        int read = 0, k;
        while (read < n && (k = fs.Read(buf, read, n - read)) > 0) read += k;
        return read == n ? buf : buf[..read];
    }

    private static string Sniff(byte[] h)
    {
        if (Starts(h, 0, "%PDF")) return "%PDF";

        // PK\x03\x04 — файл, PK\x05\x06 — порожній архів
        if (h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B &&
            ((h[2] == 0x03 && h[3] == 0x04) || (h[2] == 0x05 && h[3] == 0x06))) return "PK";

        if (h.Length >= 4 && h[0] == 0xD0 && h[1] == 0xCF && h[2] == 0x11 && h[3] == 0xE0) return Ole;

        var at = Starts(h, 0, "\u00EF\u00BB\u00BF") ? 3 : 0;   // BOM
        while (at < h.Length && (h[at] == ' ' || h[at] == '\r' || h[at] == '\n' || h[at] == '\t')) at++;
        foreach (var tag in new[] { "<!do", "<htm" })
            if (StartsIgnoreCase(h, at, tag)) return "<!DO";

        return "";
    }

    private static bool Starts(byte[] h, int at, string ascii)
    {
        if (h.Length - at < ascii.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
            if (h[at + i] != (byte)ascii[i]) return false;
        return true;
    }

    private static bool StartsIgnoreCase(byte[] h, int at, string ascii)
    {
        if (h.Length - at < ascii.Length) return false;
        for (var i = 0; i < ascii.Length; i++)
        {
            var c = char.ToLowerInvariant((char)h[at + i]);
            if (c != ascii[i]) return false;
        }
        return true;
    }
}
