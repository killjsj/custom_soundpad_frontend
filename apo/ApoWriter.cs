using Godot;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace CustomSoundpad.Apo;



[SupportedOSPlatform("windows")]
public partial class ApoWriter : Node
{
    public static ApoWriter Instance { get; private set; }

    [Signal] public delegate void ApoSetupFinishedEventHandler(int result);
    [Signal] public delegate void ApoSetupLogEventHandler(string message, bool isError);
    [Signal] public delegate void ApoGuardStartedEventHandler(int endpointCount);
    [Signal] public delegate void ApoGuardStoppedEventHandler();
    [Signal] public delegate void ApoGuardBindingChangedEventHandler(string devGuid);
    [Signal] public delegate void ApoGuardEffectPackResetEventHandler(string devGuid, string packClsid);
    [Signal] public delegate void ApoInputDeviceAddedEventHandler(string devGuid, string name);
    [Signal] public delegate void ApoInputDeviceRemovedEventHandler(string devGuid);

    private unsafe PcmRingBuffer.Shared* _ring;
    private IntPtr _mapping = IntPtr.Zero;
    private bool _shuttingDown;
    private int _setupRunning;
    private Task _setupTask;

    public override void _EnterTree()
    {
        Instance = this;
        ApoInstaller.LogCallback = OnLog;
        ApoBindingGuard.LogCallback = OnLog;
        ApoBindingGuard.EventCallback = OnGuardEvent;
    }

