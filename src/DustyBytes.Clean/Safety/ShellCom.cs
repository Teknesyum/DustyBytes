using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DustyBytes.Clean.Safety;

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint sigdn, out nint name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
internal partial interface IShellItem2 : IShellItem
{
    void GetPropertyStore(int flags, in Guid riid, out nint ppv);
    void GetPropertyStoreWithCreateObject(int flags, nint punkCreateObject, in Guid riid, out nint ppv);
    void GetPropertyStoreForKeys(nint keys, uint count, int flags, in Guid riid, out nint ppv);
    void GetPropertyDescriptionList(in PropertyKey key, in Guid riid, out nint ppv);
    void Update(nint pbc);
    void GetProperty(in PropertyKey key, nint propvar);
    void GetCLSID(in PropertyKey key, out Guid clsid);
    [PreserveSig]
    int GetFileTime(in PropertyKey key, out long fileTime);
    [PreserveSig]
    int GetInt32(in PropertyKey key, out int value);
    [PreserveSig]
    int GetString(in PropertyKey key, out nint value);
    [PreserveSig]
    int GetUInt32(in PropertyKey key, out uint value);
    [PreserveSig]
    int GetUInt64(in PropertyKey key, out ulong value);
    [PreserveSig]
    int GetBool(in PropertyKey key, out int value);
}

[GeneratedComInterface]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
internal partial interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint celt, out nint item, out uint fetched);
    void Skip(uint celt);
    void Reset();
    void Clone(out nint ppenum);
}

