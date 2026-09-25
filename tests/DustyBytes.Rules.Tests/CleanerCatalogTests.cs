using System.Diagnostics;
using DustyBytes.Clean.Rules;
using DustyBytes.Core.Protection;

namespace DustyBytes.Rules.Tests;

public class CleanerCatalogTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "dustybytes-catalog-" + Guid.NewGuid().ToString("N"));
    readonly string _rulesDir;

    public CleanerCatalogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app", "cache"));
        File.WriteAllText(Path.Combine(_root, "app", "cache", "a.tmp"), "12345");
        File.WriteAllText(Path.Combine(_root, "app", "cache", "b.tmp"), "1234567890");

        _rulesDir = Path.Combine(_root, "rules");
        Directory.CreateDirectory(_rulesDir);
        var ruleJson = $$"""
        {
          "id": "sample-app",
          "name": "Sample App",
          "running": {
            "processNames": ["{{CurrentProcessName}}.exe"],
            "lockFiles": []
          },
          "options": [
            {
              "id": "cache",
              "label": "Önbellek",
              "actions": [
                { "type": "delete", "mode": "recurse", "path": "{{EscapePath(Path.Combine(_root, "app", "cache"))}}" }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_rulesDir, "sample-app.json"), ruleJson);
    }

    static string CurrentProcessName => Process.GetCurrentProcess().ProcessName;

    static string EscapePath(string path) => path.Replace("\\", "\\\\");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    static ProtectedList EmptyProtection() => new(new ProtectedRules());

    [Fact]
    public void IsRunningTrueWhenProcessNameMatchesCurrentProcess()
    {
        var catalog = CleanerCatalog.Load(_rulesDir, EmptyProtection());
        var rule = catalog.Find("sample-app")!;
        var (running, reason) = catalog.IsRunning(rule);
        Assert.True(running);
        Assert.NotNull(reason);
    }

    [Fact]
    public void PreviewCountsFilesAndBytes()
    {
        var catalog = CleanerCatalog.Load(_rulesDir, EmptyProtection());
        var preview = catalog.Preview([new RuleSelection("sample-app", ["cache"])]);
        var entry = Assert.Single(preview);
        Assert.Equal(2, entry.FileCount);
        Assert.Equal(15, entry.Bytes);
    }

    [Fact]
    public void ExecuteSkipsWhenRuleIsRunning()
    {
        var catalog = CleanerCatalog.Load(_rulesDir, EmptyProtection());
        var deleter = new FakeDeleter();
        var results = catalog.Execute([new RuleSelection("sample-app", ["cache"])], deleter);
        var result = Assert.Single(results);
        Assert.False(result.Ran);
        Assert.Empty(deleter.Deleted);
    }

    [Fact]
    public void ExecuteDeletesWhenRuleNotRunning()
    {
        var protectedList = EmptyProtection();
        var loneRuleDir = Path.Combine(_root, "rules-not-running");
        Directory.CreateDirectory(loneRuleDir);
        var json = $$"""
        {
          "id": "idle-app",
          "name": "Idle App",
          "running": { "processNames": ["definitely-not-a-real-process-name-xyz.exe"], "lockFiles": [] },
          "options": [
            { "id": "cache", "label": "Önbellek", "actions": [
              { "type": "delete", "mode": "recurse", "path": "{{EscapePath(Path.Combine(_root, "app", "cache"))}}" }
            ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(loneRuleDir, "idle-app.json"), json);

        var catalog = CleanerCatalog.Load(loneRuleDir, protectedList);
        var deleter = new FakeDeleter();
        var results = catalog.Execute([new RuleSelection("idle-app", ["cache"])], deleter);
        var result = Assert.Single(results);
        Assert.True(result.Ran);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(2, deleter.Deleted.Count);
    }

    [Fact]
    public void ProtectedPathIsSkippedNotDeleted()
    {
        var rules = new ProtectedRules();
        rules.Roots.Add(new PathRule { Path = Path.Combine(_root, "app", "cache"), Reason = "test koruması" });
        var protectedList = new ProtectedList(rules);

        var loneRuleDir = Path.Combine(_root, "rules-protected");
        Directory.CreateDirectory(loneRuleDir);
        var json = $$"""
        {
          "id": "protected-app",
          "name": "Protected App",
          "running": { "processNames": [], "lockFiles": [] },
          "options": [
            { "id": "cache", "label": "Önbellek", "actions": [
              { "type": "delete", "mode": "recurse", "path": "{{EscapePath(Path.Combine(_root, "app", "cache"))}}" }
            ] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(loneRuleDir, "protected-app.json"), json);

        var catalog = CleanerCatalog.Load(loneRuleDir, protectedList);
        var deleter = new FakeDeleter();
        var results = catalog.Execute([new RuleSelection("protected-app", ["cache"])], deleter);
        var result = Assert.Single(results);
        Assert.True(result.Ran);
        Assert.Equal(0, result.DeletedFiles);
        Assert.Empty(deleter.Deleted);
    }
}
