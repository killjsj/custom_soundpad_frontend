using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Godot;

namespace CustomSoundpad.Apo;


[SupportedOSPlatform("windows")]
internal static class ApoInstaller
{
    public const string ApoClsidText = "{5F2EC245-A357-4858-80A2-5E575C904D6F}";
    public const string ApoDllName = "InjectAudioApo.dll";
    public const string ApoDllUnsignedName = "InjectAudioApo_unsign.dll";

    private const string CaptureKeyBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
    private const string InjectAudioSubKey = "InjectAudio";
    private const string OriginalApoValueName = "OriginalApoClsid";
    private const string EfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";
    private const string EfxModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";
    private const string DefaultProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";

    private const string CertSubject = "CN=InjectAudioApoCer";
    private const string CertCommonName = "InjectAudioApoCer";

    public static Action<string, bool> LogCallback;

    private static void Log(string message) => LogCallback?.Invoke(message, false);
    private static void LogError(string message) => LogCallback?.Invoke(message, true);

    

    public static string GetApoDirectory()
    {
        
        if (OS.HasFeature("editor"))
        {
            return Path.Combine(ProjectSettings.GlobalizePath("res://"), "bin");
        }

        string executable = OS.GetExecutablePath();
        string executableDirectory = string.IsNullOrEmpty(executable) ? null : Path.GetDirectoryName(executable);
        if (!string.IsNullOrEmpty(executableDirectory))
        {
            return Path.Combine(executableDirectory, "bin");
        }

        return Path.Combine(AppContext.BaseDirectory, "bin");
    }

    public static string ResolveApoDllPath() => Path.Combine(GetApoDirectory(), ApoDllName);

    public static string ResolveApoUnsignedDllPath() => Path.Combine(GetApoDirectory(), ApoDllUnsignedName);

    public static string ResolveApoSourceDllPath()
    {
        string unsignedPath = ResolveApoUnsignedDllPath();
        return File.Exists(unsignedPath) ? unsignedPath : ResolveApoDllPath();
    }

    

