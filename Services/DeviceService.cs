using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class DeviceService
{
    private enum SetupKind
    {
        Bind,
        Unbind,
    }

    private readonly SoundpadContext _context;
    private readonly Queue<(string DeviceId, SetupKind Kind)> _queue = new();

    private bool _running;
    private bool _hadError;
    private bool _awaitingRegister;
    private bool _awaitingUnregister;
    private (string DeviceId, SetupKind Kind) _current;

    public event Action DevicesChanged;

    public bool IsBusy => _running;

    public DeviceService(SoundpadContext context)
    {
        _context = context;
    }

    public void Initialize() => _context.Writer.SetupFinished += OnSetupFinished;

    public void Shutdown() => _context.Writer.SetupFinished -= OnSetupFinished;

    public Godot.Collections.Dictionary GetDevices() => _context.Writer.GetInputDeviceList();

    public bool IsBound(string deviceId) => _context.Writer.ApoIsInstalled(deviceId);

    public bool IsApoRegistered => _context.Writer.IsApoRegistered();

    public void Bind(IReadOnlyList<string> deviceIds) => Start(SetupKind.Bind, deviceIds);

    public void Unbind(IReadOnlyList<string> deviceIds) => Start(SetupKind.Unbind, deviceIds);

    public void UnregisterApo()
    {
        if (_running)
        {
            _context.Status.Set("注册任务进行中...");
            return;
        }

        _running = true;
        _awaitingUnregister = true;
        Error result = _context.Writer.TryUnregisterApo();
        if (result != Error.Ok)
        {
            _awaitingUnregister = false;
            _running = false;
            _context.Status.Set($"注销请求失败: {result}");
        }
        else
        {
            _context.Status.Set("正在注销 APO...");
        }
    }

    private void Start(SetupKind kind, IReadOnlyList<string> deviceIds)
    {
        if (_running)
        {
            _context.Status.Set("注册任务进行中...");
            return;
        }

        if (deviceIds == null || deviceIds.Count == 0)
        {
            _context.Status.Set(kind == SetupKind.Bind ? "请先勾选要注册的设备" : "请先勾选要取消绑定的设备");
            return;
        }

        _queue.Clear();
        foreach (string deviceId in deviceIds)
        {
            _queue.Enqueue((deviceId, kind));
        }
        _running = true;
        _hadError = false;

        if (kind == SetupKind.Bind && !IsApoRegistered)
        {
            _awaitingRegister = true;
            Error result = _context.Writer.TryRegisterApo();
            if (result != Error.Ok)
            {
                _awaitingRegister = false;
                _running = false;
                _context.Status.Set($"APO 注册请求失败: {result}");
            }
            else
            {
                _context.Status.Set("正在注册 APO...");
            }
            return;
        }

        ProcessNext();
    }

    private void ProcessNext()
    {
        if (_queue.Count == 0)
        {
            _running = false;
            _context.Status.Set(_hadError ? "设备任务结束 (部分失败，见日志/状态)" : "设备任务完成");
            _context.Writer.RefreshGuard();
            DevicesChanged?.Invoke();
            return;
        }

        _current = _queue.Dequeue();
        Error result = _current.Kind == SetupKind.Bind
            ? _context.Writer.TrySetApoToAudioInputDevice(_current.DeviceId)
            : _context.Writer.TryUnbindApoFromInputDevice(_current.DeviceId);
        if (result != Error.Ok)
        {
            _hadError = true;
            string verb = _current.Kind == SetupKind.Bind ? "绑定" : "取消绑定";
            _context.Status.Set($"{verb}请求失败: {result}");
            ProcessNext();
            return;
        }
        _context.Status.Set(_current.Kind == SetupKind.Bind
            ? $"正在绑定 {ShortName(_current.DeviceId)} ..."
            : $"正在取消绑定 {ShortName(_current.DeviceId)} ...");
    }

    private void OnSetupFinished(int result)
    {
        if (_awaitingUnregister)
        {
            _awaitingUnregister = false;
            _running = false;
            _context.Status.Set(result == (int)Error.Ok ? "APO 已注销" : $"注销失败: {(Error)result}");
            _context.Writer.RefreshGuard();
            DevicesChanged?.Invoke();
            return;
        }

        if (!_running)
        {
            return;
        }

        if (_awaitingRegister)
        {
            _awaitingRegister = false;
            if (result != (int)Error.Ok)
            {
                _running = false;
                _hadError = true;
                _queue.Clear();
                _context.Status.Set($"APO 注册失败: {(Error)result}");
                return;
            }
            ProcessNext();
            return;
        }

        string verb = _current.Kind == SetupKind.Bind ? "绑定" : "取消绑定";
        if (result != (int)Error.Ok)
        {
            _hadError = true;
        }
        _context.Status.Set(result == (int)Error.Ok
            ? $"{verb}成功: {ShortName(_current.DeviceId)}"
            : $"{verb}失败 {(Error)result}: {ShortName(_current.DeviceId)}");
        ProcessNext();
    }

    private static string ShortName(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return string.Empty;
        }
        int close = deviceId.LastIndexOf('}');
        int open = close > 0 ? deviceId.LastIndexOf('{', close) : -1;
        return open >= 0 ? deviceId.Substring(open) : deviceId;
    }
}