    public override void _ExitTree()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }
        _shuttingDown = true;

        
        ApoBindingGuard.EventCallback = null;
        ApoBindingGuard.LogCallback = null;
        ApoInstaller.LogCallback = null;

        try
        {
            _setupTask?.Wait(8000);
        }
        catch
        {
            
        }

        ApoBindingGuard.Stop();
        CloseRingBuffer();
    }

    

    private void OnLog(string message, bool isError)
    {
        if (_shuttingDown)
        {
            return;
        }
        CallDeferred(MethodName.PrintLog, message, isError);
    }

    private void OnGuardEvent(ApoBindingGuard.Event @event, string guid, string detail)
    {
        if (_shuttingDown)
        {
            return;
        }
        switch (@event)
        {
            case ApoBindingGuard.Event.Started:
                CallDeferred(MethodName.EmitGuardStarted, int.TryParse(detail, out int count) ? count : 0);
                break;
            case ApoBindingGuard.Event.Stopped:
                CallDeferred(MethodName.EmitGuardStopped);
                break;
            case ApoBindingGuard.Event.BindingChanged:
                CallDeferred(MethodName.EmitGuardBindingChanged, guid);
                break;
            case ApoBindingGuard.Event.EffectPackReset:
                CallDeferred(MethodName.EmitGuardEffectPackReset, guid, detail);
                break;
            case ApoBindingGuard.Event.DeviceAdded:
                CallDeferred(MethodName.EmitInputDeviceAdded, guid, detail);
                break;
            case ApoBindingGuard.Event.DeviceRemoved:
                CallDeferred(MethodName.EmitInputDeviceRemoved, guid);
                break;
        }
    }

    

    public void PrintLog(string message, bool isError)
    {
        if (_shuttingDown)
        {
            return;
        }
        AppLog.Write(message);
        if (isError)
        {
            GD.PushError(message);
        }
        else
        {
            GD.Print(message);
        }
        EmitSignal(SignalName.ApoSetupLog, message, isError);
    }

    public void EmitGuardStarted(int count) => EmitSignal(SignalName.ApoGuardStarted, count);
    public void EmitGuardStopped() => EmitSignal(SignalName.ApoGuardStopped);
    public void EmitGuardBindingChanged(string guid) => EmitSignal(SignalName.ApoGuardBindingChanged, guid);
    public void EmitGuardEffectPackReset(string guid, string pack) => EmitSignal(SignalName.ApoGuardEffectPackReset, guid, pack);
    public void EmitInputDeviceAdded(string guid, string name) => EmitSignal(SignalName.ApoInputDeviceAdded, guid, name);
    public void EmitInputDeviceRemoved(string guid) => EmitSignal(SignalName.ApoInputDeviceRemoved, guid);
    public void EmitSetupFinished(int result) => EmitSignal(SignalName.ApoSetupFinished, result);

    

    public unsafe Error TryOpenRingBuffer()
    {
        if (_ring != null && PcmRingBuffer.IsValid(_ring))
        {
            return Error.Ok;
        }

        CloseRingBuffer();

        
        IntPtr descriptor = Win32.LocalAlloc(Win32.LPTR, 64);
        if (descriptor == IntPtr.Zero ||
            !Win32.InitializeSecurityDescriptor(descriptor, Win32.SECURITY_DESCRIPTOR_REVISION) ||
            !Win32.SetSecurityDescriptorDacl(descriptor, true, IntPtr.Zero, false))
        {
            if (descriptor != IntPtr.Zero)
            {
                Win32.LocalFree(descriptor);
            }
            return Error.Unconfigured;
        }

        var attributes = new Win32.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<Win32.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = descriptor,
            bInheritHandle = false,
        };

        IntPtr mapping = Win32.CreateFileMappingW(Win32.INVALID_HANDLE_VALUE, ref attributes,
            Win32.PAGE_READWRITE, 0, (uint)PcmRingBuffer.Size, PcmRingBuffer.MappingName);
        int error = Marshal.GetLastWin32Error();
        Win32.LocalFree(descriptor);

        if (mapping == IntPtr.Zero)
        {
            if (error != 5)
            {
                GD.PushError($"CreateFileMappingW failed: 0x{error:X8}");
            }
            return error == 5 ? Error.Unauthorized : Error.Failed;
        }

        IntPtr view = Win32.MapViewOfFile(mapping, Win32.FILE_MAP_ALL_ACCESS, 0, 0, (UIntPtr)PcmRingBuffer.Size);
        if (view == IntPtr.Zero)
        {
            Win32.CloseHandle(mapping);
            GD.PushError($"MapViewOfFile failed: 0x{Marshal.GetLastWin32Error():X8}");
            return Error.Failed;
        }

        _mapping = mapping;
        _ring = (PcmRingBuffer.Shared*)view;
        PcmRingBuffer.Initialize(_ring, 2);
        PcmRingBuffer.SetConfig(_ring, 0, 0, 2);
        PcmRingBuffer.SetMusicVolume(_ring, 0.5f);
        return Error.Ok;
    }

    public void CloseRingBuffer()
    {
        unsafe
        {
            if (_ring != null)
            {
                Win32.UnmapViewOfFile((IntPtr)_ring);
                _ring = null;
            }
        }
        if (_mapping != IntPtr.Zero)
        {
            Win32.CloseHandle(_mapping);
            _mapping = IntPtr.Zero;
        }
    }

    public unsafe bool IsRingBufferValid() => _ring != null && PcmRingBuffer.IsValid(_ring);

    public unsafe void SetConfig(long flags, float micAmpInDb, long logLevel)
        => PcmRingBuffer.SetConfig(_ring, (uint)flags, micAmpInDb, (uint)logLevel);

    public unsafe void SetMusicVolume(float volume) => PcmRingBuffer.SetMusicVolume(_ring, volume);

    public unsafe Error WritePcm(Vector2[] pcms)
    {
        if (pcms == null || pcms.Length == 0)
        {
            return Error.Ok;
        }
        if (!IsRingBufferValid())
        {
            return Error.Unavailable;
        }
        fixed (Vector2* source = pcms)
        {
            var interleaved = new ReadOnlySpan<float>((float*)source, pcms.Length * 2);
            return PcmRingBuffer.WriteFramesStereo(_ring, interleaved) ? Error.Ok : Error.Unavailable;
        }
    }

    

    public Error TryRegisterApo()
    {
        if (!ApoInstaller.IsProcessElevated())
        {
            PrintLog("TryRegisterApo requires administrator privileges", true);
            return Error.Unauthorized;
        }
        if (Interlocked.CompareExchange(ref _setupRunning, 1, 0) != 0)
        {
            return Error.Busy;
        }

        string dllPath = ApoInstaller.ResolveApoSourceDllPath();
        if (!File.Exists(dllPath))
        {
            PrintLog($"APO DLL not found: {dllPath}", true);
            Interlocked.Exchange(ref _setupRunning, 0);
            return Error.FileNotFound;
        }

        _setupTask = Task.Run(() => RunSetup(() =>
        {
            Godot.Error result = ApoInstaller.RegisterApoDll(dllPath);
            if (result == Godot.Error.Ok)
            {
                ApoInstaller.RestartAudioStack();
            }
            return result;
        }));
        return Error.Ok;
    }

    public Error TrySetApoToAudioInputDevice(string devGuid)
    {
        if (!ApoInstaller.IsProcessElevated())
        {
            PrintLog("TrySetApoToAudioInputDevice requires administrator privileges", true);
            return Error.Unauthorized;
        }
        if (Interlocked.CompareExchange(ref _setupRunning, 1, 0) != 0)
        {
            return Error.Busy;
        }

        _setupTask = Task.Run(() => RunSetup(() =>
        {
            Godot.Error result = ApoInstaller.BindApoToEndpoint(devGuid);
            if (result == Godot.Error.Ok)
            {
                ApoInstaller.RestartAudioStack();
            }
            return result;
        }));
        return Error.Ok;
    }

    public bool ApoIsInstalled(string devGuid) => ApoInstaller.IsApoInstalled(devGuid);

    public Error TryUnbindApoFromInputDevice(string devGuid)
    {
        if (!ApoInstaller.IsProcessElevated())
        {
            PrintLog("TryUnbindApoFromInputDevice requires administrator privileges", true);
            return Error.Unauthorized;
        }
        if (Interlocked.CompareExchange(ref _setupRunning, 1, 0) != 0)
        {
            return Error.Busy;
        }

        _setupTask = Task.Run(() => RunSetup(() =>
        {
            Godot.Error result = ApoInstaller.UnbindApoFromEndpoint(devGuid);
            if (result == Godot.Error.Ok)
            {
                ApoInstaller.RestartAudioStack();
            }
            return result;
        }));
        return Error.Ok;
    }

    public Error TryUnregisterApo()
    {
        if (!ApoInstaller.IsProcessElevated())
        {
            PrintLog("TryUnregisterApo requires administrator privileges", true);
            return Error.Unauthorized;
        }
        if (Interlocked.CompareExchange(ref _setupRunning, 1, 0) != 0)
        {
            return Error.Busy;
        }

        _setupTask = Task.Run(() => RunSetup(() =>
        {
            Godot.Error result = ApoInstaller.UnregisterApo();
            if (result == Godot.Error.Ok)
            {
                ApoInstaller.RestartAudioStack();
            }
            return result;
        }));
        return Error.Ok;
    }

    public bool IsApoRegistered() => ApoInstaller.IsApoRegistered();

    public Godot.Collections.Dictionary GetInputDeviceList()
    {
        var result = new Godot.Collections.Dictionary();
        foreach (ApoInstaller.CaptureEndpoint endpoint in ApoInstaller.EnumCaptureEndpointsDetailed())
        {
            result[endpoint.Id] = endpoint.Name;
        }
        return result;
    }

    

    public Error TryStartApoGuard()
    {
        if (Interlocked.CompareExchange(ref _setupRunning, 1, 0) != 0)
        {
            return Error.Busy;
        }

        _setupTask = Task.Run(() => RunSetup(() =>
        {
            var options = new ApoBindingGuard.Options { ApoClsid = ApoInstaller.ApoClsidText };
            return ApoBindingGuard.Start(options) ? Godot.Error.Ok : Godot.Error.Failed;
        }));
        return Error.Ok;
    }

    public void StopApoGuard() => ApoBindingGuard.Stop();

    public bool IsApoGuardRunning() => ApoBindingGuard.IsRunning;

    

    private Godot.Error RunSetup(Func<Godot.Error> action)
    {
        Godot.Error result;
        try
        {
            result = action();
        }
        catch (Exception exception)
        {
            GD.PushError($"ApoWriter setup exception: {exception}");
            result = Godot.Error.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _setupRunning, 0);
        }

        if (!_shuttingDown)
        {
            CallDeferred(MethodName.EmitSetupFinished, (int)result);
        }
        return result;
    }
}
