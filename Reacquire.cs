using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dzherelo;

public enum QueueState { Queued, Retry, Parked, NeedsHuman, Closed, GaveUp }

public sealed class QueueEvent
{
    public long Seq;
    public string QueueKey = "";
    public string? ObjectId;
    public string Library = "";
    public string Code = "";
    public int Attempts;
    public DateTime FirstSeen;
    public DateTime? LastAttemptAt;
    public DateTime? NextRetryAt;
    public QueueState State;
    public string? ClosedBy;
    public string Note = "";
    public bool IsTerminal => State is QueueState.Closed or QueueState.GaveUp or QueueState.NeedsHuman;

    internal QueueEvent Copy() => (QueueEvent)MemberwiseClone();
}

// Черга дошукування (SPEC §11): append-only JSONL, перемагає остання подія за seq.
// Коди черги живуть тільки тут і ніколи не потрапляють у запис каталогу (§6.7).
public sealed class Reacquire : IDisposable
{
    private static readonly JsonSerializerOptions WriteOpts = new()
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _path;
    private readonly FileStream _append;
    private readonly Dictionary<string, QueueEvent> _byKey = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private long _seq;

    public Reacquire(string stateDir)
    {
        Directory.CreateDirectory(stateDir);
        _path = Path.Combine(stateDir, "reacquire.jsonl");
        RepairTail();
        Load();
        _append = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    // ---------- автомат ----------

    public static QueueState NextState(string code, int attempts)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return c switch
        {
            // §11: «документа немає» — лише коли двічі поспіль не знайшли; soft-404 не має права закрити з першого разу
            "NOT_FOUND" => attempts >= 2 ? QueueState.Closed : RetryOrQueued(attempts),
            "PAYWALL" or "NO_PDF" or "UNVERIFIED" => QueueState.Parked,  // чекає зовнішньої події, сам не повторюється
            "BLOCKED" => QueueState.NeedsHuman,                          // тікет людині, без повторів
            "BAD_FORMAT" => attempts >= 1 ? QueueState.GaveUp : QueueState.Queued,
            "BAD_ENDPOINT" => attempts >= 1 ? QueueState.NeedsHuman : QueueState.Queued,
            "GEO_BLOCKED" => attempts >= 3 ? QueueState.Parked : RetryOrQueued(attempts),
            "RATE_LIMITED" => attempts >= 3 ? QueueState.NeedsHuman : RetryOrQueued(attempts),
            _ => attempts >= 5 ? QueueState.GaveUp : RetryOrQueued(attempts),  // NETWORK і невідомі коди
        };
    }

    private static QueueState RetryOrQueued(int attempts) => attempts <= 0 ? QueueState.Queued : QueueState.Retry;

