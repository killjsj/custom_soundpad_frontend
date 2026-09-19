using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class HotkeyService
{
    private readonly SoundpadContext _context;
    private readonly Dictionary<Track, int> _trackBindings = new();
    private GlobalHotkeys _hook;

    public HotkeyBinding Play { get; } = new();
    public HotkeyBinding Pause { get; } = new();
    public HotkeyBinding Prev { get; } = new();
    public HotkeyBinding Next { get; } = new();

    public event Action BindingsChanged;

    public HotkeyService(SoundpadContext context)
    {
        _context = context;
    }

    public GlobalHotkeys Hook => _hook;

    public bool IsCapturing => _hook != null && _hook.IsCapturing;

    public void Initialize()
    {
        _hook = new GlobalHotkeys { Name = "GlobalHotkeys" };
        _context.Main.AddChild(_hook);
        _context.Tracks.ContentChanged += OnTracksChanged;
        ApplyGlobalBindings();
    }

    public void Shutdown()
    {
        _context.Tracks.ContentChanged -= OnTracksChanged;
        _trackBindings.Clear();
        if (_hook != null)
        {
            _hook.QueueFree();
            _hook = null;
        }
    }

    public void ApplyGlobalBindings()
    {
        RegisterBinding(Play, () => _context.Playback.TogglePlayback());
        RegisterBinding(Pause, () => _context.Playback.TogglePause());
        RegisterBinding(Prev, () => _context.Playback.PlayPrevious());
        RegisterBinding(Next, () => _context.Playback.PlayNext());
    }

    public void NotifyChanged() => BindingsChanged?.Invoke();

    public void BeginCapture(Action<string> callback)
    {
        if (_hook == null)
        {
            return;
        }

        if (_hook.IsCapturing)
        {
            _hook.CancelCapture();
            _context.Status.Set("已取消热键录制");
            return;
        }

        _context.Status.Set("请按下要绑定的键 (键盘/手柄)，再次点击按钮可取消...");
        _hook.StartCapture(combo =>
        {
            if (string.IsNullOrEmpty(combo))
            {
                _context.Status.Set("已取消热键设置");
                return;
            }
            callback(combo);
            _context.Status.Set($"热键: {combo}");
        });
    }

    public void CancelCapture()
    {
        if (_hook != null && _hook.IsCapturing)
        {
            _hook.CancelCapture();
            _context.Status.Set("已取消热键录制");
        }
    }

    public bool TryCompleteCapture(string combo)
    {
        return _hook != null && _hook.TryCompleteCapture(combo);
    }

    private void OnTracksChanged() => RegisterTrackHotkeys();

    private void RegisterTrackHotkeys()
    {
        if (_hook == null)
        {
            return;
        }

        var current = new HashSet<Track>(_context.Tracks.Items);
        var stale = new List<Track>();
        foreach (KeyValuePair<Track, int> entry in _trackBindings)
        {
            if (!current.Contains(entry.Key))
            {
                stale.Add(entry.Key);
            }
        }
        foreach (Track track in stale)
        {
            _hook.Unregister(_trackBindings[track]);
            track.HotkeyId = 0;
            _trackBindings.Remove(track);
        }

        foreach (Track track in _context.Tracks.Items)
        {
            if (_trackBindings.TryGetValue(track, out int id))
            {
                _hook.Unregister(id);
                _trackBindings.Remove(track);
            }
            track.HotkeyId = 0;
            if (!string.IsNullOrEmpty(track.Hotkey))
            {
                int newId = _hook.Register(track.Hotkey, track.HotkeyPassthrough, () => OnTrackHotkey(track));
                _trackBindings[track] = newId;
                track.HotkeyId = newId;
            }
        }
    }

    private void RegisterBinding(HotkeyBinding binding, Action action)
    {
        if (_hook == null)
        {
            return;
        }
        if (binding.Id != 0)
        {
            _hook.Unregister(binding.Id);
            binding.Id = 0;
        }
        if (!string.IsNullOrEmpty(binding.Combo))
        {
            binding.Id = _hook.Register(binding.Combo, binding.Passthrough, action);
        }
    }

    private void OnTrackHotkey(Track track)
    {
        if (track == _context.Tracks.Current)
        {
            _context.Playback.TogglePlayback();
        }
        else
        {
            _context.Playback.Select(track, _context.Playback.IsPlaying);
        }
    }
}
