
namespace Dzherelo;

// Підключити теку, яка вже є на диску: запис у каталог БЕЗ копіювання файлів.
// Для 41 ГБ це єдиний здоровий шлях — копія з'їла б стільки ж місця.
public static class ImportInPlace
{
    public sealed record Result(int Added, int Known, int Skipped, List<string> Errors);

    public static Result Run(string folder, Catalog store, Action<int, int, int>? progress = null, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".djvu", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int added = 0, known = 0, skipped = 0;
        var errors = new List<string>();
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            try
            {
                var gate = FormatGate.Check(file, null, null);
                if (!gate.Ok) { skipped++; continue; }

                var sha = Harvester.ShaOfAsync(file, ct).GetAwaiter().GetResult();
                if (store.HasSha(sha)) { known++; continue; }

                var title = Naming.CleanTitle(Path.GetFileNameWithoutExtension(file));
                if (title.Length == 0) title = "document";
                var date = File.GetLastWriteTime(file).ToString("yyyy-MM-dd");
                var magic = FormatGate.Magic(file);
                store.Add(new DocRecord
                {
                    Url = "file:///" + file.Replace('\\', '/'),
                    Sha = sha,
                    Size = new FileInfo(file).Length,
                    Path = file,                       // абсолютний шлях: файл лишається на місці
                    OrigName = Path.GetFileName(file),
                    Title = title, Date = date, Type = "other", Topic = Harvester.TopicOf(title),
                    Source = "Імпорт на місці", Section = "імпорт", Status = "ok",
                    Method = "in_place", ChannelId = "import",
                    RetrievedAt = DateTime.UtcNow.ToString("o"),
                    Magic = magic, Mime = FormatGate.MimeOf(magic), TrustLevel = "L2",
                    EditionKey = Editions.Key(null, title, int.TryParse(Naming.Year(date), out var y) ? y : null, sha),
                });
                added++;
            }
            catch (Exception ex) { if (errors.Count < 20) errors.Add(Path.GetFileName(file) + ": " + ex.Message); skipped++; }

            if (progress != null && (i % 50 == 0 || i == files.Count - 1)) progress(i + 1, added, known);
        }
        return new Result(added, known, skipped, errors);
    }
}
