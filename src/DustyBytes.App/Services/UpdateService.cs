using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DustyBytes.Core;

namespace DustyBytes.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string? ZipUrl, string? ShaUrl, string? Sha256, long Size, bool Simulated);

public enum DownloadStatus
{
    Ok,
    HashMismatch,
    NoHash,
    Failed,
}

public sealed record DownloadResult(DownloadStatus Status, string? ZipPath, string? Error)
{
    public bool Ok => Status == DownloadStatus.Ok;
}

public sealed class UpdateOptions
{
    public const string FakeVariable = "DUSTYBYTES_FAKE_UPDATE";

    public string Root { get; init; } = Path.Combine(Paths.AppData, "update");
    public string TargetDir { get; init; } = AppContext.BaseDirectory;
    public string? ExePath { get; init; } = Environment.ProcessPath;
    public int ProcessId { get; init; } = Environment.ProcessId;
    public long BytesPerSecond { get; init; } = 4L * 1024 * 1024;
    public long BusyBytesPerSecond { get; init; } = 256L * 1024;
    public bool Simulate { get; init; }
    public Version? FakeVersion { get; init; }
    public TimeSpan SimulatedStep { get; init; } = TimeSpan.FromMilliseconds(80);
    public Func<ProcessStartInfo, bool> Launch { get; init; } = psi => Process.Start(psi) is not null;

    public static UpdateOptions FromEnvironment()
    {
        var fake = UpdateService.ParseTag(Environment.GetEnvironmentVariable(FakeVariable));
        return new UpdateOptions { FakeVersion = fake, Simulate = DryRun.Enabled || fake is not null };
    }
}

public sealed partial class UpdateService
{
    public const string LatestUrl = "https://api.github.com/repos/Teknesyum/DustyBytes/releases/latest";
    public const string ZipName = "DustyBytes-win-x64.zip";
    public const string ShaName = ZipName + ".sha256";
    public const string ScriptName = "apply.ps1";

    readonly HttpClient _http;

    public UpdateService(HttpClient http, Version current, UpdateOptions options)
    {
        _http = http;
        Current = Normalize(current);
        Options = options;
    }

    public Version Current { get; }
    public UpdateOptions Options { get; }
    public bool Simulate => Options.Simulate;

