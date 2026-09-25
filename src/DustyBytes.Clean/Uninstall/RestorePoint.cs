using System.Runtime.InteropServices;
using DustyBytes.Core;

namespace DustyBytes.Clean.Uninstall;

public sealed record RestorePointResult(bool Ok, long Sequence, string Message);

public static unsafe partial class RestorePoint
{
    const int BeginSystemChange = 100;
    const int EndSystemChange = 101;
    const int ApplicationUninstall = 1;
    const int CancelledOperation = 13;
    const int ErrorServiceDisabled = 1058;
    const int ErrorBadEnvironment = 10;
    const int ErrorTimeout = 1460;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct RestorePointInfo
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        public fixed char szDescription[256];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct StateMgrStatus
    {
        public uint nStatus;
        public long llSequenceNumber;
    }

    [LibraryImport("srclient.dll", EntryPoint = "SRSetRestorePointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SRSetRestorePoint(RestorePointInfo* info, StateMgrStatus* status);

    public static RestorePointResult Create(string description)
    {
        if (DryRun.Enabled)
            return new(true, 0, $"Prova kipi: geri yükleme noktası oluşturulmadı ({description})");
        return Call(BeginSystemChange, ApplicationUninstall, 0, description);
    }

    public static RestorePointResult Complete(long sequence) =>
        DryRun.Enabled || sequence == 0 ? new(true, sequence, "") : Call(EndSystemChange, ApplicationUninstall, sequence, "");

    public static RestorePointResult Cancel(long sequence) =>
        DryRun.Enabled || sequence == 0 ? new(true, sequence, "") : Call(EndSystemChange, CancelledOperation, sequence, "");

    static RestorePointResult Call(int eventType, int type, long sequence, string description)
    {
        var info = new RestorePointInfo { dwEventType = eventType, dwRestorePtType = type, llSequenceNumber = sequence };
        var text = description.Length > 255 ? description[..255] : description;
        for (var i = 0; i < text.Length; i++)
            info.szDescription[i] = text[i];
        StateMgrStatus status;
        bool ok;
        try
        {
            ok = SRSetRestorePoint(&info, &status);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return new(false, 0, "Sistem Geri Yükleme bu sistemde yok");
        }
        if (ok)
            return new(true, status.llSequenceNumber, "Geri yükleme noktası oluşturuldu");
        var message = (int)status.nStatus switch
        {
            ErrorServiceDisabled => "Sistem Geri Yükleme kapalı",
            ErrorBadEnvironment => "Sistem Geri Yükleme bu ortamda çalışmıyor",
            ErrorTimeout => "Geri yükleme noktası zaman aşımına uğradı",
            _ => $"Geri yükleme noktası oluşturulamadı (kod {status.nStatus})",
        };
        return new(false, 0, message);
    }
}
