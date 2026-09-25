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

    public long Save(ScanResult result)
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
        tx.Commit();
        return scanId;
    }

    public ScanResult? Load(string root)
    {
        var key = Paths.Normalize(root);
        using var db = Open();
        long scanId;
        ScanResult header;
        using (var q = db.CreateCommand())
        {
            q.CommandText = "SELECT id, method, finished_at, elapsed_ms, cancelled, errors, usn_journal, usn_next FROM scans WHERE root = $r;";
            q.Parameters.AddWithValue("$r", key);
            using var r = q.ExecuteReader();
            if (!r.Read())
                return null;
            scanId = r.GetInt64(0);
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

        var nodes = new List<ScanNode>();
        long files = 0, dirs = 0;
        using (var q = db.CreateCommand())
        {
            q.CommandText = "SELECT id, parent, name, is_dir, size, logical, cloud, write, newest, file_count, flags, tag FROM nodes WHERE scan_id = $s ORDER BY id;";
            q.Parameters.AddWithValue("$s", scanId);
            using var r = q.ExecuteReader();
            while (r.Read())
            {
                var parentId = r.GetInt64(1);
                var parent = parentId >= 0 ? nodes[(int)parentId] : null;
                var isDir = r.GetInt64(3) != 0;
                var node = new ScanNode
                {
                    Name = r.GetString(2),
                    IsDirectory = isDir,
                    Parent = parent,
                    Size = r.GetInt64(4),
                    LogicalSize = r.GetInt64(5),
                    CloudSize = r.GetInt64(6),
                    LastWriteTicks = r.GetInt64(7),
                    NewestWriteTicks = r.GetInt64(8),
                    FileCount = (int)r.GetInt64(9),
                    Flags = (NodeFlags)r.GetInt64(10),
                    ReparseTag = r.IsDBNull(11) ? null : r.GetString(11),
                };
                nodes.Add(node);
                if (parent is not null)
                {
                    (parent.Children ??= []).Add(node);
                    if (isDir)
                        dirs++;
                    else
                        files++;
                }
            }
        }
        if (nodes.Count == 0)
            return null;
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
