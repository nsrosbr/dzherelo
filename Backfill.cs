
namespace Dzherelo;

// Рев'ю 2026-11-11: date/section не писались у каталог — у наявних записів дата порожня.
// Відновлюємо з файлу (остання зміна), інакше — з року в назві/шляху.
public static class Backfill
{
    public static (int filled, int skipped) Dates(Catalog store, Action<string> log)
    {
        int filled = 0, skipped = 0;
        foreach (var d in store.All().ToList())
        {
            if (!string.IsNullOrWhiteSpace(d.Date)) { skipped++; continue; }

            string date = "";
            try
            {
                var full = Path.IsPathRooted(d.Path) ? d.Path : Path.Combine(store.Library, d.Path);
                if (File.Exists(full)) date = File.GetLastWriteTime(full).ToString("yyyy-MM-dd");
            }
            catch { }

            if (date.Length == 0)
            {
                var y = Harvester.YearFromUrl(d.Path) is { Length: 4 } yy ? yy
                      : (Naming.Year(d.Title) is { Length: 4 } yt ? yt : "");
                if (y.Length == 4) date = y;
            }

            if (date.Length == 0) { skipped++; continue; }
            d.Date = date;
            if (string.IsNullOrWhiteSpace(d.Section)) d.Section = "імпорт";
            store.Add(d);
            filled++;
        }
        log($"дати відновлено: {filled}, без змін: {skipped}");
        return (filled, skipped);
    }

    // Форма документа для всіх записів — правило з назви, без виклику моделі.
    public static int Forms(Catalog store, Action<string> log)
    {
        int n = 0;
        foreach (var d in store.All().ToList())
        {
            var f = Naming.FormOf(string.IsNullOrWhiteSpace(d.Title) ? d.Path : d.Title);
            if (f.Length == 0 || f == d.Form) continue;
            d.Form = f;
            store.Add(d);
            n++;
        }
        log($"форм проставлено: {n}");
        return n;
    }
}
