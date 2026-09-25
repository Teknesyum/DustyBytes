using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace DustyBytes.Worker;

public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly CancellationTokenSource _stop = new();
    Task? _listener;

    SingleInstance(Mutex mutex, bool isFirst, string pipeName)
    {
        _mutex = mutex;
        IsFirst = isFirst;
        _pipeName = pipeName;
    }

    public bool IsFirst { get; }

    public event EventHandler<string[]>? Activated;

    static string UserKey()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.User?.Value ?? Environment.UserName;
    }

    public static SingleInstance Acquire(string appId = "DustyBytes")
    {
        var key = UserKey();
        var mutex = new Mutex(false, $@"Local\{appId}.{key}", out var createdNew);
        return new SingleInstance(mutex, createdNew, $"{appId}.Activate.{key}");
    }

    public void StartListening()
    {
        if (!IsFirst)
            throw new InvalidOperationException("Yalnız ilk örnek etkinleştirme dinler");
        _listener ??= Task.Run(ListenAsync);
    }

    PipeSecurity Security()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        using var id = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(id.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    async Task ListenAsync()
    {
        var ct = _stop.Token;
        var security = Security();
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
            }
            catch (IOException)
            {
                return;
            }
            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(pipe, WorkerWire.Utf8);
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    string[] args = [];
                    if (!string.IsNullOrEmpty(line))
                    {
                        try
                        {
                            args = JsonSerializer.Deserialize(line, WorkerJson.Default.StringArray) ?? [];
                        }
                        catch (JsonException)
                        {
                        }
                    }
                    Activated?.Invoke(this, args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                }
            }
        }
    }

    public async Task<bool> NotifyFirstAsync(string[] args, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, WorkerWire.Utf8);
            await writer.WriteAsync(JsonSerializer.Serialize(args, WorkerJson.Default.StringArray).AsMemory(), cts.Token).ConfigureAwait(false);
            await writer.WriteAsync("\n".AsMemory(), cts.Token).ConfigureAwait(false);
            await writer.FlushAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _mutex.Dispose();
        _stop.Dispose();
    }
}
