using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace CustomSoundpad.Apo;







[SupportedOSPlatform("windows")]
internal static class ApoBindingGuard
{
    public enum Event
    {
        Started,
        Stopped,
        BindingChanged,
        EffectPackReset,
        DeviceAdded,
        DeviceRemoved,
    }

    public sealed class Options
    {
        public string ApoClsid;
        public string CaptureKeyBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
        public string ProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
        public string AudioService = "Audiosrv";
        public int MonitorIntervalMs = 2000;
        public int ChangeDebounceMs = 200;
        public int RestartDebounceMs = 10000;
        public int ServiceTimeoutMs = 5000;
    }

    private const string PropertiesSubKey = "Properties";
    private const string FxPropertiesSubKey = "FxProperties";
    private const string EfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";
    private const string EfxModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";
    private const string DisableSysFxValueName = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
    private const string PackGuid = "{c876062a-a276-4ed6-b15b-3962e69007f8}";
    private const string PackValue2 = PackGuid + ",2";
    private const string FriendlyNameKey = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string DeviceDescKey = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

    public static Action<Event, string, string> EventCallback;
    public static Action<string, bool> LogCallback;

    private static readonly object StateLock = new();
    private static readonly object GuardLock = new();
    private static bool _running;
    private static Options _options;
    private static readonly List<EndpointGuard> Guards = new();
    private static readonly List<string> KnownEndpoints = new();
    private static Thread _monitorThread;
    private static ManualResetEvent _monitorStop;

    private sealed class EndpointGuard
    {
        public string EndpointId;
        public ManualResetEvent StopEvent;
        public ManualResetEvent ChangeEvent;
        public Thread Thread;
    }

    private static void Log(string message) => LogCallback?.Invoke(message, false);
    private static void LogError(string message) => LogCallback?.Invoke(message, true);

    private static void Emit(Event @event, string guid, string detail) => EventCallback?.Invoke(@event, guid, detail);

    

