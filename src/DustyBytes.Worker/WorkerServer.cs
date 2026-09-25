using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DustyBytes.Core;
using DustyBytes.Core.Ipc;

namespace DustyBytes.Worker;

public static class WorkerWire
{
    public const string ProgressPrefix = "p:";
    public const string ResponsePrefix = "r:";
    public static readonly UTF8Encoding Utf8 = new(false);

    public static bool IsValidPipeName(string name) =>
        name.Length is > 0 and <= 200 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}

public sealed class WorkerServer
{
    readonly string _pipeName;
    readonly int _parentPid;
    readonly WorkerServices _services;
    readonly bool _watchParent;
    readonly CancellationTokenSource _stop = new();

    public WorkerServer(string pipeName, int parentPid, WorkerServices services, bool watchParent = true)
    {
        if (!WorkerWire.IsValidPipeName(pipeName))
            throw new ArgumentException("Geçersiz pipe adı", nameof(pipeName));
        _pipeName = pipeName;
        _parentPid = parentPid;
        _services = services;
        _watchParent = watchParent;
    }

    public event Action<int>? RejectedClient;

    public void Stop() => _stop.Cancel();

    PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        using var me = WindowsIdentity.GetCurrent();
        var self = me.User ?? throw new InvalidOperationException("Kullanıcı SID'i okunamadı");
        security.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.FullControl, AccessControlType.Allow));
        var caller = WorkerNative.ProcessUser(_parentPid);
        if (caller is not null && !caller.Equals(self))
            security.AddAccessRule(new PipeAccessRule(caller, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    NamedPipeServerStream CreatePipe(PipeSecurity security) =>
        NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            0,
            0,
            security);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var ct = linked.Token;
        if (_watchParent)
            WatchParent(linked);

        var security = BuildSecurity();
        while (!ct.IsCancellationRequested)
        {
            var pipe = CreatePipe(security);
            try
            {
                try
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var client = WorkerNative.ClientProcessId(pipe.SafePipeHandle);
                if (client != _parentPid)
                {
                    RejectedClient?.Invoke(client ?? -1);
                    pipe.Disconnect();
                    continue;
                }

                await ServeAsync(pipe, linked).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            finally
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    void WatchParent(CancellationTokenSource linked)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(_parentPid);
        }
        catch (ArgumentException)
        {
            linked.Cancel();
            return;
        }
        _ = parent.WaitForExitAsync(linked.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                linked.Cancel();
            parent.Dispose();
        }, TaskScheduler.Default);
    }

    async Task ServeAsync(NamedPipeServerStream pipe, CancellationTokenSource linked)
    {
        var ct = linked.Token;
        using var reader = new StreamReader(pipe, WorkerWire.Utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, WorkerWire.Utf8, 4096, leaveOpen: true) { AutoFlush = false };
        var writeLock = new object();

        void WriteLine(string line)
        {
            lock (writeLock)
            {
                writer.Write(line);
                writer.Write('\n');
                writer.Flush();
            }
        }

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (line is null)
                return;
            if (line.Length == 0)
                continue;

            WorkerRequest? request;
            try
            {
                request = JsonSerializer.Deserialize(line, IpcJson.Default.WorkerRequest);
            }
            catch (JsonException e)
            {
                WriteLine(WorkerWire.ResponsePrefix + JsonSerializer.Serialize(new WorkerResponse { Id = "", Ok = false, Message = "Geçersiz istek: " + e.Message }, IpcJson.Default.WorkerResponse));
                continue;
            }
            if (request is null)
                continue;

            var progress = new DirectProgress(p =>
            {
                try
                {
                    WriteLine(WorkerWire.ProgressPrefix + JsonSerializer.Serialize(p, IpcJson.Default.WorkerProgress));
                }
                catch (IOException)
                {
                }
            });

            var response = await HandleAsync(request, progress, ct).ConfigureAwait(false);
            WriteLine(WorkerWire.ResponsePrefix + JsonSerializer.Serialize(response, IpcJson.Default.WorkerResponse));

            if (request.Op == Ops.Shutdown && response.Ok)
            {
                linked.Cancel();
                return;
            }
        }
    }

    public async Task<WorkerResponse> HandleAsync(WorkerRequest request, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        if (WorkerHandlers.Destructive.Contains(request.Op) && !request.UserApproved)
            return new WorkerResponse
            {
                Id = request.Id,
                Ok = false,
                DryRun = DryRun.Enabled,
                Message = "Kullanıcı onayı yok; yıkıcı işlem reddedildi",
            };
        try
        {
            if (WorkerHandlers.BuiltIn.Contains(request.Op))
                return await _services.ExecuteAsync(request, progress, ct).ConfigureAwait(false);
            if (WorkerHandlers.TryGet(request.Op, out var handler))
                return await handler(request, progress, ct).ConfigureAwait(false);
            return new WorkerResponse { Id = request.Id, Ok = false, Message = $"Bu işlem için worker'da işleyici yok: {request.Op}" };
        }
        catch (OperationCanceledException)
        {
            return new WorkerResponse { Id = request.Id, Ok = false, Message = "İşlem iptal edildi" };
        }
        catch (Exception e)
        {
            return new WorkerResponse { Id = request.Id, Ok = false, DryRun = DryRun.Enabled, Message = $"İşlem başarısız: {e.Message}" };
        }
    }

    sealed class DirectProgress(Action<WorkerProgress> report) : IProgress<WorkerProgress>
    {
        public void Report(WorkerProgress value) => report(value);
    }
}
