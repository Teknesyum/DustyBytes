using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Core.Ipc;
using DustyBytes.Worker;
using Microsoft.Data.Sqlite;

namespace DustyBytes.Safety.Tests;

public class QuarantineSessionTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    static QuarantineOptions Options(TempTree tree) => new()
    {
        RootResolver = _ => tree.P(".dustybytes", "quarantine"),
        FallbackToRecycleBin = false,
    };

    static QuarantineStore Store(TempTree tree) => new(SafetyGate.LoadDefault(), Options(tree));

    static string Session() => Guid.NewGuid().ToString("N");

    static SqliteConnection Open(string db)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static bool HasSessionColumn(string db)
    {
        using var connection = Open(db);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name = 'session_id'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    [Fact]
    public void Eski_Sema_Oturum_Sutunu_Eklenir_Eski_Kayit_Bos_Kalir()
    {
        using var tree = new TempTree();
        var root = tree.P(".dustybytes", "quarantine");
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "manifest.db");
        var oldId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        using (var connection = Open(db))
        {
            Exec(connection, """
                CREATE TABLE items(
                    id TEXT PRIMARY KEY, original_path TEXT NOT NULL, is_dir INTEGER NOT NULL, size INTEGER NOT NULL,
                    attributes INTEGER NOT NULL, created_utc INTEGER NOT NULL, modified_utc INTEGER NOT NULL,
                    accessed_utc INTEGER NOT NULL, acl TEXT, volume_id TEXT NOT NULL, moved_utc INTEGER NOT NULL,
                    expires_utc INTEGER NOT NULL, unit_id TEXT, state TEXT NOT NULL);
                """);
            Exec(connection, $"INSERT INTO items VALUES('{oldId}', 'C:\\eski.txt', 0, 7, 32, {now.Ticks}, {now.Ticks}, {now.Ticks}, NULL, 'vol', {now.Ticks}, {now.AddDays(30).Ticks}, 'u0', 'pending')");
        }
        File.WriteAllText(Path.Combine(root, oldId), "eskiveri");
        Assert.False(HasSessionColumn(db));

        var store = Store(tree);
        var old = Assert.Single(store.List());
        Assert.True(HasSessionColumn(db));
        Assert.Equal(oldId, old.Id);
        Assert.Null(old.SessionId);

        var session = Session();
        var file = tree.File(@"kaynak\yeni.txt", "yeni");
        var moved = store.Quarantine(file, unitId: "u1", sessionId: session);
        Assert.True(moved.Status == OpStatus.Done, moved.Message);

        var items = store.SessionItems(session);
        Assert.Equal(moved.Id, Assert.Single(items).Id);
        Assert.Equal(session, items[0].SessionId);
        Assert.Equal(2, store.List().Count);
        Assert.Null(store.List().Single(e => e.Id == oldId).SessionId);

        _ = Store(tree).List();
        Assert.True(HasSessionColumn(db));
    }

    [Fact]
    public void Gecersiz_Oturum_Kimligi_Kaydedilmez_Ve_Bos_Doner()
    {
        using var tree = new TempTree();
        var store = Store(tree);
        var moved = store.Quarantine(tree.File(@"kaynak\a.txt", "a"), sessionId: "../kötü");
        Assert.True(moved.Status == OpStatus.Done, moved.Message);
        Assert.Null(Assert.Single(store.List()).SessionId);
        Assert.Empty(store.SessionItems("../kötü"));
        Assert.Empty(store.RestoreSession(""));
    }

    [Fact]
    public void Oturum_Geri_Alma_Yalniz_O_Oturumu_Dondurur()
    {
        using var tree = new TempTree();
        var store = Store(tree);
        var first = Session();
        var second = Session();
        var dir = tree.File(@"oyun\veri\kayit.dat", "kayit");
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(dir))!;
        var single = tree.File(@"belge\not.txt", "not");
        var other = tree.File(@"diger\b.txt", "b");

        Assert.Equal(OpStatus.Done, store.Quarantine(folder, sessionId: first, includeUserData: true).Status);
        Assert.Equal(OpStatus.Done, store.Quarantine(single, sessionId: first, includeUserData: true).Status);
        Assert.Equal(OpStatus.Done, store.Quarantine(other, sessionId: second, includeUserData: true).Status);
        Assert.Equal(2, store.SessionItems(first).Count);

        var results = store.RestoreSession(first);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Status == OpStatus.Done, r.Message));
        Assert.True(File.Exists(dir));
        Assert.Equal("kayit", File.ReadAllText(dir));
        Assert.True(File.Exists(single));
        Assert.False(File.Exists(other));
        Assert.Empty(store.SessionItems(first));
        Assert.Equal(second, Assert.Single(store.List()).SessionId);
    }

    [Fact]
    public async Task Worker_Oturum_Kimligiyle_Geri_Yukler()
    {
        using var tree = new TempTree();
        var services = new WorkerServices(SafetyGate.LoadDefault(), Options(tree));
        var pipe = WorkerClient.NewPipeName();
        var server = new WorkerServer(pipe, Environment.ProcessId, services, watchParent: false);
        var running = server.RunAsync();
        try
        {
            await using var client = await WorkerClient.ConnectAsync(pipe, Environment.ProcessId, Timeout);
            var session = Session();
            var a = tree.File(@"kaynak\a.bin", "123");
            var b = tree.File(@"kaynak\b.bin", "4567");
            var c = tree.File(@"kaynak\c.bin", "89");

            var moved = await client.SendAsync(new WorkerRequest { Op = Ops.Quarantine, Paths = [a, b], UserApproved = true, UnitId = "u1", SessionId = session });
            Assert.True(moved.Ok, moved.Message);
            var loose = await client.SendAsync(new WorkerRequest { Op = Ops.Quarantine, Paths = [c], UserApproved = true, UnitId = "u2" });
            Assert.True(loose.Ok, loose.Message);
            Assert.Equal(session, services.Quarantine.List().Single(e => e.OriginalPath.EndsWith("a.bin", StringComparison.OrdinalIgnoreCase)).SessionId);

            var bad = await client.SendAsync(new WorkerRequest { Op = Ops.Restore, SessionId = "../x" });
            Assert.False(bad.Ok);
            Assert.False(File.Exists(a));

            var restored = await client.SendAsync(new WorkerRequest { Op = Ops.Restore, SessionId = session, UserApproved = true });
            Assert.True(restored.Ok, restored.Message);
            Assert.Equal(2, restored.Items.Count);
            Assert.True(File.Exists(a));
            Assert.True(File.Exists(b));
            Assert.False(File.Exists(c));
            Assert.Null(Assert.Single(services.Quarantine.List()).SessionId);
        }
        finally
        {
            server.Stop();
            await running.WaitAsync(Timeout);
        }
    }
}
