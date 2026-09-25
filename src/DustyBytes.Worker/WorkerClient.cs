using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Worker;

public sealed class WorkerStartException(string message, bool elevationDenied = false, Exception? inner = null) : Exception(message, inner)
{
    public bool ElevationDenied { get; } = elevationDenied;
}

public sealed class WorkerClient : IAsyncDisposable
{
    const int ErrorCancelled = 1223;

    readonly NamedPipeClientStream _pipe;
    readonly StreamReader _reader;
    readonly StreamWriter _writer;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Process? _process;

    WorkerClient(NamedPipeClientStream pipe, Process? process)
    {
        _pipe = pipe;
        _process = process;
        _reader = new StreamReader(pipe, WorkerWire.Utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, WorkerWire.Utf8, 4096, leaveOpen: true) { AutoFlush = false };
    }

    public bool IsConnected => _pipe.IsConnected;
    public int? ServerProcessId => WorkerNative.ServerProcessId(_pipe.SafePipeHandle);
    public string PipeName { get; private init; } = "";

    public static string NewPipeName() => $"DustyBytes.Worker.{Environment.ProcessId}.{Guid.NewGuid():N}";

    public static async Task<WorkerClient> StartAsync(bool elevated, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var exe = Environment.ProcessPath ?? throw new WorkerStartException("Uygulama yolu okunamadı");
        var name = NewPipeName();
        var args = $"--worker --pipe {name} --parent {Environment.ProcessId}";
        if (DryRun.Enabled)
            args += " --dry-run";
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = true,
            Verb = elevated ? "runas" : "",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            throw new WorkerStartException("Yönetici izni verilmedi. Bu işlem için DustyBytes'ın yönetici olarak çalışması gerekiyor.", elevationDenied: true, e);
        }
        catch (Win32Exception e)
        {
            throw new WorkerStartException($"Worker başlatılamadı: {e.Message}", inner: e);
        }

        return await ConnectAsync(name, process, timeout ?? TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    public static Task<WorkerClient> ConnectAsync(string pipeName, int expectedServerPid, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        ConnectCoreAsync(pipeName, null, expectedServerPid, timeout, cancellationToken);

    static Task<WorkerClient> ConnectAsync(string pipeName, Process? process, TimeSpan timeout, CancellationToken cancellationToken) =>
        ConnectCoreAsync(pipeName, process, process?.Id, timeout, cancellationToken);

    static async Task<WorkerClient> ConnectCoreAsync(string pipeName, Process? process, int? expectedServerPid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            var connect = pipe.ConnectAsync(cts.Token);
            if (process is not null)
            {
                var exited = process.WaitForExitAsync(cts.Token);
                var first = await Task.WhenAny(connect, exited).ConfigureAwait(false);
                if (first == exited && !connect.IsCompleted)
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    var code = process.HasExited ? process.ExitCode : -1;
                    throw new WorkerStartException($"Worker bağlanmadan kapandı (çıkış kodu {code})");
                }
            }
            await connect.ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new WorkerStartException("Worker'a bağlanılamadı: zaman aşımı", inner: e);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (expectedServerPid is { } pid && WorkerNative.ServerProcessId(pipe.SafePipeHandle) != pid)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new WorkerStartException("Pipe'ın karşısındaki süreç beklenen worker değil; bağlantı kesildi");
        }

        return new WorkerClient(pipe, process) { PipeName = pipeName };
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request, IProgress<WorkerProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteAsync(JsonSerializer.Serialize(request, IpcJson.Default.WorkerRequest).AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("Worker bağlantısı kapandı");
                if (line.StartsWith(WorkerWire.ProgressPrefix, StringComparison.Ordinal))
                {
                    var p = JsonSerializer.Deserialize(line.AsSpan(WorkerWire.ProgressPrefix.Length), IpcJson.Default.WorkerProgress);
                    if (p is not null && p.Id == request.Id)
                        progress?.Report(p);
                    continue;
                }
                if (line.StartsWith(WorkerWire.ResponsePrefix, StringComparison.Ordinal))
                {
                    var response = JsonSerializer.Deserialize(line.AsSpan(WorkerWire.ResponsePrefix.Length), IpcJson.Default.WorkerResponse);
                    if (response is not null && (response.Id == request.Id || response.Id.Length == 0))
                        return response;
                }
            }
        }
        catch (OperationCanceledException)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _pipe.DisposeAsync().ConfigureAwait(false);
        _reader.Dispose();
        _process?.Dispose();
        _gate.Dispose();
    }
}
