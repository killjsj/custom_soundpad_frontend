using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class TrackListPlugin : SoundpadPlugin
{
    [Export] public VBoxContainer AudioTable { get; set; }
    [Export] public PackedScene TrackRowScene { get; set; }
    [Export] public Window TrackDialog { get; set; }
    [Export] public Label TrackTitleLabel { get; set; }
    [Export] public Button TrackHotkeyButton { get; set; }
    [Export] public Button TrackClearButton { get; set; }
    [Export] public CheckBox TrackPassthroughCheck { get; set; }
    [Export] public CheckBox TrackLoopCheck { get; set; }
    [Export] public Button TrackOkButton { get; set; }
    [Export] public Button TrackCancelButton { get; set; }

    private readonly Dictionary<Track, TrackRow> _rows = new();
    private Track _dialogTrack;
    private string _dialogHotkey = string.Empty;

    public override void OnReady()
    {
        WireDialog();

        Context.Tracks.ContentChanged += RebuildRows;
        Context.Tracks.CurrentChanged += OnCurrentChanged;
        Context.Playback.StateChanged += RefreshRows;
        RebuildRows();
    }

    public override void OnUnload()
    {
        Context.Tracks.ContentChanged -= RebuildRows;
        Context.Tracks.CurrentChanged -= OnCurrentChanged;
        Context.Playback.StateChanged -= RefreshRows;
    }

    public override bool CanDropData(Vector2 atPosition, Variant data)
    {
        return data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("files");
    }

    public override bool DropData(Vector2 atPosition, Variant data)
    {
        var files = new List<string>();
        foreach (string path in data.AsGodotDictionary()["files"].AsStringArray())
        {
            if (TrackService.IsAudioFile(path))
            {
                files.Add(path);
            }
        }
        if (files.Count == 0)
        {
            return false;
        }

        Track first = Context.Tracks.ImportFiles(files);
        if (first != null)
        {
            Context.Playback.Select(first, Context.Playback.IsPlaying);
        }
        return true;
    }

    private void OnCurrentChanged(Track track, bool continuePlaying) => RefreshRows();

    private void RebuildRows()
    {
        foreach (TrackRow row in _rows.Values)
        {
            row.QueueFree();
        }
        _rows.Clear();

        foreach (Track track in Context.Tracks.Items)
        {
            AddRow(track);
        }
        RefreshRows();
    }

    private void AddRow(Track track)
    {
        TrackRow row = TrackRowScene.Instantiate<TrackRow>();
        row.Name = "TrackRow";
        row.Visible = true;
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.NameLabel.Text = track.Name;
        row.StatusLabel.Text = "已导入";
        row.SettingsButton.Pressed += () => OpenTrackSettings(track);
        row.DeleteButton.Pressed += () => RemoveTrack(track);
        row.GuiInput += @event => OnRowInput(@event, track);
        AudioTable.AddChild(row);
        _rows[track] = row;
    }

    private void RefreshRows()
    {
        Track current = Context.Tracks.Current;
        bool playing = Context.Playback.IsPlaying;
        bool paused = Context.Playback.IsPaused;
        foreach (KeyValuePair<Track, TrackRow> entry in _rows)
        {
            Track track = entry.Key;
            string status;
            if (track == current)
            {
                status = playing ? "播放中" : (paused ? "已暂停" : "已选择");
            }
            else
            {
                status = "已导入";
            }
            if (!string.IsNullOrEmpty(track.Hotkey))
            {
                status += " · " + track.Hotkey;
            }
            if (track.Loop)
            {
                status += " · 循环";
            }
            entry.Value.StatusLabel.Text = status;
        }
    }

    private void RemoveTrack(Track track)
    {
        if (track == null)
        {
            return;
        }
        Context.Tracks.Remove(track);
        Context.Status.Set($"已删除 {track.Name}");
    }

    private void OnRowInput(InputEvent @event, Track track)
    {
        if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        if (button.DoubleClick)
        {
            return;
        }

        if (track == Context.Tracks.Current)
        {
            Context.Playback.TogglePlayPause();
            return;
        }

        Context.Playback.Select(track, true);
    }

    private void WireDialog()
    {
        TrackDialog.CloseRequested += () =>
        {
            Context.Hotkeys.CancelCapture();
            TrackDialog.Hide();
        };
        TrackDialog.VisibilityChanged += OnDialogVisibilityChanged;

        var capture = new DialogKeyCapture { Name = "KeyCapture" };
        capture.KeyPressed = OnDialogKey;
        TrackDialog.AddChild(capture);
        TrackHotkeyButton.Pressed += () => Context.Hotkeys.BeginCapture(combo =>
        {
            _dialogHotkey = combo;
            TrackHotkeyButton.Text = string.IsNullOrEmpty(combo) ? "未设置" : combo;
        });
        TrackClearButton.Pressed += () =>
        {
            _dialogHotkey = string.Empty;
            TrackHotkeyButton.Text = "未设置";
        };
        TrackOkButton.Pressed += ApplyTrackDialog;
        TrackCancelButton.Pressed += () =>
        {
            Context.Hotkeys.CancelCapture();
            TrackDialog.Hide();
        };
    }

    private bool OnDialogKey(InputEventKey key)
    {
        if (!Context.Hotkeys.IsCapturing)
        {
            return false;
        }
        return Context.Hotkeys.TryCompleteCapture(GlobalHotkeys.ComboFromGodot(key));
    }

    private void OnDialogVisibilityChanged()
    {
        if (!TrackDialog.Visible)
        {
            Context.Hotkeys.CancelCapture();
        }
    }

    private void OpenTrackSettings(Track track)
    {
        _dialogTrack = track;
        _dialogHotkey = track.Hotkey;
        TrackTitleLabel.Text = track.Name;
        TrackHotkeyButton.Text = string.IsNullOrEmpty(track.Hotkey) ? "未设置" : track.Hotkey;
        TrackPassthroughCheck.ButtonPressed = track.HotkeyPassthrough;
        TrackLoopCheck.ButtonPressed = track.Loop;
        TrackDialog.Popup();
    }

    private void ApplyTrackDialog()
    {
        Context.Hotkeys.CancelCapture();
        if (_dialogTrack != null)
        {
            Track track = _dialogTrack;
            track.Hotkey = _dialogHotkey;
            track.HotkeyPassthrough = TrackPassthroughCheck.ButtonPressed;
            track.Loop = TrackLoopCheck.ButtonPressed;
            TrackService.ApplyLoop(track);
            Context.Tracks.NotifyChanged();
            Context.Status.Set($"{track.Name}: 热键 {(string.IsNullOrEmpty(track.Hotkey) ? "无" : track.Hotkey)}，循环 {(track.Loop ? "开" : "关")}");
            Context.Playlist.Flush();
        }
        TrackDialog.Hide();
    }
}
