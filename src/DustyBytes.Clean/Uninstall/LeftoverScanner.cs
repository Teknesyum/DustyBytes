using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public sealed record ScanProgress(string Step, double Percent, string? Line = null);

public sealed class LeftoverScanner
{
    public const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";
    public const string FirewallRulesPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    static readonly string[] RunKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
    ];

    static readonly HashSet<string> SkipSoftwareKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Classes", "Policies", "WOW6432Node", "Clients", "RegisteredApplications", "ODBC", "Windows", "Microsoft",
    };

    readonly ScanContext _ctx;

    public LeftoverScanner(ScanContext context) => _ctx = context;

    sealed class Builder(InstalledProgram program, string? installDir)
    {
        public readonly InstalledProgram Program = program;
        public readonly string? InstallDir = installDir;
        public readonly Dictionary<string, LeftoverCandidate> Candidates = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<BlockedCandidate> Blocked = [];
        public readonly List<string> Notes = [];

        public void Block(LeftoverKind kind, string target, string reason)
        {
            if (!Blocked.Any(b => b.Kind == kind && b.Target.Equals(target, StringComparison.OrdinalIgnoreCase)))
                Blocked.Add(new BlockedCandidate(kind, target, reason));
        }
    }

    public LeftoverSnapshot Snapshot(InstalledProgram program, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var snapshotId = Guid.NewGuid().ToString("N");
        if (program.Source == ProgramSource.Msix)
        {
            return new LeftoverSnapshot
            {
                Id = snapshotId,
                Program = program,
                Notes = ["Store paketi: PackageManager kaldırır, kalıntı araması gerekmez"],
            };
        }

        var (installDir, inferred) = ResolveInstallDir(program, out var installNote);
        var b = new Builder(program, installDir);
        if (installNote is not null)
            b.Notes.Add(installNote);

        var steps = new (string Name, Action Run)[]
        {
            ("Kurulum klasörü", () => AddInstallFolder(b, inferred)),
            ("Uygulama verisi klasörleri", () => ScanDataFolders(b)),
            ("Kayıt defteri", () => { if (_ctx.ScanRegistrySoftware) ScanSoftwareKeys(b); AddUninstallKey(b); }),
            ("Servisler", () => { if (_ctx.ScanServices) ScanServices(b); }),
            ("Zamanlanmış görevler", () => ScanTasks(b)),
            ("Başlangıç girdileri", () => { if (_ctx.ScanStartup) ScanRunKeys(b); }),
            ("Kısayollar", () => ScanShortcuts(b)),
            ("Dosya ilişkileri", () => { if (_ctx.ScanAssociations) ScanAssociations(b); }),
            ("Güvenlik duvarı kuralları", () => { if (_ctx.ScanFirewall) ScanFirewall(b); }),
        };

        for (var i = 0; i < steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(steps[i].Name, 100.0 * i / steps.Length));
            steps[i].Run();
        }

        var candidates = CollapseNested(b.Candidates.Values).OrderByDescending(c => c.Tier).ThenByDescending(c => c.Score).ToList();
        progress?.Report(new ScanProgress("Tamam", 100, $"{candidates.Count} aday, {b.Blocked.Count} engellendi"));
        return new LeftoverSnapshot
        {
            Id = snapshotId,
            Program = program,
            Candidates = candidates,
            Blocked = b.Blocked,
            Notes = b.Notes,
        };
    }

    public LeftoverSnapshot Diff(LeftoverSnapshot before)
    {
        var program = before.Program;
        if (IsStillInstalled(program))
        {
            return before with
            {
                IsDiff = true,
                ProgramStillInstalled = true,
                Candidates = [],
                Notes = [.. before.Notes, "Program hâlâ kurulu görünüyor; kaldırıcı tamamlanmamış ya da iptal edilmiş olabilir, kalıntı önerilmez"],
            };
        }

        var remaining = new List<LeftoverCandidate>();
        var blocked = new List<BlockedCandidate>(before.Blocked);
        foreach (var c in before.Candidates)
        {
            if (!Exists(c))
                continue;
            if (c.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut && GateCandidate(c, before.Program) is { } reason)
            {
                blocked.Add(new BlockedCandidate(c.Kind, c.Target, reason));
                continue;
            }
            remaining.Add(c.Kind is LeftoverKind.Folder or LeftoverKind.File ? c with { Bytes = FolderSize.MeasureAny(c.Target) } : c);
        }
        return before with { IsDiff = true, Candidates = remaining, Blocked = blocked };
    }

    public bool IsStillInstalled(InstalledProgram program)
    {
        if (program.Source == ProgramSource.Msix)
            return program.PackageFullName is { } full && MsixPackages.Exists(full);
        if (program.Key is not { } key || !_ctx.Registry.KeyExists(key) || _ctx.Registry.GetString(key, "DisplayName") is null)
            return false;
        if (program.WindowsInstaller && program.ProductCode is { } pc)
            return InstalledPrograms.MsiProductRegistered(_ctx.Registry, pc);
        var exe = CommandLine.Executable(program.UninstallString ?? program.QuietUninstallString);
        return exe is not null && Path.IsPathRooted(exe) && _ctx.Probe.FileExists(exe);
    }

    public bool Exists(LeftoverCandidate c) => c.Kind switch
    {
        LeftoverKind.Folder => _ctx.Probe.DirectoryExists(c.Target),
        LeftoverKind.File or LeftoverKind.Shortcut => _ctx.Probe.FileExists(c.Target),
        LeftoverKind.ScheduledTask => c.Detail is { } f && _ctx.Probe.FileExists(f),
        LeftoverKind.RegistryValue or LeftoverKind.StartupEntry or LeftoverKind.FirewallRule =>
            c.Key is { } k && c.ValueName is { } v && _ctx.Registry.ValueExists(k, v),
        _ => c.Key is { } key && _ctx.Registry.KeyExists(key),
    };

    (string? Dir, bool Inferred) ResolveInstallDir(InstalledProgram p, out string? note)
    {
        note = null;
        if (p.InstallLocation is { } loc)
        {
            if (_ctx.IsTooBroad(loc))
            {
                note = $"InstallLocation çok geniş, kanıt sayılmaz: {loc}";
                return (null, false);
            }
            return (Paths.Normalize(loc), false);
        }

        foreach (var cmd in new[] { p.UninstallString, p.QuietUninstallString, p.DisplayIcon })
        {
            var exe = CommandLine.Executable(cmd?.Split(',')[0]);
            if (exe is null || !Path.IsPathRooted(exe) || exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                continue;
            var dir = Path.GetDirectoryName(exe);
            if (dir is null || _ctx.IsTooBroad(dir))
                continue;
            if (dir.Contains("InstallShield Installation Information", StringComparison.OrdinalIgnoreCase) || dir.Contains("Package Cache", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_ctx.DataBases.Any(b => Paths.IsUnder(dir, b)))
                continue;
            note = $"InstallLocation boş; kurulum klasörü kaldırıcının konumundan çıkarıldı: {dir}";
            return (Paths.Normalize(dir), true);
        }
        return (null, false);
    }

    void AddInstallFolder(Builder b, bool inferred)
    {
        if (b.InstallDir is not { } dir || !_ctx.Probe.DirectoryExists(dir))
            return;
        var ev = new List<Evidence> { inferred ? Confidence.InstallDirInferred(dir) : Confidence.InstallDirExact(dir) };
        AddNameEvidence(ev, Path.GetFileName(dir), b.Program);
        AddFolderIdentity(ev, dir, b.Program);
        AddFolder(b, dir, ev, isInstallDir: true);
    }

    void ScanDataFolders(Builder b)
    {
        var p = b.Program;
        foreach (var root in _ctx.DataBases)
        {
            foreach (var d1 in SafeDirs(root))
            {
                var name1 = Path.GetFileName(d1);
                var isPublisher = NameMatcher.MatchesPublisher(name1, p.Publisher);
                var match1 = NameMatcher.Match(name1, p.DisplayName);
                if (match1 == NameMatch.Exact || match1 == NameMatch.Partial && !isPublisher)
                {
                    var ev = new List<Evidence>();
                    AddNameEvidence(ev, name1, p);
                    if (isPublisher)
                        ev.Add(Confidence.PublisherMatch(name1));
                    AddFolderIdentity(ev, d1, p);
                    AddInstallTime(ev, d1, p);
                    AddFolder(b, d1, ev, isInstallDir: false, publisherLevel: isPublisher && match1 != NameMatch.Exact);
                }
                if (!isPublisher)
                    continue;
                var shared = NameMatcher.IsSharedPublisher(name1);
                foreach (var d2 in SafeDirs(d1))
                {
                    var name2 = Path.GetFileName(d2);
                    var match2 = NameMatcher.Match(name2, p.DisplayName);
                    if (match2 == NameMatch.None || shared && match2 != NameMatch.Exact)
                        continue;
                    var ev = new List<Evidence> { Confidence.PublisherMatch(name1) };
                    AddNameEvidence(ev, name2, p);
                    AddFolderIdentity(ev, d2, p);
                    AddInstallTime(ev, d2, p);
                    AddFolder(b, d2, ev, isInstallDir: false);
                }
            }
        }
    }

    void AddFolder(Builder b, string path, List<Evidence> ev, bool isInstallDir, bool publisherLevel = false)
    {
        var norm = Paths.Normalize(path);
        if (publisherLevel || NameMatcher.IsSharedPublisher(Path.GetFileName(norm)))
        {
            b.Block(LeftoverKind.Folder, norm, "Yayıncı düzeyinde klasör; yalnız ürün alt klasörüne dokunulur");
            return;
        }
        if (GateFolder(norm) is { } reason)
        {
            b.Block(LeftoverKind.Folder, norm, reason);
            return;
        }
        if (OtherInstallOverlap(norm, b.Program) is { } other)
        {
            b.Block(LeftoverKind.Folder, norm, $"Başka kurulu programın klasörüyle çakışıyor: {other.DisplayName}");
            return;
        }
        var underInstall = b.InstallDir is { } dir && Paths.IsUnder(norm, dir);
        if (!isInstallDir && !underInstall && IsPortableAppFolder(norm))
        {
            b.Block(LeftoverKind.Folder, norm, "Taşınabilir uygulama klasörü olabilir (içinde çalıştırılabilir dosya var, kurulum kaydı yok)");
            return;
        }
        if (!isInstallDir && b.InstallDir is { } inst && Paths.IsUnder(norm, inst) && !ev.Any(e => e.Anchor == AnchorClass.InstallFolder))
            ev.Add(Confidence.InsideInstallDir(norm));
        AddOtherProgramPenalty(ev, Path.GetFileName(norm), b.Program);
        Add(b, LeftoverKind.Folder, norm, ev, bytes: FolderSize.MeasureAny(norm));
    }

    public string? GateFolder(string path)
    {
        string norm;
        try
        {
            norm = Paths.Normalize(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Geçersiz yol";
        }
        if (_ctx.IsUserData(norm))
            return "Kullanıcı klasörü (İndirilenler, Masaüstü, Belgeler vb.) asla kalıntı sayılmaz";
        if (_ctx.IsTooBroad(norm))
            return "Genel sistem ya da kullanıcı klasörü; kalıntı olamaz";
        var verdict = _ctx.Protection.CheckPath(norm);
        if (!verdict.Allowed)
            return $"Korumalı liste: {verdict.Reason}";
        return null;
    }

    public string? GateCandidate(LeftoverCandidate c, InstalledProgram program) =>
        c.Kind == LeftoverKind.Shortcut && IsProgramShortcut(c.Target, c.Detail, program) ? null : GateFolder(c.Target);

    public bool IsProgramShortcut(string lnk, string? target, InstalledProgram program)
    {
        if (target is null || program.InstallLocation is not { } loc || string.IsNullOrWhiteSpace(loc) || _ctx.IsTooBroad(loc))
            return false;
        string norm, dir;
        try
        {
            norm = Paths.Normalize(lnk);
            dir = Paths.Normalize(loc);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        if (!norm.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || Directory.Exists(norm))
            return false;
        var parent = Path.GetDirectoryName(norm);
        var placed = _ctx.ShortcutDirs.Any(d => SafeIsUnder(norm, d) && !norm.Equals(d, StringComparison.OrdinalIgnoreCase))
            || _ctx.DesktopDirs.Any(d => d.Equals(parent, StringComparison.OrdinalIgnoreCase));
        if (!placed || !_ctx.Protection.CheckPath(dir).Allowed)
            return false;
        return SafeIsUnder(target, dir) && !target.Equals(dir, StringComparison.OrdinalIgnoreCase);
    }

    InstalledProgram? OtherInstallOverlap(string path, InstalledProgram self)
    {
        foreach (var other in _ctx.Programs)
        {
            if (other.Id == self.Id || other.InstallLocation is not { } loc || _ctx.IsTooBroad(loc))
                continue;
            if (self.InstallLocation is { } mine && Paths.Normalize(mine).Equals(Paths.Normalize(loc), StringComparison.OrdinalIgnoreCase) && SameProduct(self, other))
                continue;
            if (Paths.IsUnder(path, loc) || Paths.IsUnder(loc, path))
                return other;
        }
        return null;
    }

    static bool SameProduct(InstalledProgram a, InstalledProgram b) =>
        string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);

    bool IsPortableAppFolder(string folder)
    {
        try
        {
            if (File.Exists(Path.Combine(folder, "App", "AppInfo", "appinfo.ini")))
                return true;
            if (Directory.EnumerateFiles(folder, "portable*", SearchOption.TopDirectoryOnly).Any())
                return true;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return Directory.EnumerateFiles(folder, "*.exe", options).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    void AddNameEvidence(List<Evidence> ev, string? name, InstalledProgram p)
    {
        switch (NameMatcher.Match(name, p.DisplayName))
        {
            case NameMatch.Exact:
                ev.Add(Confidence.NameExact(name!));
                break;
            case NameMatch.Partial:
                ev.Add(Confidence.NamePartial(name!));
                break;
        }
    }

    void AddOtherProgramPenalty(List<Evidence> ev, string? name, InstalledProgram self)
    {
        var mine = NameMatcher.Match(name, self.DisplayName);
        if (mine == NameMatch.None)
            return;
        foreach (var other in _ctx.Programs)
        {
            if (other.Id == self.Id || SameProduct(self, other))
                continue;
            var theirs = NameMatcher.Match(name, other.DisplayName);
            if (theirs != NameMatch.None && theirs >= mine)
            {
                ev.Add(Confidence.OtherProgramName(other.DisplayName));
                return;
            }
        }
    }

    void AddInstallTime(List<Evidence> ev, string dir, InstalledProgram p)
    {
        if (p.InstallDate is not { } date)
            return;
        try
        {
            var created = DateOnly.FromDateTime(Directory.GetCreationTime(dir));
            if (Math.Abs(created.DayNumber - date.DayNumber) <= 1)
                ev.Add(Confidence.InstallTime());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    void AddFolderIdentity(List<Evidence> ev, string dir, InstalledProgram p)
    {
        IEnumerable<string> exes;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            exes = Directory.EnumerateFiles(dir, "*.exe", options).Where(f => !IsUninstallerName(f)).Take(4).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        AddExeIdentity(ev, exes, p);
    }

    static bool IsUninstallerName(string file)
    {
        var n = Path.GetFileName(file);
        return n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || n.StartsWith("uninst", StringComparison.OrdinalIgnoreCase);
    }

    void AddExeIdentity(List<Evidence> ev, IEnumerable<string> exes, InstalledProgram p)
    {
        bool company = false, product = false, signer = false, mismatch = false;
        string? mismatchName = null;
        foreach (var exe in exes)
        {
            if (_ctx.Probe.Identity(exe) is not { } id)
                continue;
            if (!company && id.CompanyName is { } c)
            {
                if (NameMatcher.MatchesPublisher(c, p.Publisher) || NameMatcher.MatchesPublisher(p.Publisher, c))
                {
                    company = true;
                    ev.Add(Confidence.CompanyMatch(c));
                }
                else if (p.Publisher is not null && !mismatch)
                {
                    mismatch = true;
                    mismatchName = c;
                }
            }
            if (!product && id.ProductName is { } pn && NameMatcher.Match(pn, p.DisplayName) != NameMatch.None)
            {
                product = true;
                ev.Add(Confidence.ProductMatch(pn));
            }
            if (!signer && id.Signer is { } s && (NameMatcher.MatchesPublisher(s, p.Publisher) || NameMatcher.MatchesPublisher(p.Publisher, s)))
            {
                signer = true;
                ev.Add(Confidence.SignerMatch(s, id.SignatureTrusted));
            }
        }
        if (mismatch && !company && mismatchName is not null)
            ev.Add(Confidence.CompanyMismatch(mismatchName));
    }

    void ScanSoftwareKeys(Builder b)
    {
        var p = b.Program;
        var roots = new[]
        {
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, "SOFTWARE"),
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry32, "SOFTWARE"),
            new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, "Software"),
        };
        foreach (var root in roots)
        {
            foreach (var k1 in _ctx.Registry.GetSubKeyNames(root))
            {
                if (SkipSoftwareKeys.Contains(k1) && !NameMatcher.MatchesPublisher(k1, p.Publisher))
                    continue;
                var key1 = root.Child(k1);
                var isPublisher = NameMatcher.MatchesPublisher(k1, p.Publisher);
                var match1 = NameMatcher.Match(k1, p.DisplayName);
                if (!SkipSoftwareKeys.Contains(k1) && (match1 == NameMatch.Exact || match1 == NameMatch.Partial && !isPublisher))
                {
                    var ev = new List<Evidence>();
                    AddNameEvidence(ev, k1, p);
                    if (isPublisher)
                        ev.Add(Confidence.PublisherMatch(k1));
                    AddKeyReferences(ev, key1, b.InstallDir);
                    AddKey(b, key1, ev, publisherLevel: NameMatcher.IsSharedPublisher(k1));
                }
                if (!isPublisher)
                    continue;
                var shared = NameMatcher.IsSharedPublisher(k1);
                foreach (var k2 in _ctx.Registry.GetSubKeyNames(key1))
                {
                    var match2 = NameMatcher.Match(k2, p.DisplayName);
                    if (match2 == NameMatch.None || shared && match2 != NameMatch.Exact)
                        continue;
                    var key2 = key1.Child(k2);
                    var ev = new List<Evidence> { Confidence.PublisherMatch(k1) };
                    AddNameEvidence(ev, k2, p);
                    AddKeyReferences(ev, key2, b.InstallDir);
                    AddKey(b, key2, ev, publisherLevel: false);
                }
            }
        }
    }

    void AddKey(Builder b, RegKeyRef key, List<Evidence> ev, bool publisherLevel)
    {
        var depth = key.Path.Split('\\').Length;
        if (publisherLevel || depth < 2)
        {
            b.Block(LeftoverKind.RegistryKey, key.Display, "Yayıncı düzeyinde anahtar; yalnız ürün alt anahtarına dokunulur");
            return;
        }
        if (IsProtectedKey(key))
        {
            b.Block(LeftoverKind.RegistryKey, key.Display, "Sistem anahtarı");
            return;
        }
        AddOtherProgramPenalty(ev, key.Name, b.Program);
        Add(b, LeftoverKind.RegistryKey, key.Display, ev, key: key);
    }

    static bool IsProtectedKey(RegKeyRef key)
    {
        var p = key.Path;
        return p.StartsWith(@"SOFTWARE\Microsoft\Windows", StringComparison.OrdinalIgnoreCase) && !p.StartsWith(InstalledPrograms.UninstallPath + "\\", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(@"SOFTWARE\Classes\CLSID", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(@"SOFTWARE\Policies", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase) && !p.StartsWith(ServicesPath + "\\", StringComparison.OrdinalIgnoreCase);
    }

    void AddKeyReferences(List<Evidence> ev, RegKeyRef key, string? installDir)
    {
        if (installDir is null)
            return;
        var checkedValues = 0;
        foreach (var k in new[] { key }.Concat(_ctx.Registry.GetSubKeyNames(key).Take(20).Select(key.Child)))
        {
            foreach (var name in _ctx.Registry.GetValueNames(k))
            {
                if (++checkedValues > 200)
                    return;
                var v = _ctx.Registry.GetValue(k, name);
                if (v?.Kind is not (RegKind.String or RegKind.ExpandString) || v.AsString is not { Length: > 3 } s)
                    continue;
                if (PointsInto(s, installDir))
                {
                    ev.Add(Confidence.ReferencesInstallDir($"{name} = {s}"));
                    return;
                }
            }
        }
    }

    static bool PointsInto(string value, string dir)
    {
        var v = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (v.Length >= 3 && v[1] == ':' && SafeIsUnder(v, dir))
            return true;
        return CommandLine.Paths(value).Any(path => SafeIsUnder(path, dir));
    }

    static bool SafeIsUnder(string path, string root)
    {
        try
        {
            return Paths.IsUnder(path, root);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    void AddUninstallKey(Builder b)
    {
        if (b.Program.Key is not { } key || !_ctx.Registry.KeyExists(key))
            return;
        var ev = new List<Evidence> { Confidence.UninstallKeySelf(), Confidence.NameExact(b.Program.DisplayName) };
        Add(b, LeftoverKind.RegistryKey, key.Display, ev, key: key);
    }

    void ScanServices(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        var root = new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, ServicesPath);
        foreach (var name in _ctx.Registry.GetSubKeyNames(root))
        {
            var key = root.Child(name);
            var image = _ctx.Registry.GetString(key, "ImagePath");
            if (image is null)
                continue;
            var exe = CommandLine.Executable(image);
            if (exe is null || !SafeIsUnder(exe, dir))
                continue;
            var ev = new List<Evidence> { Confidence.InsideInstallDir(exe) };
            AddNameEvidence(ev, _ctx.Registry.GetString(key, "DisplayName") ?? name, b.Program);
            AddExeIdentity(ev, [exe], b.Program);
            Add(b, LeftoverKind.Service, name, ev, key: key, detail: image);
        }
    }

    void ScanTasks(Builder b)
    {
        if (b.InstallDir is not { } dir || _ctx.TasksDir is not { } tasks)
            return;
        foreach (var task in ScheduledTasks.Enumerate(tasks))
        {
            var hit = task.Commands.SelectMany(CommandLine.Paths).Concat(task.Commands.Select(c => CommandLine.Executable(c) ?? "")).FirstOrDefault(p => p.Length > 3 && SafeIsUnder(p, dir));
            if (hit is null)
                continue;
            var ev = new List<Evidence> { Confidence.InsideInstallDir(hit) };
            AddNameEvidence(ev, Path.GetFileName(task.TaskPath), b.Program);
            AddExeIdentity(ev, [hit], b.Program);
            Add(b, LeftoverKind.ScheduledTask, task.TaskPath, ev, detail: task.File);
        }
    }

    void ScanRunKeys(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        var roots = new List<RegKeyRef>();
        foreach (var path in RunKeys)
        {
            roots.Add(new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, path));
            roots.Add(new RegKeyRef(RegHive.LocalMachine, RegView.Registry32, path));
            roots.Add(new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, path));
        }
        foreach (var key in roots)
        {
            foreach (var name in _ctx.Registry.GetValueNames(key))
            {
                var cmd = _ctx.Registry.GetString(key, name);
                if (cmd is null)
                    continue;
                var hit = CommandLine.Paths(cmd).FirstOrDefault(p => SafeIsUnder(p, dir));
                if (hit is null)
                    continue;
                var ev = new List<Evidence> { Confidence.InsideInstallDir(hit) };
                AddNameEvidence(ev, name, b.Program);
                AddExeIdentity(ev, [hit], b.Program);
                Add(b, LeftoverKind.StartupEntry, $"{key.Display}\\{name}", ev, key: key, valueName: name, detail: cmd);
            }
        }
    }

    void ScanShortcuts(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        foreach (var root in _ctx.ShortcutDirs.Concat(_ctx.DesktopDirs))
        {
            IEnumerable<string> links;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = !_ctx.DesktopDirs.Contains(root, StringComparer.OrdinalIgnoreCase), IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                links = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.lnk", options).ToList() : [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var lnk in links)
            {
                var target = ShellLink.ReadTarget(lnk);
                if (target is null || !SafeIsUnder(target, dir))
                    continue;
                var norm = Paths.Normalize(lnk);
                var own = IsProgramShortcut(norm, target, b.Program);
                if (!own && GateFolder(norm) is { } reason)
                {
                    b.Block(LeftoverKind.Shortcut, norm, $"{reason}; kısayol elle silinebilir");
                    continue;
                }
                var ev = new List<Evidence> { Confidence.InsideInstallDir(target) };
                AddNameEvidence(ev, Path.GetFileNameWithoutExtension(lnk), b.Program);
                AddExeIdentity(ev, [target], b.Program);
                Add(b, LeftoverKind.Shortcut, norm, ev, detail: target, bytes: FolderSize.MeasureAny(norm), forceHigh: own);
            }
        }
    }

    void ScanAssociations(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        var classRoots = new[]
        {
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes"),
            new RegKeyRef(RegHive.CurrentUser, RegView.Registry64, @"Software\Classes"),
        };
        foreach (var classes in classRoots)
        {
            var progIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = _ctx.Registry.GetSubKeyNames(classes);
            foreach (var n in names)
            {
                if (!n.StartsWith('.'))
                    continue;
                var ext = classes.Child(n);
                if (_ctx.Registry.GetString(ext, "") is { } def)
                    progIds.Add(def);
                foreach (var v in _ctx.Registry.GetValueNames(ext.Child("OpenWithProgids")))
                    if (v.Length > 0)
                        progIds.Add(v);
            }
            var apps = classes.Child("Applications");
            foreach (var a in _ctx.Registry.GetSubKeyNames(apps))
                progIds.Add("Applications\\" + a);

            foreach (var progId in progIds)
            {
                var key = classes.Child(progId);
                string? hit = null;
                foreach (var verb in new[] { "open", "edit", "play" })
                {
                    var cmd = _ctx.Registry.GetString(key.Child("shell").Child(verb).Child("command"), "");
                    hit = cmd is null ? null : CommandLine.Paths(cmd).FirstOrDefault(p => SafeIsUnder(p, dir));
                    if (hit is not null)
                        break;
                }
                if (hit is null)
                    continue;
                var ev = new List<Evidence> { Confidence.InsideInstallDir(hit) };
                AddNameEvidence(ev, progId.Split('.', '\\')[0], b.Program);
                AddExeIdentity(ev, [hit], b.Program);
                Add(b, LeftoverKind.FileAssociation, key.Display, ev, key: key, detail: hit);
            }
        }
    }

    void ScanFirewall(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        var key = new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, FirewallRulesPath);
        foreach (var id in _ctx.Registry.GetValueNames(key))
        {
            var rule = _ctx.Registry.GetString(key, id);
            if (rule is null)
                continue;
            var fields = ParseFirewallRule(rule);
            if (!fields.TryGetValue("App", out var app))
                continue;
            var exe = CommandLine.Clean(app);
            if (!SafeIsUnder(exe, dir))
                continue;
            var name = fields.GetValueOrDefault("Name") ?? id;
            var ev = new List<Evidence> { Confidence.InsideInstallDir(exe) };
            AddNameEvidence(ev, name, b.Program);
            AddExeIdentity(ev, [exe], b.Program);
            Add(b, LeftoverKind.FirewallRule, name, ev, key: key, valueName: id, detail: exe);
        }
    }

    public static Dictionary<string, string> ParseFirewallRule(string rule)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in rule.Split('|'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
                d.TryAdd(part[..eq], part[(eq + 1)..]);
        }
        return d;
    }

    static void Add(Builder b, LeftoverKind kind, string target, List<Evidence> ev, RegKeyRef? key = null, string? valueName = null, string? detail = null, long bytes = 0, bool forceHigh = false)
    {
        var (score, anchors, tier) = Confidence.Evaluate(ev);
        if (forceHigh)
        {
            ev.Add(Confidence.ProgramShortcut());
            tier = ConfidenceTier.High;
        }
        var id = kind switch
        {
            LeftoverKind.RegistryKey or LeftoverKind.Service or LeftoverKind.FileAssociation when key is not null => $"{kind}:{key.Identity}",
            LeftoverKind.StartupEntry or LeftoverKind.RegistryValue or LeftoverKind.FirewallRule when key is not null => $"{kind}:{key.Identity}|{valueName}",
            _ => $"{kind}:{target}",
        };
        id = id.ToLowerInvariant();
        var candidate = new LeftoverCandidate
        {
            Id = id,
            Kind = kind,
            Target = target,
            Detail = detail,
            Key = key,
            ValueName = valueName,
            Evidence = ev,
            Score = score,
            Anchors = anchors,
            Tier = tier,
            Reason = Confidence.Reason(ev, anchors, tier),
            Bytes = bytes,
        };
        if (b.Candidates.TryGetValue(id, out var existing) && existing.Score >= score)
            return;
        b.Candidates[id] = candidate;
    }

    static IEnumerable<LeftoverCandidate> CollapseNested(IEnumerable<LeftoverCandidate> all)
    {
        var list = all.ToList();
        var folders = list.Where(c => c.Kind == LeftoverKind.Folder).ToList();
        var keys = list.Where(c => c.Kind == LeftoverKind.RegistryKey && c.Key is not null).ToList();
        foreach (var c in list)
        {
            if (c.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut
                && folders.Any(f => !ReferenceEquals(f, c) && f.Tier >= c.Tier && Paths.IsUnder(c.Target, f.Target) && !f.Target.Equals(c.Target, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (c.Kind == LeftoverKind.RegistryKey && c.Key is { } k
                && keys.Any(o => !ReferenceEquals(o, c) && o.Tier >= c.Tier && o.Key!.Hive == k.Hive && o.Key.View == k.View
                    && k.Path.StartsWith(o.Key.Path + "\\", StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return c;
        }
    }

    static IEnumerable<string> SafeDirs(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return [];
            return Directory.EnumerateDirectories(root, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
