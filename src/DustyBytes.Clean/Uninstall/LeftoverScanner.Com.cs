namespace DustyBytes.Clean.Uninstall;

public sealed partial class LeftoverScanner
{
    const string ExplorerPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
    const string ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";

    static readonly string[] ComServers = ["InprocServer32", "LocalServer32", "InprocHandler32"];

    static readonly string[] ShellexOwners =
        ["*", "Directory", @"Directory\Background", "Folder", "Drive", "AllFilesystemObjects", "DesktopBackground"];

    static readonly string[] ShellexLists =
        ["ContextMenuHandlers", "PropertySheetHandlers", "DragDropHandlers", "CopyHookHandlers", "ColumnHandlers"];

    sealed record ComClass(string Clsid, List<Evidence> Evidence);

    void ScanCom(Builder b)
    {
        if (b.InstallDir is not { } dir)
            return;
        var roots = new[]
        {
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, @"SOFTWARE\Classes"),
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry32, @"SOFTWARE\Classes"),
            _ctx.UserClasses,
        };
        var classes = new Dictionary<string, ComClass>(StringComparer.OrdinalIgnoreCase);
        var typeLibs = new Dictionary<string, ComClass>(StringComparer.OrdinalIgnoreCase);
        var appIds = new Dictionary<string, ComClass>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var clsidRoot = root.Child("CLSID");
            foreach (var guid in _ctx.Registry.GetSubKeyNames(clsidRoot))
            {
                if (!RegistryGate.IsGuid(guid))
                    continue;
                var key = clsidRoot.Child(guid);
                var server = ComServerIn(key, dir);
                if (server is null)
                    continue;
                var ev = new List<Evidence> { Confidence.ComServerInDir(server) };
                var name = _ctx.Registry.GetString(key, "");
                AddNameEvidence(ev, name, b.Program);
                if (!ev.Any(e => e.Code is "name-exact" or "name-partial"))
                    AddNameEvidence(ev, Path.GetFileNameWithoutExtension(server), b.Program);
                AddExeIdentity(ev, [server], b.Program);
                Add(b, LeftoverKind.RegistryKey, key.Display, ev, key: key, detail: server);
                var com = new ComClass(guid, ev);
                classes.TryAdd(guid, com);
                if (_ctx.Registry.GetString(key.Child("TypeLib"), "") is { } tl && RegistryGate.IsGuid(tl))
                    typeLibs.TryAdd(tl, com);
                if (_ctx.Registry.GetString(key, "AppID") is { } app && RegistryGate.IsGuid(app))
                    appIds.TryAdd(app, com);
                foreach (var pidName in new[] { "ProgID", "VersionIndependentProgID" })
                {
                    if (_ctx.Registry.GetString(key.Child(pidName), "") is not { } progId)
                        continue;
                    var pk = root.Child(progId);
                    if (string.Equals(_ctx.Registry.GetString(pk.Child("CLSID"), ""), guid, StringComparison.OrdinalIgnoreCase))
                        AddLinkedKey(b, pk, com, $"COM sınıfı {guid}");
                }
            }

            var tlRoot = root.Child("TypeLib");
            foreach (var guid in _ctx.Registry.GetSubKeyNames(tlRoot))
            {
                if (!RegistryGate.IsGuid(guid))
                    continue;
                var key = tlRoot.Child(guid);
                if (typeLibs.TryGetValue(guid, out var owner))
                {
                    AddLinkedKey(b, key, owner, $"COM sınıfı {owner.Clsid}");
                    continue;
                }
                if (TypeLibFileIn(key, dir) is not { } file)
                    continue;
                var ev = new List<Evidence> { Confidence.ComServerInDir(file) };
                AddNameEvidence(ev, _ctx.Registry.GetSubKeyNames(key).Select(v => _ctx.Registry.GetString(key.Child(v), "")).FirstOrDefault(n => n is not null), b.Program);
                AddExeIdentity(ev, [file], b.Program);
                Add(b, LeftoverKind.RegistryKey, key.Display, ev, key: key, detail: file);
                typeLibs.TryAdd(guid, new ComClass(guid, ev));
            }
        }

        if (classes.Count == 0 && typeLibs.Count == 0)
            return;

        foreach (var root in roots)
        {
            var ifRoot = root.Child("Interface");
            foreach (var iid in _ctx.Registry.GetSubKeyNames(ifRoot))
            {
                if (!RegistryGate.IsGuid(iid))
                    continue;
                var key = ifRoot.Child(iid);
                var owner = _ctx.Registry.GetString(key.Child("ProxyStubClsid32"), "") is { } ps && classes.TryGetValue(ps, out var c1) ? c1
                    : _ctx.Registry.GetString(key.Child("TypeLib"), "") is { } tl && typeLibs.TryGetValue(tl, out var c2) ? c2
                    : null;
                if (owner is not null)
                    AddLinkedKey(b, key, owner, $"COM {owner.Clsid}");
            }

            var appRoot = root.Child("AppID");
            foreach (var (appId, owner) in appIds)
            {
                var key = appRoot.Child(appId);
                if (_ctx.Registry.KeyExists(key))
                    AddLinkedKey(b, key, owner, $"COM sınıfı {owner.Clsid}");
            }

            foreach (var owner in ShellexOwners)
            {
                foreach (var list in ShellexLists)
                {
                    var listKey = root.Child(owner).Child("shellex").Child(list);
                    foreach (var h in _ctx.Registry.GetSubKeyNames(listKey))
                    {
                        var hk = listKey.Child(h);
                        var clsid = RegistryGate.IsGuid(h) ? h : _ctx.Registry.GetString(hk, "");
                        if (clsid is not null && classes.TryGetValue(clsid, out var com))
                            AddLinkedKey(b, hk, com, $"kabuk uzantısı {clsid}");
                    }
                }
            }
        }

        foreach (var view in new[] { RegView.Registry64, RegView.Registry32 })
        {
            var overlays = new RegKeyRef(RegHive.LocalMachine, view, ExplorerPath + @"\ShellIconOverlayIdentifiers");
            foreach (var name in _ctx.Registry.GetSubKeyNames(overlays))
            {
                var key = overlays.Child(name);
                if (_ctx.Registry.GetString(key, "") is { } clsid && classes.TryGetValue(clsid, out var com))
                    AddLinkedKey(b, key, com, $"simge kaplaması {clsid}");
            }
        }

        foreach (var approved in new[]
        {
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry64, ApprovedPath),
            new RegKeyRef(RegHive.LocalMachine, RegView.Registry32, ApprovedPath),
            _ctx.User(ApprovedPath),
        })
        {
            foreach (var (clsid, com) in classes)
                AddLinkedValue(b, approved, clsid, com.Evidence, $"onaylı kabuk uzantısı {clsid}");
        }
    }

    string? ComServerIn(RegKeyRef clsid, string dir)
    {
        foreach (var server in ComServers)
        {
            if (_ctx.Registry.GetString(clsid.Child(server), "") is not { } raw)
                continue;
            var path = CommandLine.Executable(raw) ?? CommandLine.Clean(raw.Trim('"'));
            if (SafeIsUnder(CommandLine.Clean(path), dir))
                return CommandLine.Clean(path);
        }
        return null;
    }

    string? TypeLibFileIn(RegKeyRef typeLib, string dir)
    {
        foreach (var version in _ctx.Registry.GetSubKeyNames(typeLib).Take(8))
        {
            var vk = typeLib.Child(version);
            foreach (var lcid in _ctx.Registry.GetSubKeyNames(vk).Take(8))
            {
                foreach (var platform in new[] { "win32", "win64" })
                {
                    if (_ctx.Registry.GetString(vk.Child(lcid).Child(platform), "") is { } raw
                        && CommandLine.Clean(raw.Trim('"')) is var file && SafeIsUnder(file, dir))
                        return file;
                }
            }
        }
        return null;
    }

    void AddLinkedKey(Builder b, RegKeyRef key, ComClass owner, string what)
    {
        var ev = owner.Evidence.Where(e => !Confidence.IsPathCode(e.Code)).ToList();
        ev.Insert(0, Confidence.LinkedTo(what));
        Add(b, LeftoverKind.RegistryKey, key.Display, ev, key: key, detail: what);
    }
}
