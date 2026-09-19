using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class PlaybackService
{
    public const string CaptureBusName = "SoundpadCapture";

    private const float CaptureBufferSeconds = 5.0f;
    private const string WriterLabel = "ApoWriter ";

    private readonly SoundpadContext _context;
    private AudioStreamPlayer _player;
    private AudioEffectCapture _capture;
    private AudioEffectSpectrumAnalyzer _analyzer;

    private bool _playing;
    private bool _paused;
    private bool _writerReady;
    private double _retryTimer;
    private long _framesWritten;
    private long _discardedAtStart;
    private long _playbackDiscarded;
    private double _pendingSeek;
    private double _displayedPosition;

    public float RingRetryIntervalSeconds { get; set; } = 2.5f;

    public event Action StateChanged;

    public event Action<double, string> ProgressChanged;

    public PlaybackService(SoundpadContext context)
    {
        _context = context;
    }

    public bool IsPlaying => _playing;

    public bool IsPaused => _paused;

    public bool IsWriterReady => _writerReady;

    public bool IsRingOpen => _context.Writer.IsRingOpen;

    public long FramesWritten => _framesWritten;

    public long PlaybackDiscarded => _playbackDiscarded;

    public double Position => _displayedPosition;

    public double CurrentLength => _context.Tracks.Current?.Stream?.GetLength() ?? 0.0;

    public void Initialize()
    {
        SetupCaptureBus();

        _player = new AudioStreamPlayer { Bus = CaptureBusName };
        _context.Main.AddChild(_player);
        _player.Finished += OnPlaybackFinished;

        _context.Tracks.CurrentChanged += OnCurrentChanged;
    }

    public void Shutdown()
    {
        _context.Tracks.CurrentChanged -= OnCurrentChanged;
        if (_player != null)
        {
            _player.Finished -= OnPlaybackFinished;
        }
    }

    public void Process(double delta)
    {
        if (_playing)
        {
            DrainCapture();
            double position = _player.GetPlaybackPosition();
            if (position > _displayedPosition)
            {
                _displayedPosition = position;
            }
            UpdateProgress(_displayedPosition);
            if (_writerReady && _capture != null)
            {
                _playbackDiscarded = _capture.GetDiscardedFrames() - _discardedAtStart;
            }
        }

        if (_context.Writer.RetryOpen)
        {
            _retryTimer += delta;
            if (_retryTimer >= RingRetryIntervalSeconds)
            {
                _retryTimer = 0.0;
                if (_context.Writer.EnsureOpen(out _))
                {
                    _capture?.ClearBuffer();
                    _discardedAtStart = _capture?.GetDiscardedFrames() ?? 0;
                    _playbackDiscarded = 0;
                    _writerReady = true;
                    _context.Writer.SetInjectEnabled(_playing);
                    _context.Status.Set("ringbuffer online (0x05)");
                }
            }
        }
    }

    public void Select(Track track, bool continuePlaying)
    {
        if (track == null)
        {
            return;
        }
        _context.Tracks.SetCurrent(track, continuePlaying);
    }

    public void Play()
    {
        if (_playing)
        {
            StopPlayback(true);
            return;
        }

        if (_paused)
        {
            Resume();
            return;
        }

        if (_context.Tracks.Current == null)
        {
            _context.Status.Set("请先导入音频");
            return;
        }

        bool ringReady = _context.Writer.EnsureOpen(out string error);
        if (ringReady)
        {
            _context.Writer.SetInjectEnabled(true);
        }
        else
        {
            GD.PushWarning($"[Soundpad] {error}");
        }

        if (_player.Playing)
        {
            _player.Stop();
        }
        _capture?.ClearBuffer();
        _framesWritten = 0;
        _discardedAtStart = _capture?.GetDiscardedFrames() ?? 0;
        _playbackDiscarded = 0;
        _displayedPosition = _pendingSeek;
        _player.Play((float)_pendingSeek);
        _playing = true;
        _writerReady = ringReady;
        StateChanged?.Invoke();
        UpdateProgress(_pendingSeek);
        _context.Status.Set(ringReady ? $"playing..." : $"playing without apo... (ring error: {error})");
    }

    public void StopPlayback(bool updateStatus)
    {
        if (_player != null && _player.Playing)
        {
            _player.Stop();
        }
        if (_player != null)
        {
            _player.StreamPaused = false;
        }
        _capture?.ClearBuffer();
        _playing = false;
        _paused = false;
        _pendingSeek = 0.0;
        _displayedPosition = 0.0;
        _context.Writer.SetInjectEnabled(false);
        StateChanged?.Invoke();
        UpdateProgress(0.0);
        if (updateStatus)
        {
            _context.Status.Set($"已停止 (totall: {_framesWritten:N0} frame)");
        }
    }

    public void TogglePlayPause()
    {
        if (_playing)
        {
            Pause();
        }
        else if (_paused)
        {
            Resume();
        }
        else
        {
            Play();
        }
    }

    public void TogglePlayback()
    {
        if (_playing)
        {
            StopPlayback(true);
        }
        else if (_paused)
        {
            Resume();
        }
        else
        {
            Play();
        }
    }

    public void TogglePause()
    {
        if (_playing)
        {
            Pause();
        }
        else if (_paused)
        {
            Resume();
        }
        else
        {
            Play();
        }
    }

    public void Pause()
    {
        if (!_playing || _player == null)
        {
            return;
        }
        _player.StreamPaused = true;
        _playing = false;
        _paused = true;
        _context.Writer.SetInjectEnabled(false);
        StateChanged?.Invoke();
        _context.Status.Set("已暂停");
    }

    public void Resume()
    {
        if (!_paused || _player == null)
        {
            return;
        }
        _player.StreamPaused = false;
        _capture?.ClearBuffer();
        _discardedAtStart = _capture?.GetDiscardedFrames() ?? 0;
        _playbackDiscarded = 0;
        _playing = true;
        _paused = false;
        _context.Writer.SetInjectEnabled(true);
        StateChanged?.Invoke();
        _context.Status.Set("继续播放");
    }

    public void PlayPrevious()
    {
        IReadOnlyList<Track> items = _context.Tracks.Items;
        if (items.Count == 0)
        {
            return;
        }
        int index = _context.Tracks.Current == null ? 0 : _context.Tracks.IndexOf(_context.Tracks.Current);
        index = (index - 1 + items.Count) % items.Count;
        Select(items[index], _playing);
    }

    public void PlayNext()
    {
        IReadOnlyList<Track> items = _context.Tracks.Items;
        if (items.Count == 0)
        {
            return;
        }
        int index = _context.Tracks.Current == null ? -1 : _context.Tracks.IndexOf(_context.Tracks.Current);
        index = (index + 1) % items.Count;
        Select(items[index], _playing);
    }

    public void SeekRatio(double ratio)
    {
        SeekTo(Math.Clamp(ratio, 0.0, 1.0) * CurrentLength);
    }

    public void SetMonitorEnabled(bool enabled)
    {
        int index = AudioServer.GetBusIndex(CaptureBusName);
        if (index >= 0)
        {
            AudioServer.SetBusMute(index, !enabled);
        }
        _context.Status.Set(enabled ? "本地监听: 开" : "本地监听: 关 (仅注入)");
    }

    private void OnCurrentChanged(Track track, bool continuePlaying)
    {
        bool wasPlaying = _playing || continuePlaying;
        StopPlayback(false);
        _player.Stream = track?.Stream;
        _pendingSeek = 0.0;
        _displayedPosition = 0.0;
        UpdateProgress(0.0);
        if (track != null)
        {
            _context.Status.Set($"已选择 {track.Name} ({track.Stream.GetLength():0.0}s)");
        }
        StateChanged?.Invoke();
        if (wasPlaying && track != null)
        {
            Play();
        }
    }

    private void OnPlaybackFinished()
    {
        DrainCapture();
        _playing = false;
        _paused = false;
        _pendingSeek = 0.0;
        _displayedPosition = CurrentLength;
        _context.Writer.SetInjectEnabled(false);
        StateChanged?.Invoke();
        _context.Status.Set($"播放完成 (共写入 {_framesWritten:N0} 帧)");
        UpdateProgress(CurrentLength);
    }

    private void SetupCaptureBus()
    {
        int index = AudioServer.GetBusIndex(CaptureBusName);
        if (index == -1)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, CaptureBusName);
            AudioServer.SetBusSend(index, "Master");
        }

        for (int i = 0; i < AudioServer.GetBusEffectCount(index); i++)
        {
            AudioEffect effect = AudioServer.GetBusEffect(index, i);
            if (effect is AudioEffectCapture capture)
            {
                _capture = capture;
            }
            else if (effect is AudioEffectSpectrumAnalyzer analyzer)
            {
                _analyzer = analyzer;
            }
        }

        if (_capture == null)
        {
            _capture = new AudioEffectCapture { BufferLength = CaptureBufferSeconds };
            AudioServer.AddBusEffect(index, _capture);
        }
        if (_analyzer == null)
        {
            _analyzer = new AudioEffectSpectrumAnalyzer { BufferLength = 0.1f };
            AudioServer.AddBusEffect(index, _analyzer);
        }
    }

    private void DrainCapture()
    {
        if (_capture == null || !_writerReady)
        {
            return;
        }

        for (int i = 0; i < 64; ++i)
        {
            int available = _capture.GetFramesAvailable();
            if (available <= 0)
            {
                return;
            }
            Vector2[] frames = _capture.GetBuffer(available);
            if (frames.Length == 0 || _context.Writer.Write(frames) != Error.Ok)
            {
                return;
            }
            _framesWritten += frames.Length;
        }
    }

    private void SeekTo(double position)
    {
        double length = CurrentLength;
        if (length <= 0.0)
        {
            return;
        }

        _pendingSeek = Math.Clamp(position, 0.0, length);
        _displayedPosition = _pendingSeek;
        if (_player.Playing)
        {
            _player.Seek((float)_pendingSeek);
            _capture?.ClearBuffer();
        }
        UpdateProgress(_pendingSeek);
    }

    private void UpdateProgress(double position)
    {
        double length = CurrentLength;
        if (length <= 0.0)
        {
            ProgressChanged?.Invoke(0.0, "00:00 / 00:00");
            return;
        }

        double clamped = Math.Clamp(position, 0.0, length);
        ProgressChanged?.Invoke(clamped / length, $"{FormatTime(clamped)} / {FormatTime(length)}");
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)seconds;
        return $"{total / 60:00}:{total % 60:00}";
    }
}