    // пауза між спробами: 5, 10, 20… хв, стеля 6 год
    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(5 * Math.Pow(2, Math.Max(0, attempts - 1)), 360));

    // ---------- запис ----------

    public QueueEvent Enqueue(string queueKey, string code, string library, string? objectId = null)
    {
        if (string.IsNullOrWhiteSpace(queueKey)) throw new ArgumentException("порожній queue_key", nameof(queueKey));
        lock (_sync)
        {
            // повторний Enqueue не скидає лічильник спроб: інакше NETWORK пробується нескінченно
            if (_byKey.TryGetValue(queueKey, out var cur) && !cur.IsTerminal) return cur;
            return Write(new QueueEvent
            {
                QueueKey = queueKey,
                ObjectId = objectId,
                Library = library,
                Code = Norm(code),
                FirstSeen = cur?.FirstSeen ?? DateTime.UtcNow,
                State = QueueState.Queued,
                Note = cur?.Note ?? "",
            });
        }
    }

    public QueueEvent Attempt(string queueKey, string? code = null)
    {
        lock (_sync)
        {
            var cur = Find(queueKey);
            if (cur.IsTerminal)
                throw new InvalidOperationException($"{queueKey}: стан {StateName(cur.State)} — автоматика стоїть, потрібен Enqueue");

            var now = DateTime.UtcNow;
            var ev = cur.Copy();
            ev.Code = code is { Length: > 0 } ? Norm(code) : cur.Code;
            ev.Attempts = cur.Attempts + 1;
            ev.State = NextState(ev.Code, ev.Attempts);
            ev.LastAttemptAt = now;
            ev.NextRetryAt = ev.State == QueueState.Retry ? now + Backoff(ev.Attempts) : null;
            ev.ClosedBy = ev.State == QueueState.Closed ? "auto" : null;   // NOT_FOUND ставить лише застосунок
            return Write(ev);
        }
    }

    public void Close(string queueKey, string by, string note = "")
    {
        lock (_sync)
        {
            var ev = Find(queueKey).Copy();
            ev.State = QueueState.Closed;
            ev.ClosedBy = string.IsNullOrWhiteSpace(by) ? "user" : by;
            ev.Note = note;
            ev.NextRetryAt = null;
            Write(ev);
        }
    }

    public void Park(string queueKey, string note = "")
    {
        lock (_sync)
        {
            var ev = Find(queueKey).Copy();
            ev.State = QueueState.Parked;
            ev.Note = note;
            ev.NextRetryAt = null;
            ev.ClosedBy = null;
            Write(ev);
        }
    }

    private QueueEvent Write(QueueEvent ev)
    {
        ev.Seq = ++_seq;
        var o = new JsonObject
        {
            ["seq"] = ev.Seq,
            ["queue_key"] = ev.QueueKey,
            ["object_id"] = ev.ObjectId,
            ["library"] = ev.Library,
            ["code"] = ev.Code,
            ["state"] = StateName(ev.State),
            ["attempts"] = ev.Attempts,
            ["first_seen"] = Iso(ev.FirstSeen),
            ["last_attempt_at"] = Iso(ev.LastAttemptAt),
            ["next_retry_at"] = Iso(ev.NextRetryAt),
            ["closed_by"] = ev.ClosedBy,
            ["note"] = ev.Note,
        };
        var bytes = Encoding.UTF8.GetBytes(o.ToJsonString(WriteOpts) + "\n");
        _append.Write(bytes, 0, bytes.Length);
        _append.Flush(true);                 // інакше обрив живлення губить щойно прийняту подію
        _byKey[ev.QueueKey] = ev;
        return ev;
    }

    // ---------- читання ----------

    public QueueEvent? Get(string queueKey)
    {
        lock (_sync) return _byKey.TryGetValue(queueKey, out var ev) ? ev : null;
    }

    public IReadOnlyList<QueueEvent> Pending()
    {
        lock (_sync)
            return _byKey.Values.Where(e => e.State is QueueState.Queued or QueueState.Retry)
                .OrderBy(e => e.Seq).ToList();
    }

    private QueueEvent Find(string queueKey) =>
        _byKey.TryGetValue(queueKey, out var ev) ? ev : throw new KeyNotFoundException("немає в черзі: " + queueKey);

    private void Load()
    {
        if (!File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            QueueEvent? ev;
            try { ev = Parse(line); } catch { continue; }   // зіпсований рядок не має валити весь журнал
            if (ev is null) continue;
            if (!_byKey.TryGetValue(ev.QueueKey, out var cur) || ev.Seq >= cur.Seq) _byKey[ev.QueueKey] = ev;
            if (ev.Seq > _seq) _seq = ev.Seq;
        }
    }

    // shortcut: файл читається в пам'ять цілком — черга маленька; якщо переросте ~10 МБ, шукати хвіст з кінця
    private void RepairTail()
    {
        if (!File.Exists(_path)) return;
        var bytes = File.ReadAllBytes(_path);
        if (bytes.Length == 0 || bytes[^1] == (byte)'\n') return;
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);
        fs.SetLength(Array.LastIndexOf(bytes, (byte)'\n') + 1);
    }

    private static QueueEvent? Parse(string line)
    {
        if (JsonNode.Parse(line) is not JsonObject o) return null;
        var key = Str(o, "queue_key");
        if (key.Length == 0) return null;
        return new QueueEvent
        {
            Seq = Long(o, "seq"),
            QueueKey = key,
            ObjectId = Str(o, "object_id") is { Length: > 0 } id ? id : null,
            Library = Str(o, "library"),
            Code = Str(o, "code"),
            Attempts = (int)Long(o, "attempts"),
            FirstSeen = Date(o, "first_seen") ?? DateTime.UnixEpoch,
            LastAttemptAt = Date(o, "last_attempt_at"),
            NextRetryAt = Date(o, "next_retry_at"),
            State = StateOf(Str(o, "state")),
            ClosedBy = Str(o, "closed_by") is { Length: > 0 } by ? by : null,
            Note = Str(o, "note"),
        };
    }

    private static string Str(JsonObject o, string k) =>
        o[k] is JsonNode n && n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : "";

    private static long Long(JsonObject o, string k) =>
        o[k] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;

    private static DateTime? Date(JsonObject o, string k)
    {
        var s = Str(o, k);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToUniversalTime() : null;
    }

    private static string Norm(string code) => (code ?? "").Trim().ToUpperInvariant();

    private static string? Iso(DateTime? t) => t?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string StateName(QueueState s) => s switch
    {
        QueueState.Retry => "retry",
        QueueState.Parked => "parked",
        QueueState.NeedsHuman => "needs_human",
        QueueState.Closed => "closed",
        QueueState.GaveUp => "gave_up",
        _ => "queued",
    };

    private static QueueState StateOf(string s) => s.ToLowerInvariant() switch
    {
        "retry" => QueueState.Retry,
        "parked" => QueueState.Parked,
        "needs_human" => QueueState.NeedsHuman,
        "closed" => QueueState.Closed,
        "gave_up" => QueueState.GaveUp,
        _ => QueueState.Queued,
    };

    public void Dispose() => _append.Dispose();

    // ---------- перевірка ----------

    public static (string Name, bool Ok, string Detail)[] SelfCheck()
    {
        var checks = new List<(string, bool, string)>();
        void Check(string name, bool ok, string detail = "") => checks.Add((name, ok, detail));

        var dir = Path.Combine(Path.GetTempPath(), "dtic-reacquire-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var q = new Reacquire(dir))
            {
                var first = q.Enqueue("https://a/x", "NETWORK", "army-pubs", "sha-a");
                Check("Enqueue дає queued", first.State == QueueState.Queued && first.Attempts == 0,
                    first.State.ToString());

                QueueEvent? last = null;
                for (var i = 0; i < 5; i++) last = q.Attempt("https://a/x");
                Check("NETWORK: 5 спроб → gave_up",
                    last is { State: QueueState.GaveUp, Attempts: 5 }, last?.State + "/" + last?.Attempts);

                q.Enqueue("https://b/x", "BLOCKED", "army-pubs");
                var blocked = q.Attempt("https://b/x");
                Check("BLOCKED одразу needs_human",
                    blocked.State == QueueState.NeedsHuman && blocked.Attempts == 1, blocked.State.ToString());

                q.Enqueue("https://c/x", "PAYWALL", "army-pubs");
                var pay = q.Attempt("https://c/x");
                Check("PAYWALL одразу parked", pay.State == QueueState.Parked, pay.State.ToString());

                q.Enqueue("https://d/x", "NOT_FOUND", "army-pubs");
                var nf = q.Attempt("https://d/x");
                // §11: закривається двічі, не з першого разу — soft-404 не має викреслювати бібліотеку
            var nf2 = q.Attempt("https://d/x");
            Check("NOT_FOUND → retry після першої спроби",
                    nf.State == QueueState.Retry, nf.State.ToString());
            Check("NOT_FOUND → closed після другої, closed_by=auto",
                    nf2.State == QueueState.Closed && nf2.ClosedBy == "auto", nf2.State + "/" + nf2.ClosedBy);

                q.Enqueue("https://e/x", "GEO_BLOCKED", "army-pubs");
                var geo = q.Attempt("https://e/x");
                Check("GEO_BLOCKED: retry з паузою",
                    geo.State == QueueState.Retry && geo.NextRetryAt is not null, geo.State.ToString());

                q.Enqueue("https://f/x", "NETWORK", "army-pubs");
                var pending = q.Pending();
                Check("Pending — лише queued/retry, у порядку seq",
                    pending.Select(p => p.QueueKey).SequenceEqual(new[] { "https://e/x", "https://f/x" }),
                    string.Join(",", pending.Select(p => p.QueueKey)));

                Check("NextState(BAD_FORMAT,1) == gave_up",
                    NextState("BAD_FORMAT", 1) == QueueState.GaveUp, NextState("BAD_FORMAT", 1).ToString());

                Check("два ключі не змішуються",
                    q.Get("https://c/x") is { State: QueueState.Parked, Attempts: 1 }
                    && q.Get("https://e/x") is { State: QueueState.Retry, Attempts: 1 }
                    && q.Get("https://b/x") is { State: QueueState.NeedsHuman, Attempts: 1 },
                    "pay/geo/blocked — окремі записи");
            }

            // рваний хвіст: обірвана лінія в кінці файлу не має ламати читання
            File.AppendAllText(Path.Combine(dir, "reacquire.jsonl"),
                "{\"seq\":999,\"queue_key\":\"https://z/x\",\"cod", Encoding.UTF8);

            using (var q = new Reacquire(dir))
            {
                Check("перезапуск бачить той самий стан",
                    q.Get("https://a/x") is { State: QueueState.GaveUp, Attempts: 5 }
                    && q.Get("https://d/x")?.ClosedBy == "auto", "jsonl reload");
                Check("рваний хвіст не ламає читання",
                    q.Get("https://z/x") is null && q.Pending().Count == 2,
                    q.Pending().Count + " pending");
            }
        }
        catch (Exception ex)
        {
            Check("selfcheck без винятків", false, ex.Message);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        return checks.ToArray();
    }
}
