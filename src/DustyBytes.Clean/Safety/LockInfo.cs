using System.ComponentModel;
using System.Diagnostics;
using DustyBytes.Core;

namespace DustyBytes.Clean.Safety;

public static unsafe class LockInfo
{
    public static IReadOnlyList<LockHolder> Holders(string path) => Holders([path]);

    public static IReadOnlyList<LockHolder> Holders(IEnumerable<string> paths)
    {
        var files = paths.Select(p => Paths.FromLong(p)).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
            return [];

        var key = stackalloc char[Native.CCH_RM_SESSION_KEY + 1];
        var rc = Native.RmStartSession(out var session, 0, key);
        if (rc != Native.ERROR_SUCCESS)
            throw new Win32Exception(rc, "Restart Manager oturumu açılamadı");
        var arr = stackalloc char*[1];
        try
        {
            foreach (var file in files)
            {
                fixed (char* name = file)
                {
                    arr[0] = name;
                    rc = Native.RmRegisterResources(session, 1, arr, 0, 0, 0, 0);
                    if (rc != Native.ERROR_SUCCESS)
                        throw new Win32Exception(rc, "Restart Manager kaynağı kaydedilemedi");
                }
            }

            uint count = 0;
            Native.RM_PROCESS_INFO[] infos = [];
            for (var attempt = 0; attempt < 5; attempt++)
            {
                fixed (Native.RM_PROCESS_INFO* p = infos)
                {
                    var capacity = (uint)infos.Length;
                    count = capacity;
                    rc = Native.RmGetList(session, out var needed, ref count, p, out _);
                    if (rc == Native.ERROR_SUCCESS)
                        break;
                    if (rc != Native.ERROR_MORE_DATA)
                        throw new Win32Exception(rc, "Restart Manager listesi alınamadı");
                    infos = new Native.RM_PROCESS_INFO[needed + 2];
                }
            }
            if (rc != Native.ERROR_SUCCESS)
                throw new Win32Exception(rc, "Restart Manager listesi alınamadı");

            var result = new List<LockHolder>((int)count);
            for (var i = 0; i < count; i++)
            {
                var info = infos[i];
                var name = new string(info.AppName);
                name = name.TrimEnd('\0');
                if (name.Length == 0)
                {
                    try
                    {
                        using var proc = Process.GetProcessById((int)info.ProcessId);
                        name = proc.ProcessName;
                    }
                    catch (ArgumentException)
                    {
                        name = "?";
                    }
                }
                result.Add(new LockHolder((int)info.ProcessId, name, KindName(info.ApplicationType), info.Restartable != 0));
            }
            return result;
        }
        finally
        {
            Native.RmEndSession(session);
        }
    }

    static string KindName(int type) => type switch
    {
        1 => "Pencere",
        2 => "Pencere",
        3 => "Hizmet",
        4 => "Explorer",
        5 => "Konsol",
        1000 => "Kritik",
        _ => "Bilinmiyor",
    };

    public static IReadOnlyList<LockHolder> TryHolders(IEnumerable<string> paths)
    {
        try
        {
            return Holders(paths);
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    public static OpResult ScheduleDeleteOnReboot(string path)
    {
        var plain = Paths.FromLong(path);
        if (DryRun.Enabled)
        {
            DryRunLog.Write(OpMethod.Reboot, plain, "yeniden başlatmada silinecekti");
            return new OpResult { Path = plain, Status = OpStatus.DryRun, Method = OpMethod.Reboot, Message = "Prova: yeniden başlatmaya ertelenecekti" };
        }
        if (!Native.MoveFileEx(Paths.ToLong(plain), null, Native.MOVEFILE_DELAY_UNTIL_REBOOT))
            return OpResult.Failed(plain, OpMethod.Reboot, new Win32Exception().Message);
        return new OpResult { Path = plain, Status = OpStatus.Scheduled, Method = OpMethod.Reboot, Message = "Yeniden başlatmada silinecek" };
    }
}
