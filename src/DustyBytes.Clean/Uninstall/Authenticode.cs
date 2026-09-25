using System.Runtime.InteropServices;

namespace DustyBytes.Clean.Uninstall;

public sealed record SignerInfo(string Name, bool Trusted);

public static unsafe partial class Authenticode
{
    const uint WTD_UI_NONE = 2;
    const uint WTD_REVOKE_NONE = 0;
    const uint WTD_CHOICE_FILE = 1;
    const uint WTD_STATEACTION_VERIFY = 1;
    const uint WTD_STATEACTION_CLOSE = 2;
    const uint WTD_REVOCATION_CHECK_NONE = 0x10;
    const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
    const uint CERT_NAME_SIMPLE_DISPLAY_TYPE = 4;

    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    struct WintrustFileInfo
    {
        public uint cbStruct;
        public char* pcwszFilePath;
        public nint hFile;
        public Guid* pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WintrustData
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public WintrustFileInfo* pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public char* pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, Guid* pgActionID, WintrustData* pWVTData);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperProvDataFromStateData(nint hStateData);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperGetProvSignerFromChain(nint pProvData, uint idxSigner, int fCounterSigner, uint idxCounterSigner);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperGetProvCertFromChain(nint pSgnr, uint idxCert);

    [LibraryImport("crypt32.dll", EntryPoint = "CertGetNameStringW")]
    private static partial uint CertGetNameString(nint pCertContext, uint dwType, uint dwFlags, void* pvTypePara, char* pszNameString, uint cchNameString);

    public static SignerInfo? GetSigner(string file)
    {
        fixed (char* path = file)
        {
            var fileInfo = new WintrustFileInfo
            {
                cbStruct = (uint)sizeof(WintrustFileInfo),
                pcwszFilePath = path,
            };
            var data = new WintrustData
            {
                cbStruct = (uint)sizeof(WintrustData),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = &fileInfo,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            var action = GenericVerifyV2;
            int status;
            try
            {
                status = WinVerifyTrust(-1, &action, &data);
            }
            catch (DllNotFoundException)
            {
                return null;
            }

            try
            {
                if (data.hWVTStateData == 0)
                    return null;
                var prov = WTHelperProvDataFromStateData(data.hWVTStateData);
                if (prov == 0)
                    return null;
                var signer = WTHelperGetProvSignerFromChain(prov, 0, 0, 0);
                if (signer == 0)
                    return null;
                var provCert = WTHelperGetProvCertFromChain(signer, 0);
                if (provCert == 0)
                    return null;
                var certContext = *(nint*)((byte*)provCert + IntPtr.Size);
                if (certContext == 0)
                    return null;
                var buffer = stackalloc char[512];
                var len = CertGetNameString(certContext, CERT_NAME_SIMPLE_DISPLAY_TYPE, 0, null, buffer, 512);
                if (len <= 1)
                    return null;
                return new SignerInfo(new string(buffer, 0, (int)len - 1), status == 0);
            }
            finally
            {
                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(-1, &action, &data);
            }
        }
    }
}
