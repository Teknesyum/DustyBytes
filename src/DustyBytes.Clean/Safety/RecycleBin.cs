using System.Runtime.InteropServices;
using DustyBytes.Core;

namespace DustyBytes.Clean.Safety;

public sealed record RecycleBinItem(string ParsingPath, string Name, string OriginalLocation, DateTime? DeletedUtc, long Size)
{
    public string OriginalPath => Path.Combine(OriginalLocation, Name);
}

public sealed class RecycleBin
{
    readonly SafetyGate _gate;

    public RecycleBin(SafetyGate gate)
    {
        _gate = gate;
    }

    static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null)
            throw error;
        return result;
    }

    public OpResult Send(string path, bool includeUserData = false)
    {
        var verdict = _gate.Check(path, includeUserData);
        if (!verdict.Allowed)
            return OpResult.Denied(path, verdict, OpMethod.RecycleBin);
        var plain = Paths.Normalize(path);
        if (!FileTree.Exists(plain))
            return OpResult.NotFound(plain, OpMethod.RecycleBin);

        if (Native.GetDriveType(Path.GetPathRoot(plain) ?? plain) == Native.DRIVE_REMOTE)
            return OpResult.Failed(plain, OpMethod.RecycleBin, "Ağ sürücüsünde Geri Dönüşüm Kutusu yok; güvenli silme yolu bulunamadı, işlem reddedildi");

        var (bytes, _) = FileTree.Measure(plain);
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.RecycleBin, plain, $"{bytes} bayt Geri Dönüşüm Kutusu'na gidecekti");
            return new OpResult { Path = plain, Status = OpStatus.DryRun, Method = OpMethod.RecycleBin, Bytes = bytes, Message = "Prova: Geri Dönüşüm Kutusu'na gönderilecekti" };
        }

        try
        {
            return OnSta(() => SendCore(plain, bytes));
        }
        catch (COMException e)
        {
            return OpResult.Failed(plain, OpMethod.RecycleBin, $"Geri Dönüşüm Kutusu'na gönderilemedi: {e.Message}");
        }
    }

    static OpResult SendCore(string plain, long bytes)
    {
        var op = ShellNative.CreateFileOperation();
        op.SetOperationFlags(ShellNative.FOF_NO_UI | ShellNative.FOF_ALLOWUNDO | ShellNative.FOFX_EARLYFAILURE | ShellNative.FOFX_RECYCLEONDELETE);
        var sink = new RecycleSink();
        op.Advise(sink, out var cookie);
        try
        {
            var item = ShellNative.ItemFromPath(plain);
            op.DeleteItem(item, 0);
            var hr = op.PerformOperations();
            op.GetAnyOperationsAborted(out var aborted);
            if (sink.RefusedPermanentDelete)
                return OpResult.Failed(plain, OpMethod.RecycleBin, "Öğe Geri Dönüşüm Kutusu'na sığmıyor ya da birimde kutu yok; kalıcı silme reddedildi");
            if (hr < 0 || aborted || sink.DeleteResult < 0 || FileTree.Exists(plain))
                return OpResult.Failed(plain, OpMethod.RecycleBin, $"Geri Dönüşüm Kutusu'na gönderilemedi (0x{(hr < 0 ? hr : sink.DeleteResult):X8})");
            return new OpResult
            {
                Path = plain,
                Status = OpStatus.Done,
                Method = OpMethod.RecycleBin,
                Bytes = bytes,
                Id = sink.RecycledParsingPath,
                Message = "Geri Dönüşüm Kutusu'na gönderildi",
            };
        }
        finally
        {
            op.Unadvise(cookie);
        }
    }

    public IReadOnlyList<RecycleBinItem> List() => OnSta(ListCore);

    static List<RecycleBinItem> ListCore()
    {
        var result = new List<RecycleBinItem>();
        var hr = ShellNative.SHGetKnownFolderItem(ShellNative.FOLDERID_RecycleBinFolder, 0, 0, ShellNative.IID_IShellItem, out var binPtr);
        Marshal.ThrowExceptionForHR(hr);
        var bin = ShellNative.Wrap<IShellItem>(binPtr);
        bin.BindToHandler(0, ShellNative.BHID_EnumItems, ShellNative.IID_IEnumShellItems, out var enumPtr);
        var items = ShellNative.Wrap<IEnumShellItems>(enumPtr);
        while (items.Next(1, out var itemPtr, out var fetched) == ShellNative.S_OK && fetched == 1)
        {
            var item = ShellNative.Wrap<IShellItem>(itemPtr);
            if (Describe(item) is { } described)
                result.Add(described);
        }
        return result;
    }

    static RecycleBinItem? Describe(IShellItem item)
    {
        if (item is not IShellItem2 item2)
            return null;
        var parsing = ShellNative.DisplayName(item, ShellNative.SIGDN_DESKTOPABSOLUTEPARSING);
        if (parsing is null)
            return null;
        string? location = null;
        if (item2.GetString(ShellNative.SCID_ORIGINAL_LOCATION, out var locPtr) == ShellNative.S_OK)
            location = ShellNative.TakeString(locPtr);
        DateTime? deleted = null;
        if (item2.GetFileTime(ShellNative.SCID_DATE_DELETED, out var ft) == ShellNative.S_OK && ft > 0)
            deleted = DateTime.FromFileTimeUtc(ft);
        long size = 0;
        if (item2.GetUInt64(ShellNative.PKEY_Size, out var sz) == ShellNative.S_OK)
            size = (long)sz;
        var name = ShellNative.DisplayName(item, ShellNative.SIGDN_PARENTRELATIVEEDITING)
            ?? ShellNative.DisplayName(item, ShellNative.SIGDN_NORMALDISPLAY)
            ?? Path.GetFileName(parsing);
        return new RecycleBinItem(parsing, name, location ?? "", deleted, size);
    }

    public IReadOnlyList<RecycleBinItem> FindByOriginalPath(string originalPath)
    {
        var target = Paths.Normalize(originalPath);
        return List().Where(i => i.OriginalLocation.Length > 0 && string.Equals(Paths.Normalize(i.OriginalPath), target, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public OpResult Restore(RecycleBinItem item)
    {
        var target = item.OriginalPath;
        if (item.OriginalLocation.Length == 0)
            return OpResult.Failed(item.ParsingPath, OpMethod.Restore, "Orijinal konum okunamadı");
        if (FileTree.Exists(target))
            return new OpResult { Path = target, Status = OpStatus.Conflict, Method = OpMethod.Restore, Message = "Orijinal yolda başka bir öğe var; üzerine yazılmadı", Id = item.ParsingPath };
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Restore, target, "Geri Dönüşüm Kutusu'ndan geri yüklenecekti");
            return new OpResult { Path = target, Status = OpStatus.DryRun, Method = OpMethod.Restore, Message = "Prova: geri yüklenecekti", Id = item.ParsingPath };
        }
        var recreated = false;
        if (!Directory.Exists(item.OriginalLocation))
        {
            Directory.CreateDirectory(item.OriginalLocation);
            recreated = true;
        }
        try
        {
            return OnSta(() =>
            {
                var op = ShellNative.CreateFileOperation();
                op.SetOperationFlags(ShellNative.FOF_NO_UI);
                var source = ShellNative.ItemFromPath(item.ParsingPath);
                var folder = ShellNative.ItemFromPath(item.OriginalLocation);
                op.MoveItem(source, folder, item.Name, 0);
                var hr = op.PerformOperations();
                op.GetAnyOperationsAborted(out var aborted);
                if (hr < 0 || aborted || !FileTree.Exists(target))
                    return OpResult.Failed(target, OpMethod.Restore, $"Geri yüklenemedi (0x{hr:X8})");
                return new OpResult
                {
                    Path = target,
                    Status = OpStatus.Done,
                    Method = OpMethod.Restore,
                    Bytes = item.Size,
                    Id = item.ParsingPath,
                    Message = recreated ? "Geri yüklendi; üst klasör yeniden kuruldu" : "Geri yüklendi",
                };
            });
        }
        catch (COMException e)
        {
            return OpResult.Failed(target, OpMethod.Restore, $"Geri yüklenemedi: {e.Message}");
        }
    }

    public OpResult Purge(RecycleBinItem item)
    {
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Purge, item.OriginalPath, "Geri Dönüşüm Kutusu'ndan kalıcı silinecekti");
            return new OpResult { Path = item.OriginalPath, Status = OpStatus.DryRun, Method = OpMethod.Purge, Bytes = item.Size, Id = item.ParsingPath, Message = "Prova: kalıcı silinecekti" };
        }
        try
        {
            return OnSta(() =>
            {
                var op = ShellNative.CreateFileOperation();
                op.SetOperationFlags(ShellNative.FOF_NO_UI);
                op.DeleteItem(ShellNative.ItemFromPath(item.ParsingPath), 0);
                var hr = op.PerformOperations();
                op.GetAnyOperationsAborted(out var aborted);
                if (hr < 0 || aborted)
                    return OpResult.Failed(item.OriginalPath, OpMethod.Purge, $"Kalıcı silinemedi (0x{hr:X8})");
                return new OpResult { Path = item.OriginalPath, Status = OpStatus.Done, Method = OpMethod.Purge, Bytes = item.Size, Id = item.ParsingPath, Message = "Geri Dönüşüm Kutusu'ndan kalıcı silindi" };
            });
        }
        catch (COMException e)
        {
            return OpResult.Failed(item.OriginalPath, OpMethod.Purge, $"Kalıcı silinemedi: {e.Message}");
        }
    }
}
