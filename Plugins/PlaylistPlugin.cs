using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class PlaylistPlugin : SoundpadPlugin
{
    [Export] public MenuButton PlaylistMenu { get; set; }
    [Export] public FileDialog PlaylistSaveDialog { get; set; }
    [Export] public FileDialog PlaylistLoadDialog { get; set; }

    private bool _mergeMode;

    public override void OnReady()
    {
        PlaylistSaveDialog.FileSelected += path => Context.Playlist.Save(path);
        PlaylistLoadDialog.FileSelected += path => Context.Playlist.Load(path, _mergeMode);

        PopupMenu popup = PlaylistMenu.GetPopup();
        popup.AddItem("保存歌单", 0);
        popup.AddItem("读取歌单", 1);
        popup.AddItem("混合歌单", 2);
        popup.AddItem("恢复默认歌单", 3);
        popup.IdPressed += OnMenuPressed;

        Context.Playlist.Changed += UpdateLabel;
        Context.Playlist.Initialize();
        UpdateLabel();
        Context.Playlist.LoadDefaultIfExists();
    }

    public override void OnUnload()
    {
        Context.Playlist.Changed -= UpdateLabel;
    }

    private void OnMenuPressed(long id)
    {
        switch (id)
        {
            case 0:
                PlaylistSaveDialog.Popup();
                break;
            case 1:
                _mergeMode = false;
                PlaylistLoadDialog.Popup();
                break;
            case 2:
                _mergeMode = true;
                PlaylistLoadDialog.Popup();
                break;
            case 3:
                Context.Playlist.RestoreDefault();
                break;
        }
    }

    private void UpdateLabel()
    {
        if (PlaylistMenu == null)
        {
            return;
        }
        PlaylistMenu.Text = $"歌单: {Context.Playlist.DisplayName}";
        PlaylistMenu.TooltipText = Context.Playlist.Tooltip;
    }
}
