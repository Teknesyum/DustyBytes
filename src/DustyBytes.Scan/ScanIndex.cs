using DustyBytes.Core;
using Microsoft.Data.Sqlite;

namespace DustyBytes.Scan;

public sealed record ScanIndexEntry(long Id, string Root, string Method, DateTimeOffset FinishedAt, long Files, long Directories, long Size, UsnCursor? Usn);

public sealed class ScanIndex
{
    private const int SchemaVersion = 1;

    public ScanIndex(string? path = null)
    {
        Path = path ?? DefaultPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var db = Open();
        Exec(db, "PRAGMA journal_mode=WAL;");
        Exec(db, $"""
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            INSERT OR IGNORE INTO meta VALUES ('schema', '{SchemaVersion}');
            CREATE TABLE IF NOT EXISTS scans (
                id INTEGER PRIMARY KEY,
                root TEXT NOT NULL UNIQUE COLLATE NOCASE,
                method TEXT NOT NULL,
                finished_at INTEGER NOT NULL,
                elapsed_ms INTEGER NOT NULL,
                files INTEGER NOT NULL,
                directories INTEGER NOT NULL,
                size INTEGER NOT NULL,
                cancelled INTEGER NOT NULL,
                errors TEXT NOT NULL,
                usn_journal INTEGER,
                usn_next INTEGER
            );
            CREATE TABLE IF NOT EXISTS nodes (
                scan_id INTEGER NOT NULL,
                id INTEGER NOT NULL,
                parent INTEGER NOT NULL,
                name TEXT NOT NULL,
                is_dir INTEGER NOT NULL,
                size INTEGER NOT NULL,
                logical INTEGER NOT NULL,
                cloud INTEGER NOT NULL,
                write INTEGER NOT NULL,
                newest INTEGER NOT NULL,
                file_count INTEGER NOT NULL,
                flags INTEGER NOT NULL,
                tag TEXT,
                PRIMARY KEY (scan_id, id)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS folder_history (
                root TEXT NOT NULL COLLATE NOCASE,
                day INTEGER NOT NULL,
                taken_at INTEGER NOT NULL,
                path TEXT NOT NULL,
                size INTEGER NOT NULL,
                PRIMARY KEY (root, day, path)
            ) WITHOUT ROWID;
            """);
    }

    public static string DefaultPath => System.IO.Path.Combine(Paths.AppData, "index.db");

