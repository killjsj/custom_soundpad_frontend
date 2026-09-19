using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class OverlayService
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x8000000;
    private const uint SwpNoSize = 0x1;
    private const uint SwpNoMove = 0x2;
    private const uint SwpNoActivate = 0x10;
    private const int MaxOverlayTracks = 7;
    private const int OverlayBaseHeight = 170;
    private static readonly IntPtr HwndTopmost = new IntPtr(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private readonly SoundpadContext _context;
    private Window _overlay;
    private OverlayHud _hud;
    private CheckButton _toggle;
    private CheckButton _lockToggle;
    private bool _nativeApplied;
    private double _nativeTimer;
    private bool _dragging;
    private Vector2I _dragOffset;

    public bool Enabled { get; set; } = true;

    public Vector2I Size { get; set; } = new Vector2I(420, 300);

    public Vector2I Offset { get; set; } = new Vector2I(16, 16);

    public int Opacity { get; set; } = 205;

    public int VisualizerMode { get; set; }

    public OverlayService(SoundpadContext context)
    {
        _context = context;
    }

    private bool Locked => _lockToggle == null || _lockToggle.ButtonPressed;

    public void Initialize(Window overlay, OverlayHud hud, CheckButton toggle, CheckButton lockToggle)
    {
        _overlay = overlay;
        _hud = hud;
        _toggle = toggle;
        _lockToggle = lockToggle;
        if (_overlay == null || _hud == null)
        {
            return;
        }

        _hud.SetVisualizerMode(VisualizerMode);
        _overlay.Visible = Enabled;
        _nativeApplied = false;
        _nativeTimer = 0.0;

        GlobalHotkeys hook = _context.Hotkeys.Hook;
        if (hook != null)
        {
            hook.MouseDownCallback = OnMouseDown;
            hook.MouseMoveCallback = OnMouseMove;
            hook.MouseUpCallback = OnMouseUp;
        }
        _context.Playback.ProgressChanged += OnProgressChanged;

        Place();
        if (_toggle != null)
        {
            _toggle.ButtonPressed = Enabled;
        }
    }

    public void Shutdown()
    {
        GlobalHotkeys hook = _context.Hotkeys.Hook;
        if (hook != null)
        {
            hook.MouseDownCallback = null;
            hook.MouseMoveCallback = null;
            hook.MouseUpCallback = null;
        }
        _context.Playback.ProgressChanged -= OnProgressChanged;
    }

    public void Process(double delta)
    {
        if (_nativeApplied || _overlay == null || !_overlay.Visible)
        {
            return;
        }
        _nativeTimer += delta;
        if (_nativeTimer >= 0.5)
        {
            _nativeApplied = true;
            ApplyNativeStyles();
        }
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (_overlay != null)
        {
            _overlay.Visible = enabled;
            if (enabled)
            {
                _nativeApplied = false;
                _nativeTimer = 0.0;
            }
        }
        _context.Status.Set(enabled ? "悬浮窗: on" : "悬浮窗: off");
    }

    public void SetLocked(bool locked)
    {
        ApplyNativeStyles();
        _context.Status.Set(locked ? "悬浮窗: locked" : "悬浮窗: movable");
    }

    public void SetVisualizerMode(int mode)
    {
        VisualizerMode = mode;
        _hud?.SetVisualizerMode(mode);
    }

    public void Refresh()
    {
        if (_hud == null || _overlay == null || !_overlay.Visible)
        {
            return;
        }

        _hud.SetStatus(_context.Status.Message);
        _hud.SetFrames($"已写入 {_context.Playback.FramesWritten:N0} 帧");
        _hud.SetPlaylist($"歌单: {_context.Playlist.DisplayName}");
        _hud.SetTracks(BuildTrackList());
        _hud.SetHotkeys(BuildHotkeyList());

        int height = Math.Max(OverlayBaseHeight, _hud.MeasureContentHeight());
        if (_overlay.Size.Y != height)
        {
            _overlay.Size = new Vector2I(Size.X, height);
        }
    }

    private void ApplyNativeStyles()
    {
        if (_overlay == null || !_overlay.Visible)
        {
            return;
        }
        IntPtr hwnd = GetOverlayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        bool locked = Locked;
        long exStyle = GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExLayered | WsExNoActivate;
        if (locked)
        {
            exStyle |= WsExTransparent;
        }
        else
        {
            exStyle &= ~WsExTransparent;
        }
        SetWindowLongPtrW(hwnd, GwlExStyle, new IntPtr(exStyle));
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        return;
    }

    private IntPtr GetOverlayHwnd()
    {
        if (_overlay == null)
        {
            return IntPtr.Zero;
        }
        long raw = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, _overlay.GetWindowId());
        return new IntPtr(raw);
    }

    private void Place()
    {
        if (_overlay == null)
        {
            return;
        }
        int screen = _context.Main.GetWindow().CurrentScreen;
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        _overlay.Size = Size;
        _overlay.Position = new Vector2I(
            usable.Position.X + usable.Size.X - Size.X - Offset.X,
            usable.Position.Y + Offset.Y);
    }

    private void OnProgressChanged(double ratio, string text)
    {
        _hud?.SetProgress(ratio, text);
    }

    private void OnMouseDown(int x, int y)
    {
        if (_overlay == null || !_overlay.Visible || _dragging)
        {
            return;
        }
        if (Locked)
        {
            return;
        }
        Rect2I rect = new Rect2I(_overlay.Position, _overlay.Size);
        if (!rect.HasPoint(new Vector2I(x, y)))
        {
            return;
        }
        _dragging = true;
        _dragOffset = new Vector2I(x, y) - _overlay.Position;
    }

    private void OnMouseMove(int x, int y)
    {
        if (!_dragging || _overlay == null)
        {
            return;
        }
        _overlay.Position = new Vector2I(x, y) - _dragOffset;
    }

    private void OnMouseUp()
    {
        _dragging = false;
    }

    private string BuildTrackList()
    {
        IReadOnlyList<Track> tracks = _context.Tracks.Items;
        if (tracks.Count == 0)
        {
            return "歌曲: (无)";
        }

        const int maxLines = MaxOverlayTracks;
        int count = tracks.Count;
        int start = 0;
        if (count > maxLines)
        {
            int currentIndex = _context.Tracks.Current == null ? 0 : _context.Tracks.IndexOf(_context.Tracks.Current);
            start = Math.Clamp(currentIndex - maxLines / 2, 0, count - maxLines);
        }
        int end = Math.Min(count, start + maxLines);

        var lines = new List<string>();
        if (start > 0)
        {
            lines.Add($"… 上方 {start} 首");
        }
        for (int i = start; i < end; ++i)
        {
            Track track = tracks[i];
            string line = (track == _context.Tracks.Current ? "▶ " : "  ") + track.Name;
            if (track.Loop)
            {
                line += " [循环]";
            }
            lines.Add(line);
            if (!string.IsNullOrEmpty(track.Hotkey))
            {
                lines.Add("   热键：" + track.Hotkey);
            }
        }
        if (end < count)
        {
            lines.Add($"… 下方 {count - end} 首");
        }
        return string.Join("\n", lines);
    }

    private string BuildHotkeyList()
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(_context.Hotkeys.Play.Combo))
        {
            lines.Add($"播放/停止: {_context.Hotkeys.Play.Combo}");
        }
        if (!string.IsNullOrEmpty(_context.Hotkeys.Pause.Combo))
        {
            lines.Add($"暂停/恢复: {_context.Hotkeys.Pause.Combo}");
        }
        if (!string.IsNullOrEmpty(_context.Hotkeys.Prev.Combo))
        {
            lines.Add($"上一首: {_context.Hotkeys.Prev.Combo}");
        }
        if (!string.IsNullOrEmpty(_context.Hotkeys.Next.Combo))
        {
            lines.Add($"下一首: {_context.Hotkeys.Next.Combo}");
        }
        if (lines.Count == 0)
        {
            lines.Add("热键: 未设置");
        }
        return string.Join("\n", lines);
    }
}
