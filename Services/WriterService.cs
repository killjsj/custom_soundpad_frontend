using System;
using System.Runtime.Versioning;
using CustomSoundpad.Apo;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class WriterService
{
    public const uint RingFlagDisableInject = 0x00000002u;

    private const float RingMusicVolume = 0.5f;

    private readonly SoundpadContext _context;
    private ApoWriter _writer;

    public event Action<int> SetupFinished;

    public WriterService(SoundpadContext context)
    {
        _context = context;
    }

    public void Initialize()
    {
        _writer = ApoWriter.Instance;
        if (_writer == null)
        {
            _writer = new ApoWriter { Name = "ApoWriter" };
            _context.Main.AddChild(_writer);
        }
        _writer.ApoSetupFinished += OnSetupFinished;
        RefreshGuard();
    }

    public void Shutdown()
    {
        if (_writer != null)
        {
            _writer.ApoSetupFinished -= OnSetupFinished;
            _writer.StopApoGuard();
        }
    }

    public void RefreshGuard()
    {
        ApoWriter writer = _writer;
        if (writer == null)
        {
            return;
        }
        try
        {
            writer.StopApoGuard();
            if (writer.IsApoRegistered())
            {
                Error result = writer.TryStartApoGuard();
                if (result != Error.Ok)
                {
                    GD.PushWarning($"[WriterService] TryStartApoGuard: {result}");
                }
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"RefreshGuard failed: {exception.Message}");
        }
    }

    public bool RetryOpen { get; private set; }

    public bool IsRingOpen => _writer != null && _writer.IsRingBufferValid();

    public bool EnsureOpen(out string error)
    {
        error = null;
        RetryOpen = false;

        if (_writer == null)
        {
            error = "ApoWriter 不可用";
            return false;
        }

        if (!_writer.IsRingBufferValid())
        {
            Error result = _writer.TryOpenRingBuffer();
            if (result != Error.Ok || !_writer.IsRingBufferValid())
            {
                RetryOpen = result == Error.Unauthorized;
                error = RetryOpen
                    ? "环形缓冲未就绪 (0x05)，录音激活后自动重试"
                    : $"TryOpenRingBuffer 失败: {result}";
                return false;
            }
        }

        _writer.SetMusicVolume(RingMusicVolume);
        return true;
    }

    public Error Write(Vector2[] frames)
    {
        return _writer != null ? _writer.WritePcm(frames) : Error.Unavailable;
    }

    public void SetInjectEnabled(bool enabled)
    {
        try
        {
            _writer?.SetConfig(enabled ? 0L : RingFlagDisableInject, 0.0f, 0L);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"SetInjectEnabled failed: {exception.Message}");
        }
    }

    public Error TryRegisterApo() => _writer?.TryRegisterApo() ?? Error.Unavailable;

    public Error TrySetApoToAudioInputDevice(string deviceId) => _writer?.TrySetApoToAudioInputDevice(deviceId) ?? Error.Unavailable;

    public Error TryUnbindApoFromInputDevice(string deviceId) => _writer?.TryUnbindApoFromInputDevice(deviceId) ?? Error.Unavailable;

    public Error TryUnregisterApo() => _writer?.TryUnregisterApo() ?? Error.Unavailable;

    public bool IsApoRegistered() => _writer != null && _writer.IsApoRegistered();

    public bool ApoIsInstalled(string deviceId) => _writer != null && _writer.ApoIsInstalled(deviceId);

    public Godot.Collections.Dictionary GetInputDeviceList() => _writer?.GetInputDeviceList();

    private void OnSetupFinished(int result)
    {
        SetupFinished?.Invoke(result);
    }
}
