using System.Security.Principal;
using System.Text.Json;
using DustyBytes.Clean.Quarantine;
using DustyBytes.Clean.Safety;
using DustyBytes.Clean.Uninstall;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;
using Xunit.Abstractions;

namespace DustyBytes.Uninstall.Tests;

public class RealMachineUninstallTests(ITestOutputHelper output)
{
    public const string Variable = "DUSTYBYTES_REAL_UNINSTALL";

    [Fact]
    [Trait("Kind", "Machine")]
    public async Task UninstallOneTestProgramAndCleanItsTraces()
    {
        var name = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(name))
            return;
        using (var identity = WindowsIdentity.GetCurrent())
            Assert.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), "Yönetici olarak çalıştırılmalı");

        var gate = SafetyGate.LoadDefault();
        var quarantine = new QuarantineStore(gate);
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        var handlers = new UninstallHandlers(gate.List, path => Task.FromResult(quarantine.Quarantine(path).Ok))
        {
            User = UserScope.Resolve(sid, WindowsRegistryView.Instance),
        };
        var programs = InstalledPrograms.Enumerate(new EnumerateOptions { MeasureSize = false, UserSid = sid });
        var program = programs.SingleOrDefault(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
        Assert.True(program is not null, $"Kurulu değil: {name}");

        var lines = new List<string>();
        var progress = new Sync(p => lines.Add($"{p.Step} {p.Percent:0} {p.Line}"));
        var response = await handlers.HandleUninstall(new WorkerRequest
        {
            Op = Ops.Uninstall,
            Target = program!.Id,
            UserApproved = true,
            Items = [UninstallHandlers.AutoClean],
        }, progress, default);

        foreach (var line in lines)
            output.WriteLine(line);
        output.WriteLine(response.Message);
        foreach (var item in response.Items)
            output.WriteLine($"{(item.Ok ? "tamam" : "HATA")} {item.Path}: {item.Message}");
        if (response.Payload is { } payload)
        {
            var diff = JsonSerializer.Deserialize(payload, UninstallJson.Default.LeftoverSnapshot)!;
            foreach (var c in diff.Candidates)
                output.WriteLine($"kalan {c.Tier} {c.Kind} {c.Target}");
            foreach (var r in diff.AutoRemoved)
                output.WriteLine($"temizlendi {r.Kind} {r.Target}: {r.Message}");
            foreach (var n in diff.Notes)
                output.WriteLine($"not {n}");
        }
        var file = Environment.GetEnvironmentVariable(Variable + "_OUT");
        if (!string.IsNullOrEmpty(file))
            File.WriteAllLines(file, lines.Append(response.Message));
        Assert.True(response.Ok, response.Message);
    }

    sealed class Sync(Action<WorkerProgress> a) : IProgress<WorkerProgress>
    {
        public void Report(WorkerProgress value) => a(value);
    }
}