[GeneratedComInterface]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint flags, nint item, nint newName);
    [PreserveSig] int PostRenameItem(uint flags, nint item, nint newName, int hrRename, nint newlyCreated);
    [PreserveSig] int PreMoveItem(uint flags, nint item, nint destinationFolder, nint newName);
    [PreserveSig] int PostMoveItem(uint flags, nint item, nint destinationFolder, nint newName, int hrMove, nint newlyCreated);
    [PreserveSig] int PreCopyItem(uint flags, nint item, nint destinationFolder, nint newName);
    [PreserveSig] int PostCopyItem(uint flags, nint item, nint destinationFolder, nint newName, int hrCopy, nint newlyCreated);
    [PreserveSig] int PreDeleteItem(uint flags, nint item);
    [PreserveSig] int PostDeleteItem(uint flags, nint item, int hrDelete, nint newlyCreated);
    [PreserveSig] int PreNewItem(uint flags, nint destinationFolder, nint newName);
    [PreserveSig] int PostNewItem(uint flags, nint destinationFolder, nint newName, nint templateName, uint fileAttributes, int hrNew, nint newItem);
    [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[GeneratedComInterface]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
internal partial interface IFileOperation
{
    void Advise(IFileOperationProgressSink sink, out uint cookie);
    void Unadvise(uint cookie);
    void SetOperationFlags(uint flags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
    void SetProgressDialog(nint dialog);
    void SetProperties(nint properties);
    void SetOwnerWindow(nint hwnd);
    void ApplyPropertiesToItem(IShellItem item);
    void ApplyPropertiesToItems(nint items);
    void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, nint sink);
    void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    void MoveItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, nint sink);
    void MoveItems(nint items, IShellItem destinationFolder);
    void CopyItem(IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, nint sink);
    void CopyItems(nint items, IShellItem destinationFolder);
    void DeleteItem(IShellItem item, nint sink);
    void DeleteItems(nint items);
    void NewItem(IShellItem destinationFolder, uint fileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, nint sink);
    [PreserveSig]
    int PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

internal static partial class ShellNative
{
    public static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    public static readonly Guid IID_IFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid FOLDERID_RecycleBinFolder = new("b7534046-3ecb-4c18-be4e-64cd4cb7d6ac");

    static readonly Guid PSGUID_DISPLACED = new("9b174b33-40ff-11d2-a27e-00c04fc30871");
    public static readonly PropertyKey SCID_ORIGINAL_LOCATION = new(PSGUID_DISPLACED, 2);
    public static readonly PropertyKey SCID_DATE_DELETED = new(PSGUID_DISPLACED, 3);
    public static readonly PropertyKey PKEY_Size = new(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 12);
    public static readonly PropertyKey PKEY_ItemNameDisplay = new(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);
    public static readonly PropertyKey PKEY_FileName = new(new Guid("41cf5ae0-f75a-4806-bd87-59c7d9248eb9"), 100);

    public const uint SIGDN_NORMALDISPLAY = 0;
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    public const uint SIGDN_PARENTRELATIVEEDITING = 0x80031001;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const uint FOF_SILENT = 0x4;
    public const uint FOF_NOCONFIRMATION = 0x10;
    public const uint FOF_ALLOWUNDO = 0x40;
    public const uint FOF_NOCONFIRMMKDIR = 0x200;
    public const uint FOF_NOERRORUI = 0x400;
    public const uint FOF_NO_UI = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR;
    public const uint FOFX_EARLYFAILURE = 0x100000;
    public const uint FOFX_RECYCLEONDELETE = 0x80000;

    public const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;

    public const int S_OK = 0;
    public const int E_ABORT = unchecked((int)0x80004004);
    public const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    public static readonly StrategyBasedComWrappers Wrappers = new();

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, nint pbc, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderItem(in Guid folderId, uint flags, nint token, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid riid, out nint ppv);

    public const uint CLSCTX_ALL = 0x17;

    public static T Wrap<T>(nint ptr) where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    public static IShellItem ItemFromPath(string path)
    {
        var hr = SHCreateItemFromParsingName(path, 0, IID_IShellItem, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        return Wrap<IShellItem>(ptr);
    }

    public static IFileOperation CreateFileOperation()
    {
        var hr = CoCreateInstance(CLSID_FileOperation, 0, CLSCTX_ALL, IID_IFileOperation, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        return Wrap<IFileOperation>(ptr);
    }

    public static string? TakeString(nint ptr)
    {
        if (ptr == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    public static string? DisplayName(IShellItem item, uint sigdn)
    {
        try
        {
            item.GetDisplayName(sigdn, out var p);
            return TakeString(p);
        }
        catch (COMException)
        {
            return null;
        }
    }
}

[GeneratedComClass]
internal sealed partial class RecycleSink : IFileOperationProgressSink
{
    public bool RefusedPermanentDelete { get; private set; }
    public int DeleteResult { get; private set; }
    public string? RecycledParsingPath { get; private set; }

    public int StartOperations() => ShellNative.S_OK;
    public int FinishOperations(int hrResult) => ShellNative.S_OK;
    public int PreRenameItem(uint flags, nint item, nint newName) => ShellNative.S_OK;
    public int PostRenameItem(uint flags, nint item, nint newName, int hrRename, nint newlyCreated) => ShellNative.S_OK;
    public int PreMoveItem(uint flags, nint item, nint destinationFolder, nint newName) => ShellNative.S_OK;
    public int PostMoveItem(uint flags, nint item, nint destinationFolder, nint newName, int hrMove, nint newlyCreated) => ShellNative.S_OK;
    public int PreCopyItem(uint flags, nint item, nint destinationFolder, nint newName) => ShellNative.S_OK;
    public int PostCopyItem(uint flags, nint item, nint destinationFolder, nint newName, int hrCopy, nint newlyCreated) => ShellNative.S_OK;

    public int PreDeleteItem(uint flags, nint item)
    {
        if ((flags & ShellNative.TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0)
        {
            RefusedPermanentDelete = true;
            return ShellNative.E_ABORT;
        }
        return ShellNative.S_OK;
    }

    public int PostDeleteItem(uint flags, nint item, int hrDelete, nint newlyCreated)
    {
        DeleteResult = hrDelete;
        if (newlyCreated != 0)
        {
            Marshal.AddRef(newlyCreated);
            var created = ShellNative.Wrap<IShellItem>(newlyCreated);
            RecycledParsingPath = ShellNative.DisplayName(created, ShellNative.SIGDN_DESKTOPABSOLUTEPARSING);
        }
        return ShellNative.S_OK;
    }

    public int PreNewItem(uint flags, nint destinationFolder, nint newName) => ShellNative.S_OK;
    public int PostNewItem(uint flags, nint destinationFolder, nint newName, nint templateName, uint fileAttributes, int hrNew, nint newItem) => ShellNative.S_OK;
    public int UpdateProgress(uint workTotal, uint workSoFar) => ShellNative.S_OK;
    public int ResetTimer() => ShellNative.S_OK;
    public int PauseTimer() => ShellNative.S_OK;
    public int ResumeTimer() => ShellNative.S_OK;
}
