using System;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class SoundpadContext : IDisposable
{
    public SoundpadMain Main { get; }
    public StatusService Status { get; }
    public WriterService Writer { get; }
    public TrackService Tracks { get; }
    public HotkeyService Hotkeys { get; }
    public PlaybackService Playback { get; }
    public DeviceService Devices { get; }
    public OverlayService Overlay { get; }
    public PlaylistService Playlist { get; }

    public bool Exiting { get; set; }

    public SoundpadContext(SoundpadMain main)
    {
        AppLog.Initialize();
        ApoLog.Rotate();

        Main = main;
        Status = new StatusService(this);
        Writer = new WriterService(this);
        Tracks = new TrackService(this);
        Hotkeys = new HotkeyService(this);
        Playback = new PlaybackService(this);
        Devices = new DeviceService(this);
        Overlay = new OverlayService(this);
        Playlist = new PlaylistService(this);

        Playback.RingRetryIntervalSeconds = main.RingRetryIntervalSeconds;
        Overlay.Enabled = main.OverlayEnabled;
        Overlay.Size = main.OverlaySize;
        Overlay.Offset = main.OverlayOffset;
        Overlay.Opacity = main.OverlayOpacity;
        Playlist.ConfiguredDefaultPath = main.DefaultPlaylistPath;
        Playlist.AutoSave = main.AutoSavePlaylist;

        Writer.Initialize();
        Hotkeys.Initialize();
        Playback.Initialize();
        Devices.Initialize();

        GD.Print($"[Soundpad] 用户数据目录: {OS.GetUserDataDir()}");
        GD.Print($"[Soundpad] 默认歌单路径: {ProjectSettings.GlobalizePath(Playlist.DefaultPath)}");
    }

    public void Process(double delta)
    {
        Playback.Process(delta);
        Playlist.Process(delta);
        Overlay.Process(delta);
    }

    public void Dispose()
    {
        Playlist.Shutdown();
        Playback.Shutdown();
        Overlay.Shutdown();
        Devices.Shutdown();
        Hotkeys.Shutdown();
        Writer.Shutdown();
    }
}
