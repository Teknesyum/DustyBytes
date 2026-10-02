using System.Text.Json;
using DustyBytes.Clean.Rules;
using DustyBytes.Clean.SystemCleanup;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using DustyBytes.Core.Protection;

namespace DustyBytes.Rules.Tests;

public sealed class PreviewExecutionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "dustybytes-preview-" + Guid.NewGuid().ToString("N"));
    readonly string _cache;
    readonly string _rulesDir;
    readonly string? _dryRun = Environment.GetEnvironmentVariable(DryRun.Variable);
    static readonly RuleSelection[] Selection = [new RuleSelection("idle-app", ["cache"])];

    public PreviewExecutionTests()
    {
        Environment.SetEnvironmentVariable(DryRun.Variable, null);
        _cache = Path.Combine(_root, "app", "cache");
        Directory.CreateDirectory(Path.Combine(_cache, "keep"));
        Directory.CreateDirectory(Path.Combine(_cache, "sub"));
        File.WriteAllText(Path.Combine(_cache, "a.tmp"), "12345");
        File.WriteAllText(Path.Combine(_cache, "b.tmp"), "1234567890");
        File.WriteAllText(Path.Combine(_cache, "sub", "c.tmp"), "123");
        File.WriteAllText(Path.Combine(_cache, "keep", "korunan.tmp"), "1234");

        _rulesDir = Path.Combine(_root, "rules");
        Directory.CreateDirectory(_rulesDir);
        File.WriteAllText(Path.Combine(_rulesDir, "idle-app.json"), $$"""
        {
          "id": "idle-app",
          "name": "Idle App",
          "running": { "processNames": ["definitely-not-a-real-process-name-xyz.exe"], "lockFiles": [] },
          "options": [
            { "id": "cache", "label": "Önbellek", "actions": [
              { "type": "delete", "mode": "recurse", "path": "{{_cache.Replace("\\", "\\\\")}}" }
            ] }
          ]
        }
        """);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DryRun.Variable, _dryRun);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    string Keep => Path.Combine(_cache, "keep", "korunan.tmp");

    CleanerCatalog Catalog(bool protectKeep = true)
    {
        var rules = new ProtectedRules();
        if (protectKeep)
            rules.Files.Add(new NameRule { Name = "korunan.tmp", Reason = "test koruması" });
        return CleanerCatalog.Load(_rulesDir, new ProtectedList(rules));
    }

    static HashSet<string> Set(IEnumerable<string> paths) => new(paths, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Touched_Set_Equals_Preview_Set()
    {
        var catalog = Catalog();
        var preview = catalog.Plan(Selection).ToPreview();
        var deleter = new FakeDeleter();

        var run = catalog.Execute(Selection, preview.Paths(), deleter);

        var shown = Set(preview.Paths());
        var touched = Set(deleter.Deleted);
        Assert.Equal(3, shown.Count);
        Assert.Empty(touched.Except(shown, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(shown.Except(touched, StringComparer.OrdinalIgnoreCase));
        Assert.Null(run.Error);
        Assert.Equal(3, run.Tally.Processed);
        Assert.True(run.Tally.Balanced);
    }

    [Fact]
    public void Worker_Handler_Touches_Only_Preview_Set()
    {
        var catalog = Catalog();
        var previewResponse = WorkerCleanHandlers.HandleCleanPreview(new WorkerRequest { Op = Ops.CleanPreview, Items = ["idle-app/cache"] }, catalog);
        var preview = JsonSerializer.Deserialize(previewResponse.Payload!, IpcJson.Default.CleanPreview)!;
        var late = Path.Combine(_cache, "sub", "sonradan.tmp");
        File.WriteAllText(late, "yeni");
        var deleter = new FakeDeleter();

        var response = WorkerCleanHandlers.HandleClean(
            new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = ["idle-app/cache"], Paths = preview.Paths(), Digest = preview.Digest },
            catalog, deleter);

        Assert.True(response.Ok, response.Message);
        Assert.True(Set(deleter.Deleted).SetEquals(preview.Paths()));
        Assert.True(File.Exists(late));
        Assert.Equal(preview.Count, response.Tally!.Shown);
        Assert.Equal(preview.Count, response.Tally.Processed);
        Assert.Equal(1, response.Tally.NotShown);
        Assert.Equal(response.Tally.ProcessedBytes, response.FreedBytes);
    }

    [Fact]
    public void File_Added_After_Preview_Is_Not_Deleted()
    {
        var catalog = Catalog();
        var preview = catalog.Plan(Selection).ToPreview();
        var late = Path.Combine(_cache, "yeni.tmp");
        File.WriteAllText(late, "sonradan");
        var deleter = new FakeDeleter();

        var run = catalog.Execute(Selection, preview.Paths(), deleter);

        Assert.True(File.Exists(late));
        Assert.DoesNotContain(late, deleter.Deleted, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, run.Tally.NotShown);
        Assert.Equal(3, run.Tally.Processed);
    }

    [Fact]
    public void Protected_Path_Stays_Out_Of_Preview_And_Execution()
    {
        var catalog = Catalog();
        var plan = catalog.Plan(Selection);
        Assert.DoesNotContain(Keep, plan.ToPreview().Paths(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(plan.Excluded, s => s.Path.Equals(Keep, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, plan.ToPreview().Protected);

        var deleter = new FakeDeleter();
        var forged = plan.ToPreview().Paths().Append(Keep).ToList();
        var run = catalog.Execute(Selection, forged, deleter);

        Assert.True(File.Exists(Keep));
        Assert.DoesNotContain(Keep, deleter.Deleted, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, run.Tally.Protected);
        Assert.Equal(4, run.Tally.Shown);
        Assert.True(run.Tally.Balanced);
    }

    [Fact]
    public void Counts_Report_Shown_Processed_And_Skip_Reasons()
    {
        var catalog = Catalog();
        var preview = catalog.Plan(Selection).ToPreview();
        File.Delete(Path.Combine(_cache, "b.tmp"));
        File.WriteAllText(Path.Combine(_cache, "yeni.tmp"), "sonradan");
        using var held = new FileStream(Path.Combine(_cache, "sub", "c.tmp"), FileMode.Open, FileAccess.Read, FileShare.None);

        var run = catalog.Execute(Selection, preview.Paths(), new RealFileDeleter());

        var tally = run.Tally;
        Assert.Equal(3, tally.Shown);
        Assert.Equal(1, tally.Processed);
        Assert.Equal(5, tally.ProcessedBytes);
        Assert.Equal(1, tally.Vanished);
        Assert.Equal(1, tally.Locked);
        Assert.Equal(0, tally.Protected);
        Assert.Equal(1, tally.NotShown);
        Assert.Equal(2, tally.Skipped);
        Assert.True(tally.Balanced);
        Assert.Equal("Gösterilen 3 dosya, silinen 1, atlanan 2 (1 kullanımda, 1 kayboldu); önizlemeden sonra çıkan 1 dosya gösterilmediği için dokunulmadı", tally.Describe());
    }

    [Fact]
    public void Describe_Uses_Single_Reason_Without_Count()
    {
        var tally = new PathTally { Shown = 1204, Processed = 1198, Locked = 6 };
        Assert.Equal("Gösterilen 1.204 dosya, silinen 1.198, atlanan 6 (kullanımda)", tally.Describe());
    }

    [Fact]
    public void Clean_Without_Preview_Is_Refused()
    {
        var catalog = Catalog();
        var deleter = new FakeDeleter();

        var response = WorkerCleanHandlers.HandleClean(new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = ["idle-app/cache"] }, catalog, deleter);

        Assert.False(response.Ok);
        Assert.Empty(deleter.Deleted);
        Assert.True(File.Exists(Path.Combine(_cache, "a.tmp")));
    }

    [Fact]
    public void Clean_With_List_Not_Matching_Digest_Is_Refused()
    {
        var catalog = Catalog();
        var preview = catalog.Plan(Selection).ToPreview();
        var deleter = new FakeDeleter();

        var response = WorkerCleanHandlers.HandleClean(
            new WorkerRequest { Op = Ops.Clean, UserApproved = true, Items = ["idle-app/cache"], Paths = [.. preview.Paths(), Path.Combine(_cache, "baska.tmp")], Digest = preview.Digest },
            catalog, deleter);

        Assert.False(response.Ok);
        Assert.Empty(deleter.Deleted);
    }

    [Fact]
    public void Deleter_Guard_Stops_On_Unshown_Path()
    {
        var inner = new FakeDeleter();
        var guard = new ShownOnlyDeleter(inner, Set([Path.Combine(_cache, "a.tmp")]));

        var e = Assert.Throws<ShownListViolationException>(() => guard.TryDeleteFile(Path.Combine(_cache, "b.tmp")));

        Assert.EndsWith("b.tmp", e.Path);
        Assert.Empty(inner.Deleted);
        Assert.True(File.Exists(Path.Combine(_cache, "b.tmp")));
    }

    [Fact]
    public void Preview_Digest_Is_Order_Independent_And_Sensitive_To_Content()
    {
        var a = PreviewDigest.Of([@"C:\x\1.tmp", @"C:\x\2.tmp"]);
        Assert.Equal(a, PreviewDigest.Of([@"C:\x\2.tmp", @"C:\x\1.tmp"]));
        Assert.NotEqual(a, PreviewDigest.Of([@"C:\x\1.tmp"]));
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Preview_Lists_Size_And_Last_Write()
    {
        var stamp = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_cache, "a.tmp"), stamp);

        var preview = Catalog().Plan(Selection).ToPreview();

        var a = Assert.Single(preview.Files, f => f.Path.EndsWith("a.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, a.Bytes);
        Assert.Equal(stamp, a.LastWriteUtc);
        Assert.Equal("idle-app/cache", a.Option);
        Assert.Equal(18, preview.Bytes);
    }
}