    public static UpdateService FromEnvironment()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        return new UpdateService(http, CurrentVersion(), UpdateOptions.FromEnvironment());
    }

    public static Version CurrentVersion() =>
        Normalize((Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
            text = text[..cut];
        if (!text.Contains('.'))
            text += ".0";
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    public static string? FindSha(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var m = ShaPattern().Match(text);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])")]
    private static partial Regex ShaPattern();

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (Options.FakeVersion is { } fake)
            return IsNewer(fake, Current) ? new UpdateInfo(fake, "v" + fake.ToString(3), null, null, null, 0, true) : null;
        try
        {
            using var request = Request(LatestUrl, "application/vnd.github+json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var release = await JsonSerializer.DeserializeAsync(stream, UpdateJson.Default.GitHubRelease, ct);
            if (release is null || release.Draft || release.Prerelease)
                return null;
            var version = ParseTag(release.TagName);
            if (version is null || !IsNewer(version, Current))
                return null;
            var assets = release.Assets ?? [];
            var zip = assets.FirstOrDefault(a => a.Name.Equals(ZipName, StringComparison.OrdinalIgnoreCase));
            if (zip is null)
                return null;
            var sha = assets.FirstOrDefault(a => a.Name.Equals(ShaName, StringComparison.OrdinalIgnoreCase));
            return new UpdateInfo(version, release.TagName, zip.DownloadUrl, sha?.DownloadUrl, FindSha(release.Body), zip.Size, Simulate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task<DownloadResult> DownloadAsync(UpdateInfo info, Action<double> progress, Func<bool> busy, CancellationToken ct = default)
    {
        var done = new TaskCompletionSource<DownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(info.Simulated ? SimulateDownload(progress, ct) : Download(info, progress, busy, ct));
            }
            catch (Exception e)
            {
                done.SetResult(new DownloadResult(DownloadStatus.Failed, null, e.Message));
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "DustyBytes update download",
        };
        thread.Start();
        return done.Task;
    }

    DownloadResult SimulateDownload(Action<double> progress, CancellationToken ct)
    {
        for (var p = 0; p <= 100; p += 4)
        {
            ct.ThrowIfCancellationRequested();
            progress(p / 100.0);
            Thread.Sleep(Options.SimulatedStep);
        }
        return new DownloadResult(DownloadStatus.Ok, null, null);
    }

    DownloadResult Download(UpdateInfo info, Action<double> progress, Func<bool> busy, CancellationToken ct)
    {
        if (info.ZipUrl is null)
            return new DownloadResult(DownloadStatus.Failed, null, "İndirme adresi yok");
        var expected = info.Sha256 ?? FetchSha(info.ShaUrl, ct);
        if (expected is null)
            return new DownloadResult(DownloadStatus.NoHash, null, "SHA-256 değeri bulunamadı");

        var dir = VersionDir(info.Version);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, ZipName);
        var part = zip + ".part";

        using var request = Request(info.ZipUrl, "application/octet-stream");
        using var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? info.Size;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var source = response.Content.ReadAsStream(ct))
        using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[64 * 1024];
            long received = 0;
            var clock = Stopwatch.StartNew();
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                target.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
                received += read;
                if (total > 0)
                    progress(Math.Min(1.0, (double)received / total));
                Throttle(read, busy(), clock);
            }
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            return new DownloadResult(DownloadStatus.HashMismatch, null, "SHA-256 uyuşmadı");
        }
        File.Move(part, zip, true);
        progress(1.0);
        return new DownloadResult(DownloadStatus.Ok, zip, null);
    }

    void Throttle(int bytes, bool busy, Stopwatch clock)
    {
        var limit = busy ? Options.BusyBytesPerSecond : Options.BytesPerSecond;
        if (limit <= 0)
            return;
        var wanted = TimeSpan.FromSeconds((double)bytes / limit);
        var spent = clock.Elapsed;
        if (spent < wanted)
            Thread.Sleep(wanted - spent);
        clock.Restart();
    }

    string? FetchSha(string? url, CancellationToken ct)
    {
        if (url is null)
            return null;
        using var request = Request(url, "application/octet-stream");
        using var response = _http.Send(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        using var reader = new StreamReader(response.Content.ReadAsStream(ct), Encoding.UTF8);
        return FindSha(reader.ReadToEnd());
    }

    HttpRequestMessage Request(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DustyBytes", Current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    public string VersionDir(Version version) => Path.Combine(Options.Root, version.ToString(3));

    public ProcessStartInfo PrepareInstall(UpdateInfo info, string zipPath)
    {
        var dir = VersionDir(info.Version);
        var app = Path.Combine(dir, "app");
        if (Directory.Exists(app))
            Directory.Delete(app, true);
        ZipFile.ExtractToDirectory(zipPath, app, true);
        var source = app;
        if (Directory.GetFiles(app).Length == 0 && Directory.GetDirectories(app) is [var only])
            source = only;

        var script = Path.Combine(dir, ScriptName);
        File.WriteAllText(script, ApplyScript, new UTF8Encoding(true));
        var exe = Options.ExePath ?? Path.Combine(Options.TargetDir, "DustyBytes.exe");

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-ProcessId", Options.ProcessId.ToString(), "-Source", source,
                     "-Target", Path.TrimEndingDirectorySeparator(Options.TargetDir), "-Exe", exe,
                 })
            psi.ArgumentList.Add(arg);
        return psi;
    }

    public bool Install(UpdateInfo info, string zipPath) => Options.Launch(PrepareInstall(info, zipPath));

    public static int CleanupOld(string dir)
    {
        var removed = 0;
        try
        {
            foreach (var old in Directory.EnumerateFiles(dir, "*.old", SearchOption.AllDirectories))
            {
                if (!File.Exists(old[..^4]))
                    continue;
                try
                {
                    File.Delete(old);
                    removed++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return removed;
    }

    public const string ApplyScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Exe)
        $ErrorActionPreference = 'Stop'
        $log = Join-Path (Split-Path -Parent $PSCommandPath) 'apply.log'
        function Write-Log([string]$Text) { Add-Content -LiteralPath $log -Value ((Get-Date -Format 's') + ' ' + $Text) }
        try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch { }
        Write-Log "start $Source -> $Target"
        $Source = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')
        $failed = 0
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $relative = $file.FullName.Substring($Source.Length).TrimStart('\')
            $dest = Join-Path $Target $relative
            $old = $dest + '.old'
            $folder = Split-Path -Parent $dest
            if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
            $placed = $false
            for ($i = 0; $i -lt 120 -and -not $placed; $i++) {
                try {
                    if (Test-Path -LiteralPath $dest) {
                        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
                        Rename-Item -LiteralPath $dest -NewName ([IO.Path]::GetFileName($old))
                    }
                    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
                    $placed = $true
                } catch { Start-Sleep -Milliseconds 250 }
            }
            if (-not $placed) {
                $failed++
                Write-Log "failed $relative"
                if (-not (Test-Path -LiteralPath $dest) -and (Test-Path -LiteralPath $old)) {
                    Rename-Item -LiteralPath $old -NewName ([IO.Path]::GetFileName($dest))
                }
            }
        }
        Write-Log "done, failed $failed"
        Start-Process -FilePath $Exe
        """;
}