    private static bool RegSetRaw(string subKey, string valueName, uint type, byte[] data)
    {
        if (Win32.RegCreateKeyExW(Win32.HKEY_LOCAL_MACHINE, subKey, 0, null, 0,
                Win32.KEY_SET_VALUE | Win32.KEY_QUERY_VALUE, IntPtr.Zero, out IntPtr key, out _) != Win32.ERROR_SUCCESS)
        {
            return false;
        }
        try
        {
            return Win32.RegSetValueExW(key, valueName, 0, type, data, data.Length) == Win32.ERROR_SUCCESS;
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
    }

    private static bool RegSetString(string subKey, string valueName, string value)
        => RegSetRaw(subKey, valueName, Win32.REG_SZ, Encoding.Unicode.GetBytes(value + "\0"));

    private static bool RegSetMultiString(string subKey, string valueName, string[] values)
    {
        var builder = new StringBuilder();
        foreach (string value in values)
        {
            builder.Append(value).Append('\0');
        }
        builder.Append('\0');
        return RegSetRaw(subKey, valueName, Win32.REG_MULTI_SZ, Encoding.Unicode.GetBytes(builder.ToString()));
    }

    private static string RegQueryString(string subKey, string valueName)
    {
        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, subKey, 0, Win32.KEY_QUERY_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return null;
        }
        try
        {
            uint type = 0;
            int size = 0;
            if (Win32.RegQueryValueExW(key, valueName, IntPtr.Zero, out type, null, ref size) != Win32.ERROR_SUCCESS)
            {
                return null;
            }
            var buffer = new byte[size + 2];
            if (Win32.RegQueryValueExW(key, valueName, IntPtr.Zero, out type, buffer, ref size) != Win32.ERROR_SUCCESS ||
                type != Win32.REG_SZ)
            {
                return null;
            }
            return Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
    }

    public static bool IsProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out IntPtr token))
        {
            return false;
        }
        try
        {
            return GetTokenInformation(token, 20, out int elevated, sizeof(int), out _) && elevated != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool RegDeleteValue(string subKey, string valueName)
    {
        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, subKey, 0, Win32.KEY_SET_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return false;
        }
        try
        {
            int status = Win32.RegDeleteValueW(key, valueName);
            return status == Win32.ERROR_SUCCESS || status == 2;
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
    }

    private static bool RegDeleteTree(string subKey)
    {
        int status = Win32.RegDeleteTreeW(Win32.HKEY_LOCAL_MACHINE, subKey);
        return status == Win32.ERROR_SUCCESS || status == 2 || status == 3;
    }

    private static bool KeyExists(string subKey)
    {        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, subKey, 0, Win32.KEY_READ, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return false;
        }
        Win32.RegCloseKey(key);
        return true;
    }

    public readonly struct CaptureEndpoint
    {
        public CaptureEndpoint(string id, string guid, string name)
        {
            Id = id;
            Guid = guid;
            Name = name;
        }

        public string Id { get; }
        public string Guid { get; }
        public string Name { get; }
    }

    public static List<CaptureEndpoint> EnumCaptureEndpointsDetailed()
    {
        var endpoints = new List<CaptureEndpoint>();
        EnsureComInitialized();

        IMMDeviceEnumerator enumerator = null;
        IMMDeviceCollection collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
            if (enumerator.EnumAudioEndpoints(ECapture, DeviceStateActive, out collection) != 0 || collection == null)
            {
                return endpoints;
            }
            if (collection.GetCount(out int count) != 0)
            {
                return endpoints;
            }

            for (int index = 0; index < count; ++index)
            {
                IMMDevice device = null;
                try
                {
                    if (collection.Item(index, out device) != 0 || device == null)
                    {
                        continue;
                    }
                    if (device.GetId(out string id) != 0 || string.IsNullOrEmpty(id) ||
                        !TryExtractEndpointGuid(id, out string guid))
                    {
                        continue;
                    }
                    string name = GetDeviceFriendlyName(device) ?? GetEndpointFriendlyName(id);
                    endpoints.Add(new CaptureEndpoint(id, guid, name));
                }
                finally
                {
                    if (device != null)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            LogError($"EnumCaptureEndpoints failed: {exception.Message}");
        }
        finally
        {
            if (collection != null)
            {
                Marshal.ReleaseComObject(collection);
            }
            if (enumerator != null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        return endpoints;
    }

    public static IEnumerable<string> EnumCaptureEndpoints()
    {
        foreach (CaptureEndpoint endpoint in EnumCaptureEndpointsDetailed())
        {
            yield return endpoint.Guid;
        }
    }

    public static bool TryExtractEndpointGuid(string text, out string guid)
    {
        guid = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        int close = text.LastIndexOf('}');
        int open = close > 0 ? text.LastIndexOf('{', close) : -1;
        if (open < 0 || close - open + 1 != 38)
        {
            return false;
        }
        guid = text.Substring(open, 38);
        return true;
    }

    public static string GetEndpointFriendlyName(string endpointId)
    {
        if (!TryExtractEndpointGuid(endpointId, out string guid))
        {
            guid = endpointId;
        }
        string propertiesKey = CaptureKeyBase + "\\" + guid + "\\Properties";
        return RegQueryString(propertiesKey, "{a45c254e-df1c-4efd-8020-67d146a850e0},2")
            ?? RegQueryString(propertiesKey, "{b3f8fa53-0004-438e-9003-51a46e139bfc},6")
            ?? guid;
    }

    

    private const int ECapture = 1;
    private const int DeviceStateActive = 0x1;
    private const int STGM_READ = 0;
    private const int VtLpwstr = 31;
    private const int CoInitApartmentThreaded = 0x2;
    private const int RpcChangedMode = unchecked((int)0x80010106);
    private static readonly Guid PkeyDeviceFriendlyName = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

    private static bool _comInitialized;
    private static readonly object ComLock = new object();

    private static void EnsureComInitialized()
    {
        lock (ComLock)
        {
            if (_comInitialized)
            {
                return;
            }
            int result = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
            _comInitialized = result == 0 || result == 1 || result == RpcChangedMode;
            if (!_comInitialized)
            {
                LogError($"CoInitializeEx failed: 0x{result:X8}");
            }
        }
    }

    private static string GetDeviceFriendlyName(IMMDevice device)
    {
        IPropertyStore store = null;
        try
        {
            if (device.OpenPropertyStore(STGM_READ, out store) != 0 || store == null)
            {
                return null;
            }
            var key = new PROPERTYKEY { fmtid = PkeyDeviceFriendlyName, pid = 14 };
            if (store.GetValue(ref key, out PROPVARIANT value) != 0)
            {
                return null;
            }
            try
            {
                return value.vt == VtLpwstr ? Marshal.PtrToStringUni(value.pointerValue) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (store != null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PROPERTYKEY key);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int infoLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    

    private static X509Certificate2 FindDevCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            if (certificate.Subject == CertSubject && certificate.HasPrivateKey)
            {
                return certificate;
            }
        }
        return null;
    }

    private static X509Certificate2 CreateDevCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(CertSubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, false));

        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var pfx = selfSigned.Export(X509ContentType.Pfx);
#pragma warning disable SYSLIB0057
        return new X509Certificate2(pfx, (string)null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
    }

    private static bool AddCertificateToStore(X509Certificate2 certificate, StoreName storeName, bool withPrivateKey)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        bool exists = false;
        foreach (X509Certificate2 existing in store.Certificates)
        {
            if (existing.Thumbprint == certificate.Thumbprint)
            {
                exists = true;
                break;
            }
        }

        if (exists)
        {
            Log($"Cert already in LocalMachine\\{storeName}");
            return true;
        }

        try
        {
            if (withPrivateKey)
            {
                store.Add(certificate);
            }
            else
            {
#pragma warning disable SYSLIB0057
                store.Add(new X509Certificate2(certificate.Export(X509ContentType.Cert)));
#pragma warning restore SYSLIB0057
            }
            Log($"Added cert to LocalMachine\\{storeName}");
            return true;
        }
        catch (CryptographicException exception)
        {
            LogError($"AddCertificateToStore({storeName}) failed: {exception.Message}");
            return false;
        }
    }

    public static string FindSigntool()
    {
        foreach (string directory in new[] { GetApoDirectory(), AppContext.BaseDirectory })
        {
            string local = Path.Combine(directory, "signtool.exe");
            if (File.Exists(local))
            {
                return local;
            }
        }

        string programFilesX86 = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86);
        string kits = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");
        if (!Directory.Exists(kits))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(kits, "signtool.exe", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "x64", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int RunProcess(string executable, string arguments)
    {
        var startInfo = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return -1;
        }
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool SignWithSigntool(string signtool, string dllPath)
    {
        string baseArguments = $"sign /fd SHA256 /sm /s My /n \"{CertCommonName}\" ";
        if (RunProcess(signtool, baseArguments + $"/tr http://timestamp.digicert.com /td SHA256 \"{dllPath}\"") == 0)
        {
            return true;
        }

        Log("Timestamp failed, signing without timestamp...");
        return RunProcess(signtool, baseArguments + $"\"{dllPath}\"") == 0;
    }

    private static bool IsFileLocked(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string InprocServer32SubKey() => $@"SOFTWARE\Classes\CLSID\{ApoClsidText}\InprocServer32";

    

    public static Godot.Error RegisterApoDll(string dllPath)
    {
        if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
        {
            LogError($"APO DLL not found: {dllPath}");
            return Godot.Error.FileNotFound;
        }

        string directory = Path.GetDirectoryName(dllPath) ?? GetApoDirectory();
        string signedPath = Path.Combine(directory, ApoDllName);
        bool inPlace = string.Equals(Path.GetFullPath(dllPath), Path.GetFullPath(signedPath), StringComparison.OrdinalIgnoreCase);

        string signtool = FindSigntool();
        if (string.IsNullOrEmpty(signtool))
        {
            LogError("signtool.exe not found (Windows SDK required)");
            return Godot.Error.Unconfigured;
        }

        X509Certificate2 certificate = FindDevCertificate();
        if (certificate == null)
        {
            Log("Creating self-signed code signing certificate...");
            certificate = CreateDevCertificate();
            if (certificate == null || !AddCertificateToStore(certificate, StoreName.My, true))
            {
                certificate?.Dispose();
                return Godot.Error.Unconfigured;
            }
        }
        else
        {
            Log("Using existing code signing certificate");
        }

        bool storesOk = AddCertificateToStore(certificate, StoreName.Root, false) &
            AddCertificateToStore(certificate, StoreName.TrustedPublisher, false);
        certificate.Dispose();
        if (!storesOk)
        {
            return Godot.Error.Unauthorized;
        }

        if (inPlace)
        {
            if (IsFileLocked(signedPath))
            {
                string backup = signedPath + ".locked_" + DateTime.Now.ToString("HHmmss");
                LogError($"APO DLL is locked (audiodg), renaming to {backup}");
                File.Move(signedPath, backup, true);
                return Godot.Error.Busy;
            }
        }
        else if (!CopyToSignedPath(dllPath, signedPath))
        {
            return Godot.Error.Busy;
        }

        Log($"Signing {signedPath}");
        if (!SignWithSigntool(signtool, signedPath))
        {
            LogError("signtool failed");
            return Godot.Error.CantCreate;
        }

        Log("regsvr32...");
        if (RunProcess(Path.Combine(System.Environment.SystemDirectory, "regsvr32.exe"), $"/s \"{signedPath}\"") != 0)
        {
            LogError("regsvr32 failed");
            return Godot.Error.CantCreate;
        }

        if (!RegSetString(InprocServer32SubKey(), string.Empty, signedPath))
        {
            LogError("set InprocServer32 failed");
            return Godot.Error.Unauthorized;
        }

        Log($"InprocServer32 -> {signedPath}");
        return Godot.Error.Ok;
    }

    private static bool CopyToSignedPath(string sourcePath, string signedPath)
    {
        try
        {
            File.Copy(sourcePath, signedPath, true);
            Log($"Copied {Path.GetFileName(sourcePath)} -> {Path.GetFileName(signedPath)}");
            return true;
        }
        catch (IOException)
        {
            string backup = signedPath + ".locked_" + DateTime.Now.ToString("HHmmss");
            LogError($"APO DLL is locked (audiodg), renaming to {backup}");
            try
            {
                File.Move(signedPath, backup, true);
            }
            catch (Exception exception)
            {
                LogError($"rename failed: {exception.Message}");
                return false;
            }
            try
            {
                File.Copy(sourcePath, signedPath, true);
                return true;
            }
            catch (Exception exception)
            {
                LogError($"copy failed: {exception.Message}");
                return false;
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            LogError($"copy failed: {exception.Message}");
            return false;
        }
    }

    public static Godot.Error BindApoToEndpoint(string endpointId)
    {
        if (!TryExtractEndpointGuid(endpointId, out string guid))
        {
            LogError($"invalid endpoint id: {endpointId}");
            return Godot.Error.InvalidParameter;
        }

        string endpointKey = CaptureKeyBase + "\\" + guid;
        if (!KeyExists(endpointKey))
        {
            LogError($"capture endpoint not found: {guid}");
            return Godot.Error.DoesNotExist;
        }

        string fxKey = endpointKey + "\\FxProperties";

        string existing = RegQueryString(fxKey, EfxValueName);
        if (!string.IsNullOrEmpty(existing) &&
            !string.Equals(existing, ApoClsidText, StringComparison.OrdinalIgnoreCase))
        {
            if (!RegSetString(endpointKey + "\\" + InjectAudioSubKey, OriginalApoValueName, existing))
            {
                LogError("set OriginalApoClsid failed");
                return Godot.Error.Unauthorized;
            }
            Log($"OriginalApoClsid <- {existing}");
        }

        if (!RegSetString(fxKey, EfxValueName, ApoClsidText))
        {
            LogError("set EFX binding failed");
            return Godot.Error.Unauthorized;
        }

        if (!RegSetMultiString(fxKey, EfxModesValueName, new[] { DefaultProcessingMode }))
        {
            LogError("set EFX processing mode failed");
            return Godot.Error.Unauthorized;
        }

        Log($"APO bound to capture endpoint {guid}");
        return Godot.Error.Ok;
    }

    public static bool IsApoInstalled(string endpointId)
    {
        if (!TryExtractEndpointGuid(endpointId, out string guid))
        {
            return false;
        }
        string value = RegQueryString(CaptureKeyBase + "\\" + guid + "\\FxProperties", EfxValueName);
        return string.Equals(value, ApoClsidText, StringComparison.OrdinalIgnoreCase);
    }

    public static Godot.Error UnbindApoFromEndpoint(string endpointId)
    {
        if (!TryExtractEndpointGuid(endpointId, out string guid))
        {
            LogError($"invalid endpoint id: {endpointId}");
            return Godot.Error.InvalidParameter;
        }

        string endpointKey = CaptureKeyBase + "\\" + guid;
        if (!KeyExists(endpointKey))
        {
            LogError($"capture endpoint not found: {guid}");
            return Godot.Error.DoesNotExist;
        }

        string fxKey = endpointKey + "\\FxProperties";
        string injectKey = endpointKey + "\\" + InjectAudioSubKey;

        string original = RegQueryString(injectKey, OriginalApoValueName);
        if (!string.IsNullOrEmpty(original))
        {
            if (!RegSetString(fxKey, EfxValueName, original))
            {
                LogError("restore OriginalApoClsid failed");
                return Godot.Error.Unauthorized;
            }
            RegDeleteValue(injectKey, OriginalApoValueName);
            Log($"OriginalApoClsid restored <- {original}");
        }
        else
        {
            if (!RegDeleteValue(fxKey, EfxValueName))
            {
                LogError("delete EFX binding failed");
                return Godot.Error.Unauthorized;
            }
            Log("EFX binding removed (system default restored)");
        }

        RegDeleteValue(fxKey, EfxModesValueName);
        RegDeleteTree(injectKey);
        Log($"APO unbound from capture endpoint {guid}");
        return Godot.Error.Ok;
    }

    public static Godot.Error UnregisterApo()
    {
        if (!RegDeleteTree(InprocServer32SubKey()))
        {
            LogError("unregister APO failed");
            return Godot.Error.Unauthorized;
        }

        Win32.RegDeleteKeyW(Win32.HKEY_LOCAL_MACHINE, @"SOFTWARE\Classes\CLSID\" + ApoClsidText);
        Log("APO unregistered");
        return Godot.Error.Ok;
    }

    public static bool IsApoRegistered()
    {
        string dllPath = RegQueryString(InprocServer32SubKey(), string.Empty);
        if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
        {
            return false;
        }

        string expected = ResolveApoDllPath();
        return string.Equals(Path.GetFullPath(dllPath), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
    }

    

    private static bool StopServiceAndWait(string serviceName, int timeoutMs)
    {
        IntPtr manager = Win32.OpenSCManagerW(null, null, Win32.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
        {
            return false;
        }
        IntPtr service = Win32.OpenServiceW(manager, serviceName, Win32.SERVICE_STOP | Win32.SERVICE_QUERY_STATUS);
        if (service == IntPtr.Zero)
        {
            Win32.CloseServiceHandle(manager);
            return false;
        }

        bool stopped = true;
        var status = new Win32.SERVICE_STATUS();
        if (Win32.ControlService(service, Win32.SERVICE_CONTROL_STOP, ref status))
        {
            int start = System.Environment.TickCount;
            while (true)
            {
                if (!Win32.QueryServiceStatus(service, ref status) || status.dwCurrentState == Win32.SERVICE_STOPPED)
                {
                    break;
                }
                if (System.Environment.TickCount - start > timeoutMs)
                {
                    stopped = false;
                    break;
                }
                Thread.Sleep(100);
            }
        }

        Win32.CloseServiceHandle(service);
        Win32.CloseServiceHandle(manager);
        return stopped;
    }

    private static void StartService(string serviceName)
    {
        IntPtr manager = Win32.OpenSCManagerW(null, null, Win32.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
        {
            return;
        }
        IntPtr service = Win32.OpenServiceW(manager, serviceName, Win32.SERVICE_START);
        if (service != IntPtr.Zero)
        {
            Win32.StartServiceW(service, 0, IntPtr.Zero);
            Win32.CloseServiceHandle(service);
        }
        Win32.CloseServiceHandle(manager);
    }

    public static void RestartAudioStack()
    {
        Log("Restarting audio stack...");
        StopServiceAndWait("Audiosrv", 15000);
        StopServiceAndWait("AudioEndpointBuilder", 15000);
        Thread.Sleep(500);
        foreach (var process in Process.GetProcessesByName("audiodg"))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                
            }
        }
        Thread.Sleep(500);
        StartService("AudioEndpointBuilder");
        StartService("Audiosrv");
        Log("Audio stack restarted");
    }
}
