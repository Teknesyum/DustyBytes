using DustyBytes.Clean.Quarantine;
using DustyBytes.Core;
using Microsoft.Data.Sqlite;

namespace DustyBytes.App.Services;

public static class QuarantineReader
{
    public const double WarnShare = 0.20;

    public static IEnumerable<string> DefaultRoots()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                continue;
            bool ready;
            try
            {
                ready = drive.IsReady;
            }
            catch (IOException)
            {
                ready = false;
            }
            if (ready)
                yield return Path.Combine(drive.RootDirectory.FullName, Paths.QuarantineDir, "quarantine");
        }
    }

    public static QuarantineSnapshot Read() => Read(DefaultRoots());

    public static QuarantineSnapshot Read(IEnumerable<string> roots)
    {
        var entries = new List<QuarantineEntry>();
        var usage = new List<VolumeUsage>();
        var denied = new List<string>();
        foreach (var root in roots)
        {
            var db = Path.Combine(root, "manifest.db");
            try
            {
                if (!File.Exists(db))
                    continue;
                var found = Query(root, db);
                entries.AddRange(found);
                var pending = found.Where(e => e.State == QuarantineState.Pending).ToList();
                var bytes = pending.Sum(e => e.Size);
                var total = VolumeBytes(root);
                usage.Add(new VolumeUsage(root, pending.FirstOrDefault()?.VolumeId ?? "", pending.Count, bytes, total, total > 0 && bytes > total * WarnShare));
            }
            catch (Exception e) when (e is SqliteException or UnauthorizedAccessException or IOException)
            {
                denied.Add(Path.GetPathRoot(root) ?? root);
            }
        }
        var note = denied.Count == 0 ? null : $"{string.Join(", ", denied)} karantina kaydı okunamadı; yönetici izniyle açılır";
        return new QuarantineSnapshot([.. entries.OrderByDescending(e => e.MovedUtc)], usage, denied.Count == 0, note);
    }

    static long VolumeBytes(string root)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(root)!).TotalSize;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    static List<QuarantineEntry> Query(string root, string db)
    {
        var connection = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var con = new SqliteConnection(connection);
        con.Open();
        bool session;
        using (var probe = con.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name = 'session_id'";
            session = Convert.ToInt64(probe.ExecuteScalar()) > 0;
        }
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT id, original_path, is_dir, size, volume_id, moved_utc, expires_utc, unit_id, state, {(session ? "session_id" : "NULL")} FROM items WHERE state IN ('pending', 'moving') ORDER BY moved_utc DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<QuarantineEntry>();
        while (r.Read())
        {
            list.Add(new QuarantineEntry
            {
                Id = r.GetString(0),
                OriginalPath = r.GetString(1),
                Root = root,
                IsDirectory = r.GetInt64(2) != 0,
                Size = r.GetInt64(3),
                VolumeId = r.GetString(4),
                MovedUtc = new DateTime(r.GetInt64(5), DateTimeKind.Utc),
                ExpiresUtc = new DateTime(r.GetInt64(6), DateTimeKind.Utc),
                UnitId = r.IsDBNull(7) ? null : r.GetString(7),
                State = r.GetString(8),
                SessionId = r.IsDBNull(9) ? null : r.GetString(9),
            });
        }
        return list;
    }
}
