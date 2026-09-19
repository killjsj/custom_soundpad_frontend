using System;
using System.Runtime.InteropServices;

namespace CustomSoundpad.Apo;

internal static class Win32
{
    public static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public const int KEY_QUERY_VALUE = 0x0001;
    public const int KEY_SET_VALUE = 0x0002;
    public const int KEY_READ = 0x20019;
    public const int KEY_NOTIFY = 0x0010;

    public const uint REG_SZ = 1;
    public const uint REG_MULTI_SZ = 7;
    public const uint REG_DWORD = 4;

    public const int REG_NOTIFY_CHANGE_NAME = 0x00000001;
    public const int REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;

    public const uint PAGE_READWRITE = 0x04;
    public const uint FILE_MAP_ALL_ACCESS = 0x000F001F;

    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_STOP = 0x0020;
    public const uint SERVICE_START = 0x0010;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;
    public const uint SERVICE_STOPPED = 0x00000001;
    public const uint SERVICE_RUNNING = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegCreateKeyExW(IntPtr hKey, string lpSubKey, int reserved, string lpClass,
        int dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired,
        out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegSetValueExW(IntPtr hKey, string lpValueName, int reserved, uint dwType,
        byte[] lpData, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved,
        out uint lpType, byte[] lpData, ref int lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegDeleteValueW(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegDeleteTreeW(IntPtr hKey, string lpSubKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegDeleteKeyW(IntPtr hKey, string lpSubKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegEnumKeyExW(IntPtr hKey, int dwIndex, char[] lpName, ref int lpcchName,
        IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegEnumValueW(IntPtr hKey, int dwIndex, char[] lpValueName, ref int lpcchName,
        IntPtr lpReserved, out uint lpType, IntPtr lpData, IntPtr lpcbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool bWatchSubtree, int dwNotifyFilter,
        IntPtr hEvent, bool fAsynchronous);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool InitializeSecurityDescriptor(IntPtr pSecurityDescriptor, uint dwRevision);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetSecurityDescriptorDacl(IntPtr pSecurityDescriptor, bool bDaclPresent,
        IntPtr pDacl, bool bDaclDefaulted);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileMappingW(IntPtr hFile, ref SECURITY_ATTRIBUTES lpFileMappingAttributes,
        uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess,
        uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalAlloc(uint uFlags, UIntPtr uBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenSCManagerW(string machineName, string databaseName, uint dwAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);

    public const uint SECURITY_DESCRIPTOR_REVISION = 1;
    public const uint LPTR = 0x0040;
    public const int ERROR_SUCCESS = 0;
}
