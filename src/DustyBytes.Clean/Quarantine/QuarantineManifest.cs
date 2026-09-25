using Microsoft.Data.Sqlite;

namespace DustyBytes.Clean.Quarantine;

public static class QuarantineState
{
    public const string Moving = "moving";
    public const string Pending = "pending";
    public const string Restored = "restored";
    public const string Purged = "purged";
}

public sealed record QuarantineEntry
{
    public required string Id { get; init; }
    public required string OriginalPath { get; init; }
    public required string Root { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public int Attributes { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
    public DateTime AccessedUtc { get; init; }
    public string? Acl { get; init; }
    public string VolumeId { get; init; } = "";
    public DateTime MovedUtc { get; init; }
    public DateTime ExpiresUtc { get; init; }
    public string? UnitId { get; init; }
    public string State { get; init; } = QuarantineState.Pending;

    public string StoredPath => Path.Combine(Root, Id);
}

internal sealed class QuarantineManifest
{
    public const string FileName = "manifest.db";

    readonly string _root;
    readonly string _connection;

    public QuarantineManifest(string root)
    {
        _root = root;
        _connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, FileName),
            Pooling = false,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        using var db = Open();
        Exec(db, """
            CREATE TABLE IF NOT EXISTS items(
                id TEXT PRIMARY KEY,
                original_path TEXT NOT NULL,
                is_dir INTEGER NOT NULL,
                size INTEGER NOT NULL,
                attributes INTEGER NOT NULL,
                created_utc INTEGER NOT NULL,
                modified_utc INTEGER NOT NULL,
                accessed_utc INTEGER NOT NULL,
                acl TEXT,
                volume_id TEXT NOT NULL,
                moved_utc INTEGER NOT NULL,
                expires_utc INTEGER NOT NULL,
                unit_id TEXT,
                state TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS items_state ON items(state, expires_utc);
            """);
    }

    public static bool ExistsIn(string root) => File.Exists(Path.Combine(root, FileName));

    SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Insert(QuarantineEntry e)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items(id, original_path, is_dir, size, attributes, created_utc, modified_utc, accessed_utc, acl, volume_id, moved_utc, expires_utc, unit_id, state)
            VALUES($id, $path, $dir, $size, $attrs, $c, $m, $a, $acl, $vol, $moved, $exp, $unit, $state)
            """;
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$path", e.OriginalPath);
        cmd.Parameters.AddWithValue("$dir", e.IsDirectory ? 1 : 0);
        cmd.Parameters.AddWithValue("$size", e.Size);
        cmd.Parameters.AddWithValue("$attrs", e.Attributes);
        cmd.Parameters.AddWithValue("$c", e.CreatedUtc.Ticks);
        cmd.Parameters.AddWithValue("$m", e.ModifiedUtc.Ticks);
        cmd.Parameters.AddWithValue("$a", e.AccessedUtc.Ticks);
        cmd.Parameters.AddWithValue("$acl", (object?)e.Acl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$vol", e.VolumeId);
        cmd.Parameters.AddWithValue("$moved", e.MovedUtc.Ticks);
        cmd.Parameters.AddWithValue("$exp", e.ExpiresUtc.Ticks);
        cmd.Parameters.AddWithValue("$unit", (object?)e.UnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", e.State);
        cmd.ExecuteNonQuery();
    }

    public void SetState(string id, string state)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE items SET state = $state WHERE id = $id";
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetExpiry(string id, DateTime expiresUtc)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE items SET expires_utc = $exp WHERE id = $id";
        cmd.Parameters.AddWithValue("$exp", expiresUtc.Ticks);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Remove(string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public QuarantineEntry? Get(string id) => Query("WHERE id = $id", ("$id", id)).FirstOrDefault();

    public List<QuarantineEntry> All(bool includeClosed) =>
        includeClosed ? Query("ORDER BY moved_utc DESC") : Query("WHERE state IN ('pending', 'moving') ORDER BY moved_utc DESC");

    public List<QuarantineEntry> Expired(DateTime nowUtc) =>
        Query("WHERE state = 'pending' AND expires_utc <= $now", ("$now", nowUtc.Ticks));

    List<QuarantineEntry> Query(string tail, params (string Name, object Value)[] args)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, original_path, is_dir, size, attributes, created_utc, modified_utc, accessed_utc, acl, volume_id, moved_utc, expires_utc, unit_id, state FROM items " + tail;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        using var r = cmd.ExecuteReader();
        var list = new List<QuarantineEntry>();
        while (r.Read())
        {
            list.Add(new QuarantineEntry
            {
                Id = r.GetString(0),
                OriginalPath = r.GetString(1),
                Root = _root,
                IsDirectory = r.GetInt64(2) != 0,
                Size = r.GetInt64(3),
                Attributes = (int)r.GetInt64(4),
                CreatedUtc = new DateTime(r.GetInt64(5), DateTimeKind.Utc),
                ModifiedUtc = new DateTime(r.GetInt64(6), DateTimeKind.Utc),
                AccessedUtc = new DateTime(r.GetInt64(7), DateTimeKind.Utc),
                Acl = r.IsDBNull(8) ? null : r.GetString(8),
                VolumeId = r.GetString(9),
                MovedUtc = new DateTime(r.GetInt64(10), DateTimeKind.Utc),
                ExpiresUtc = new DateTime(r.GetInt64(11), DateTimeKind.Utc),
                UnitId = r.IsDBNull(12) ? null : r.GetString(12),
                State = r.GetString(13),
            });
        }
        return list;
    }
}