    public string Path { get; }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static void Exec(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    public long Save(ScanResult result, DateTimeOffset? at = null)
    {
        using var db = Open();
        Exec(db, "PRAGMA synchronous=NORMAL;");
        using var tx = db.BeginTransaction();
        var root = result.Root.Name;
        using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM nodes WHERE scan_id IN (SELECT id FROM scans WHERE root = $r); DELETE FROM scans WHERE root = $r;";
            del.Parameters.AddWithValue("$r", root);
            del.ExecuteNonQuery();
        }
        long scanId;
        using (var ins = db.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO scans (root, method, finished_at, elapsed_ms, files, directories, size, cancelled, errors, usn_journal, usn_next)
                VALUES ($root, $method, $fin, $el, $files, $dirs, $size, $can, $err, $uj, $un) RETURNING id;
                """;
            ins.Parameters.AddWithValue("$root", root);
            ins.Parameters.AddWithValue("$method", result.Method);
            ins.Parameters.AddWithValue("$fin", result.FinishedAt.UtcTicks);
            ins.Parameters.AddWithValue("$el", (long)result.Elapsed.TotalMilliseconds);
            ins.Parameters.AddWithValue("$files", result.Files);
            ins.Parameters.AddWithValue("$dirs", result.Directories);
            ins.Parameters.AddWithValue("$size", result.Root.Size);
            ins.Parameters.AddWithValue("$can", result.Cancelled ? 1 : 0);
            ins.Parameters.AddWithValue("$err", string.Join('\n', result.Errors));
            ins.Parameters.AddWithValue("$uj", result.Usn is { } u1 ? (long)u1.JournalId : DBNull.Value);
            ins.Parameters.AddWithValue("$un", result.Usn is { } u2 ? u2.NextUsn : DBNull.Value);
            scanId = (long)ins.ExecuteScalar()!;
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO nodes (scan_id, id, parent, name, is_dir, size, logical, cloud, write, newest, file_count, flags, tag)
            VALUES ($s, $id, $p, $n, $d, $sz, $l, $c, $w, $nw, $fc, $f, $t);
            """;
        var pS = cmd.Parameters.Add("$s", SqliteType.Integer);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pP = cmd.Parameters.Add("$p", SqliteType.Integer);
        var pN = cmd.Parameters.Add("$n", SqliteType.Text);
        var pD = cmd.Parameters.Add("$d", SqliteType.Integer);
        var pSz = cmd.Parameters.Add("$sz", SqliteType.Integer);
        var pL = cmd.Parameters.Add("$l", SqliteType.Integer);
        var pC = cmd.Parameters.Add("$c", SqliteType.Integer);
        var pW = cmd.Parameters.Add("$w", SqliteType.Integer);
        var pNw = cmd.Parameters.Add("$nw", SqliteType.Integer);
        var pFc = cmd.Parameters.Add("$fc", SqliteType.Integer);
        var pF = cmd.Parameters.Add("$f", SqliteType.Integer);
        var pT = cmd.Parameters.Add("$t", SqliteType.Text);
        cmd.Prepare();
        pS.Value = scanId;

        long next = 0;
        var stack = new Stack<(ScanNode Node, long Parent)>();
        stack.Push((result.Root, -1));
        while (stack.Count > 0)
        {
            var (node, parent) = stack.Pop();
            var id = next++;
            pId.Value = id;
            pP.Value = parent;
            pN.Value = node.Name;
            pD.Value = node.IsDirectory ? 1 : 0;
            pSz.Value = node.Size;
            pL.Value = node.LogicalSize;
            pC.Value = node.CloudSize;
            pW.Value = node.LastWriteTicks;
            pNw.Value = node.NewestWriteTicks;
            pFc.Value = node.FileCount;
            pF.Value = (int)node.Flags;
            pT.Value = (object?)node.ReparseTag ?? DBNull.Value;
            cmd.ExecuteNonQuery();
            if (node.Children is { } kids)
                for (var i = kids.Count - 1; i >= 0; i--)
                    stack.Push((kids[i], id));
        }
        if (!result.Cancelled)
            WriteHistory(db, tx, root, FolderHistory.Extract(result), at ?? result.FinishedAt);
        tx.Commit();
        return scanId;
    }

    static long DayOf(DateTimeOffset at) => at.ToLocalTime().Date.Ticks / TimeSpan.TicksPerDay;

    public void RecordHistory(ScanResult result, DateTimeOffset at)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        WriteHistory(db, tx, result.Root.Name, FolderHistory.Extract(result), at);
        tx.Commit();
    }