    private static string QueryString(string subKey, string valueName)
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
                (type != Win32.REG_SZ && type != Win32.REG_MULTI_SZ))
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

    private static uint QueryDword(string subKey, string valueName, uint fallback)
    {
        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, subKey, 0, Win32.KEY_QUERY_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return fallback;
        }
        try
        {
            int size = 4;
            var buffer = new byte[4];
            uint type = 0;
            if (Win32.RegQueryValueExW(key, valueName, IntPtr.Zero, out type, buffer, ref size) != Win32.ERROR_SUCCESS)
            {
                return fallback;
            }
            return BitConverter.ToUInt32(buffer, 0);
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
    }

    private static bool WriteRaw(string subKey, string valueName, uint type, byte[] data)
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

    private static string EndpointKey(string endpointId, string leaf = null)
        => string.IsNullOrEmpty(leaf) ? _options.CaptureKeyBase + "\\" + endpointId : _options.CaptureKeyBase + "\\" + endpointId + "\\" + leaf;

    
    private static bool DeletePackValues(string propertiesKey)
    {
        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, propertiesKey, 0,
                Win32.KEY_SET_VALUE | Win32.KEY_QUERY_VALUE, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return false;
        }
        try
        {
            bool deleted = false;
            var name = new char[256];
            int index = 0;
            while (true)
            {
                int length = name.Length;
                uint type = 0;
                if (Win32.RegEnumValueW(key, index, name, ref length, IntPtr.Zero, out type, IntPtr.Zero, IntPtr.Zero) != Win32.ERROR_SUCCESS)
                {
                    break;
                }
                string valueName = new string(name, 0, length);
                if (valueName.StartsWith(PackGuid, StringComparison.OrdinalIgnoreCase))
                {
                    deleted |= Win32.RegDeleteValueW(key, valueName) == Win32.ERROR_SUCCESS;
                }
                else
                {
                    ++index;
                }
            }
            return deleted;
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
    }

    

    private static bool IsActive(uint deviceState)
        => (deviceState & 0x0F) == 0x01 && (deviceState & 0x10000000) == 0;

    private static string GetFriendlyName(string endpointId)
        => QueryString(EndpointKey(endpointId, PropertiesSubKey), FriendlyNameKey)
           ?? QueryString(EndpointKey(endpointId, PropertiesSubKey), DeviceDescKey)
           ?? endpointId;

    private static List<(string Id, string Name)> EnumActiveEndpoints()
    {
        var result = new List<(string, string)>();
        if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, _options.CaptureKeyBase, 0, Win32.KEY_READ, out IntPtr key) != Win32.ERROR_SUCCESS)
        {
            return result;
        }
        try
        {
            var name = new char[256];
            for (int index = 0; ; ++index)
            {
                int length = name.Length;
                if (Win32.RegEnumKeyExW(key, index, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != Win32.ERROR_SUCCESS)
                {
                    break;
                }
                string endpointId = new string(name, 0, length);
                if (endpointId.Length != 38 || endpointId[0] != '{' || endpointId[^1] != '}')
                {
                    continue;
                }
                if (IsActive(QueryDword(EndpointKey(endpointId), "DeviceState", 0)))
                {
                    result.Add((endpointId, GetFriendlyName(endpointId)));
                }
            }
        }
        finally
        {
            Win32.RegCloseKey(key);
        }
        return result;
    }

    private static bool IsBound(string endpointId)
    {
        string value = QueryString(EndpointKey(endpointId, FxPropertiesSubKey), EfxValueName);
        return string.Equals(value, _options.ApoClsid, StringComparison.OrdinalIgnoreCase);
    }

    

    private static void RestoreEndpoint(EndpointGuard guard)
    {
        string fxKey = EndpointKey(guard.EndpointId, FxPropertiesSubKey);

        
        string efx = QueryString(fxKey, EfxValueName);
        if (!string.Equals(efx, _options.ApoClsid, StringComparison.OrdinalIgnoreCase))
        {
            LogError($"[ALERT] APO binding was modified (expected {_options.ApoClsid})");
            Emit(Event.BindingChanged, guard.EndpointId, string.Empty);
        }

        
        string modes = QueryString(fxKey, EfxModesValueName);
        if (!string.Equals(modes, _options.ProcessingMode, StringComparison.OrdinalIgnoreCase))
        {
            var data = new List<byte>();
            data.AddRange(Encoding.Unicode.GetBytes(_options.ProcessingMode + "\0\0"));
            if (WriteRaw(fxKey, EfxModesValueName, Win32.REG_MULTI_SZ, data.ToArray()))
            {
                Log("restored EFX processing mode");
            }
        }

        if (QueryDword(fxKey, DisableSysFxValueName, 0) != 0)
        {
            if (WriteRaw(fxKey, DisableSysFxValueName, Win32.REG_DWORD, BitConverter.GetBytes(0u)))
            {
                Log("restored Disable_SysFx = 0");
                RestartAudioStack();
            }
            else
            {
                LogError("restore Disable_SysFx failed (需要管理员权限)");
            }
        }

        
        string pack = QueryString(EndpointKey(guard.EndpointId, PropertiesSubKey), PackValue2);
        if (!string.IsNullOrEmpty(pack))
        {
            Log($"effect pack selected ({pack}), resetting to device default");
            if (DeletePackValues(EndpointKey(guard.EndpointId, PropertiesSubKey)))
            {
                Emit(Event.EffectPackReset, guard.EndpointId, pack);
                RestartAudioStack();
            }
            else
            {
                LogError("reset effect pack failed (需要管理员权限)");
            }
        }
    }

    private static long _lastRestartTicks;

    private static void RestartAudioStack()
    {
        long now = Environment.TickCount64;
        if (_lastRestartTicks != 0 && now - _lastRestartTicks < _options.RestartDebounceMs)
        {
            Log("audio stack restart skipped (debounce)");
            return;
        }
        _lastRestartTicks = now;

        Log($"restarting {_options.AudioService} to apply revert...");
        StopService(_options.AudioService);
        Thread.Sleep(500);
        StartService(_options.AudioService);
        Log($"{_options.AudioService} restarted");
    }

    private static void StopService(string serviceName)
    {
        IntPtr manager = Win32.OpenSCManagerW(null, null, Win32.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
        {
            return;
        }
        IntPtr service = Win32.OpenServiceW(manager, serviceName, Win32.SERVICE_STOP | Win32.SERVICE_QUERY_STATUS);
        if (service != IntPtr.Zero)
        {
            var status = new Win32.SERVICE_STATUS();
            Win32.ControlService(service, Win32.SERVICE_CONTROL_STOP, ref status);
            int start = Environment.TickCount;
            while (Environment.TickCount - start < _options.ServiceTimeoutMs)
            {
                if (!Win32.QueryServiceStatus(service, ref status) || status.dwCurrentState == Win32.SERVICE_STOPPED)
                {
                    break;
                }
                Thread.Sleep(100);
            }
            Win32.CloseServiceHandle(service);
        }
        Win32.CloseServiceHandle(manager);
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

    

    private static void WatchLoop(object state)
    {
        var guard = (EndpointGuard)state;
        IntPtr key = IntPtr.Zero;
        while (true)
        {
            if (key == IntPtr.Zero)
            {
                if (Win32.RegOpenKeyExW(Win32.HKEY_LOCAL_MACHINE, EndpointKey(guard.EndpointId), 0,
                        Win32.KEY_NOTIFY | Win32.KEY_READ, out key) != Win32.ERROR_SUCCESS)
                {
                    if (guard.StopEvent.WaitOne(500))
                    {
                        break;
                    }
                    continue;
                }
            }

            guard.ChangeEvent.Reset();
            if (Win32.RegNotifyChangeKeyValue(key, true,
                    Win32.REG_NOTIFY_CHANGE_LAST_SET | Win32.REG_NOTIFY_CHANGE_NAME,
                    guard.ChangeEvent.SafeWaitHandle.DangerousGetHandle(), true) != Win32.ERROR_SUCCESS)
            {
                Win32.RegCloseKey(key);
                key = IntPtr.Zero;
                if (guard.StopEvent.WaitOne(500))
                {
                    break;
                }
                continue;
            }

            int wait = WaitHandle.WaitAny(new WaitHandle[] { guard.StopEvent, guard.ChangeEvent });
            if (wait == 0)
            {
                break;
            }

            Thread.Sleep(_options.ChangeDebounceMs);
            RestoreEndpoint(guard);

            Win32.RegCloseKey(key);
            key = IntPtr.Zero;
            Thread.Sleep(500);
        }

        if (key != IntPtr.Zero)
        {
            Win32.RegCloseKey(key);
        }
    }

    private static void MonitorLoop(object state)
    {
        var stop = (ManualResetEvent)state;
        KnownEndpoints.Clear();
        foreach (var endpoint in EnumActiveEndpoints())
        {
            KnownEndpoints.Add(endpoint.Id);
            EnsureGuard(endpoint.Id);
        }
        Log($"endpoint monitor started ({KnownEndpoints.Count} active capture endpoints)");

        while (!stop.WaitOne(_options.MonitorIntervalMs))
        {
            List<(string Id, string Name)> endpoints = EnumActiveEndpoints();

            foreach (var endpoint in endpoints)
            {
                if (!KnownEndpoints.Contains(endpoint.Id))
                {
                    Log($"[ALERT] new capture endpoint: {endpoint.Name} ({endpoint.Id})");
                    Emit(Event.DeviceAdded, endpoint.Id, endpoint.Name);
                    KnownEndpoints.Add(endpoint.Id);
                    EnsureGuard(endpoint.Id);
                }
            }

            for (int i = KnownEndpoints.Count - 1; i >= 0; --i)
            {
                string id = KnownEndpoints[i];
                if (!endpoints.Exists(endpoint => endpoint.Id == id))
                {
                    Log($"[ALERT] capture endpoint removed: {id}");
                    Emit(Event.DeviceRemoved, id, string.Empty);
                    KnownEndpoints.RemoveAt(i);
                    RemoveGuard(id);
                }
            }
        }
    }

    private static void EnsureGuard(string endpointId)
    {
        if (!_running || string.IsNullOrEmpty(endpointId) || !IsBound(endpointId))
        {
            return;
        }
        lock (GuardLock)
        {
            if (!_running)
            {
                return;
            }
            foreach (EndpointGuard existing in Guards)
            {
                if (existing.EndpointId == endpointId)
                {
                    return;
                }
            }

            var guard = new EndpointGuard
            {
                EndpointId = endpointId,
                StopEvent = new ManualResetEvent(false),
                ChangeEvent = new ManualResetEvent(false),
            };
            Guards.Add(guard);
            RestoreEndpoint(guard);
            guard.Thread = new Thread(WatchLoop) { IsBackground = true, Name = "ApoGuardWatch" };
            guard.Thread.Start(guard);
        }
        Log($"watching endpoint {endpointId}");
    }

    private static void RemoveGuard(string endpointId)
    {
        EndpointGuard found = null;
        lock (GuardLock)
        {
            for (int i = Guards.Count - 1; i >= 0; --i)
            {
                if (Guards[i].EndpointId == endpointId)
                {
                    found = Guards[i];
                    Guards.RemoveAt(i);
                    break;
                }
            }
        }
        if (found == null)
        {
            return;
        }
        found.StopEvent.Set();
        found.Thread?.Join(3000);
        found.StopEvent.Dispose();
        found.ChangeEvent.Dispose();
        Log($"stopped watching endpoint {endpointId}");
    }

    

    public static bool Start(Options options)
    {
        lock (StateLock)
        {
            if (_running)
            {
                return true;
            }
            if (string.IsNullOrEmpty(options?.ApoClsid))
            {
                LogError("Start failed: apoClsid is empty");
                return false;
            }

            _options = options;
            _running = true;
        }

        foreach (string endpointId in ApoInstaller.EnumCaptureEndpoints())
        {
            EnsureGuard(endpointId);
        }

        _monitorStop = new ManualResetEvent(false);
        _monitorThread = new Thread(MonitorLoop) { IsBackground = true, Name = "ApoGuardMonitor" };
        _monitorThread.Start(_monitorStop);

        int count;
        lock (GuardLock)
        {
            count = Guards.Count;
        }
        Log(count > 0
            ? $"watching {count} bound capture endpoint(s)"
            : "no bound capture endpoint yet, waiting for monitor");
        Emit(Event.Started, string.Empty, count.ToString());
        return true;
    }

    public static void Stop()
    {
        bool wasRunning;
        lock (StateLock)
        {
            wasRunning = _running;
            _running = false;
        }

        _monitorStop?.Set();
        _monitorThread?.Join(3000);
        _monitorThread = null;
        _monitorStop?.Dispose();
        _monitorStop = null;

        List<EndpointGuard> guards;
        lock (GuardLock)
        {
            guards = new List<EndpointGuard>(Guards);
            Guards.Clear();
        }

        foreach (EndpointGuard guard in guards)
        {
            guard.StopEvent.Set();
        }
        foreach (EndpointGuard guard in guards)
        {
            guard.Thread?.Join(3000);
            guard.StopEvent.Dispose();
            guard.ChangeEvent.Dispose();
        }

        if (wasRunning)
        {
            Emit(Event.Stopped, string.Empty, string.Empty);
        }
    }

    public static bool IsRunning
    {
        get
        {
            lock (StateLock)
            {
                return _running;
            }
        }
    }
}