    static void WriteHistory(SqliteConnection db, SqliteTransaction tx, string root, IReadOnlyList<FolderSize> folders, DateTimeOffset at)
    {
        var key = Paths.Normalize(root);
        var day = DayOf(at);
        using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM folder_history WHERE (root = $r AND day = $d) OR taken_at < $old;";
            del.Parameters.AddWithValue("$r", key);
            del.Parameters.AddWithValue("$d", day);
            del.Parameters.AddWithValue("$old", at.AddDays(-FolderHistory.KeepDays).UtcTicks);
            del.ExecuteNonQuery();
        }
        using (var ins = db.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR REPLACE INTO folder_history (root, day, taken_at, path, size) VALUES ($r, $d, $t, $p, $s);";
            ins.Parameters.AddWithValue("$r", key);
            ins.Parameters.AddWithValue("$d", day);
            ins.Parameters.AddWithValue("$t", at.UtcTicks);
            var p = ins.Parameters.Add("$p", SqliteType.Text);
            var s = ins.Parameters.Add("$s", SqliteType.Integer);
            foreach (var folder in folders)
            {
                p.Value = folder.Path;
                s.Value = folder.Size;
                ins.ExecuteNonQuery();
            }
        }
        while (HistoryBytes(db, tx) > FolderHistory.MaxStoreBytes)
        {
            using var trim = db.CreateCommand();
            trim.Transaction = tx;
            trim.CommandText = "DELETE FROM folder_history WHERE taken_at = (SELECT MIN(taken_at) FROM folder_history) AND NOT (root = $r AND day = $d);";
            trim.Parameters.AddWithValue("$r", key);
            trim.Parameters.AddWithValue("$d", day);
            if (trim.ExecuteNonQuery() == 0)
                break;
        }
    }

    static long HistoryBytes(SqliteConnection db, SqliteTransaction tx)
    {
        using var q = db.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "SELECT COALESCE(SUM(LENGTH(path) + LENGTH(root) + 32), 0) FROM folder_history;";
        return (long)q.ExecuteScalar()!;
    }

    public long HistoryBytes()
    {
        using var db = Open();
        using var q = db.CreateCommand();
        q.CommandText = "SELECT COALESCE(SUM(LENGTH(path) + LENGTH(root) + 32), 0) FROM folder_history;";
        return (long)q.ExecuteScalar()!;
    }

    public int HistoryDays(string root)
    {
        using var db = Open();
        using var q = db.CreateCommand();
        q.CommandText = "SELECT COUNT(DISTINCT day) FROM folder_history WHERE root = $r;";
        q.Parameters.AddWithValue("$r", Paths.Normalize(root));
        return (int)(long)q.ExecuteScalar()!;
    }

    static FolderBaseline? ReadSnapshot(SqliteConnection db, string key, long day, long takenAt)
    {
        using var q = db.CreateCommand();
        q.CommandText = "SELECT path, size FROM folder_history WHERE root = $r AND day = $d;";
        q.Parameters.AddWithValue("$r", key);
        q.Parameters.AddWithValue("$d", day);
        using var r = q.ExecuteReader();
        var list = new List<FolderSize>();
        while (r.Read())
            list.Add(new FolderSize(r.GetString(0), r.GetInt64(1)));
        return list.Count == 0 ? null : new FolderBaseline(new DateTimeOffset(takenAt, TimeSpan.Zero), list);
    }

    static List<(long Day, long TakenAt)> HistoryDaysFor(SqliteConnection db, string key)
    {
        using var q = db.CreateCommand();
        q.CommandText = "SELECT day, MAX(taken_at) FROM folder_history WHERE root = $r GROUP BY day ORDER BY day DESC;";
        q.Parameters.AddWithValue("$r", key);
        using var r = q.ExecuteReader();
        var days = new List<(long, long)>();
        while (r.Read())
            days.Add((r.GetInt64(0), r.GetInt64(1)));
        return days;
    }

    public FolderBaseline? Baseline(string root, DateTimeOffset now, int minAgeDays = 1)
    {
        var key = Paths.Normalize(root);
        using var db = Open();
        var limit = DayOf(now) - minAgeDays;
        foreach (var (day, takenAt) in HistoryDaysFor(db, key))
            if (day <= limit)
                return ReadSnapshot(db, key, day, takenAt);
        return null;
    }

    public FolderGrowth? WeeklyGrowth(string root, int spanDays = 7)
    {
        var key = Paths.Normalize(root);
        using var db = Open();
        var days = HistoryDaysFor(db, key);
        if (days.Count < 2)
            return null;
        var (day, takenAt) = days[0];
        var older = days.Skip(1).MinBy(d => (Math.Abs(day - spanDays - d.Day), -d.Day));
        if (ReadSnapshot(db, key, day, takenAt) is not { } current || ReadSnapshot(db, key, older.Day, older.TakenAt) is not { } before)
            return null;
        return FolderHistory.Compare(current.Folders, before);
    }

    public ScanResult? Load(string root, Action<long, long>? rows = null)
    {
        var key = Paths.Normalize(root);
        using var db = Open();
        long scanId;
        long expected;
        ScanResult header;
        using (var q = db.CreateCommand())
        {
            q.CommandText = "SELECT id, method, finished_at, elapsed_ms, cancelled, errors, usn_journal, usn_next, files, directories FROM scans WHERE root = $r;";
            q.Parameters.AddWithValue("$r", key);
            using var r = q.ExecuteReader();
            if (!r.Read())
                return null;
            scanId = r.GetInt64(0);
            expected = r.GetInt64(8) + r.GetInt64(9) + 1;
            header = new ScanResult
            {
                Root = null!,
                Method = r.GetString(1),
                FinishedAt = new DateTimeOffset(r.GetInt64(2), TimeSpan.Zero),
                Elapsed = TimeSpan.FromMilliseconds(r.GetInt64(3)),
                Cancelled = r.GetInt64(4) != 0,
                Errors = r.GetString(5) is { Length: > 0 } e ? e.Split('\n') : [],
                Usn = r.IsDBNull(6) ? null : new UsnCursor((ulong)r.GetInt64(6), r.GetInt64(7)),
            };
        }

        long count;
        using (var q = db.CreateCommand())
        {
            q.CommandText = "SELECT COALESCE(MAX(id), -1) + 1 FROM nodes WHERE scan_id = $s;";
            q.Parameters.AddWithValue("$s", scanId);
            count = (long)q.ExecuteScalar()!;
        }
        if (count == 0)
            return null;

        var nodes = new ScanNode[count];
        var parents = new long[count];
        var read = 0L;
        var parts = (int)Math.Clamp(count / 250_000, 1, Math.Min(8, Environment.ProcessorCount));
        Parallel.For(0, parts, part =>
        {
            var from = count * part / parts;
            var to = count * (part + 1) / parts;
            using var conn = Open();
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT id, parent, name, is_dir, size, logical, cloud, write, newest, file_count, flags, tag FROM nodes WHERE scan_id = $s AND id >= $a AND id < $b ORDER BY id;";
            q.Parameters.AddWithValue("$s", scanId);
            q.Parameters.AddWithValue("$a", from);
            q.Parameters.AddWithValue("$b", to);
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                parents[id] = r.GetInt64(1);
                nodes[id] = new ScanNode
                {
                    Name = r.GetString(2),
                    IsDirectory = r.GetInt64(3) != 0,
                    Size = r.GetInt64(4),
                    LogicalSize = r.GetInt64(5),
                    CloudSize = r.GetInt64(6),
                    LastWriteTicks = r.GetInt64(7),
                    NewestWriteTicks = r.GetInt64(8),
                    FileCount = (int)r.GetInt64(9),
                    Flags = (NodeFlags)r.GetInt64(10),
                    ReparseTag = r.IsDBNull(11) ? null : r.GetString(11),
                };
                if (rows is not null && (Interlocked.Increment(ref read) & 0x3FFF) == 0)
                    rows(Interlocked.Read(ref read), expected);
            }
        });

        long files = 0, dirs = 0;
        for (var i = 0; i < count; i++)
        {
            var node = nodes[i];
            if (node is null || parents[i] < 0 || nodes[parents[i]] is not { } parent)
                continue;
            node.Parent = parent;
            (parent.Children ??= []).Add(node);
            if (node.IsDirectory)
                dirs++;
            else
                files++;
        }
        if (nodes[0] is null)
            return null;
        rows?.Invoke(count, count);
        return new ScanResult
        {
            Root = nodes[0],
            Files = files,
            Directories = dirs,
            Method = header.Method,
            FinishedAt = header.FinishedAt,
            Elapsed = header.Elapsed,
            Cancelled = header.Cancelled,
            Errors = header.Errors,
            Usn = header.Usn,
        };
    }

    public void SetCursor(string root, UsnCursor cursor)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE scans SET usn_journal = $j, usn_next = $n WHERE root = $r;";
        cmd.Parameters.AddWithValue("$j", (long)cursor.JournalId);
        cmd.Parameters.AddWithValue("$n", cursor.NextUsn);
        cmd.Parameters.AddWithValue("$r", Paths.Normalize(root));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ScanIndexEntry> List()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, root, method, finished_at, files, directories, size, usn_journal, usn_next FROM scans ORDER BY finished_at DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<ScanIndexEntry>();
        while (r.Read())
            list.Add(new ScanIndexEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), new DateTimeOffset(r.GetInt64(3), TimeSpan.Zero),
                r.GetInt64(4), r.GetInt64(5), r.GetInt64(6), r.IsDBNull(7) ? null : new UsnCursor((ulong)r.GetInt64(7), r.GetInt64(8))));
        return list;
    }

    public async Task<UsnUpdateResult?> RefreshAsync(string root, CancellationToken cancel = default)
    {
        var cached = Load(root);
        if (cached?.Usn is null)
            return null;
        var update = await UsnUpdater.ApplyAsync(cached, cancel).ConfigureAwait(false);
        Save(update.Result);
        return update;
    }
}
