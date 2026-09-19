using System.Runtime.Versioning;
using CustomSoundpad.Apo;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class StatusPlugin : SoundpadPlugin
{
    [Export] public Label StatusLabel { get; set; }
    [Export] public Label VersionLabel { get; set; }

    private double _timer;

    public override void OnReady()
    {
        if (VersionLabel != null)
        {
            string name = ProjectSettings.GetSetting("application/config/name", "apoFrontend").AsString();
            string version = ProjectSettings.GetSetting("application/config/version", "0.0").AsString();
            string hash = BuildInfo.GitHash;
            string suffix = string.IsNullOrEmpty(hash) || hash == "unknown" ? string.Empty : "+" + hash;
            VersionLabel.Text = $"{name} v{version}{suffix}";
        }

        Context.Status.Changed += Refresh;
        if (!ApoInstaller.IsProcessElevated())
        {
            Context.Status.Set("提示: 未以管理员身份运行，环形缓冲/APO 注册需要管理员权限");
        }
        Refresh();
    }

    public override void OnUnload()
    {
        Context.Status.Changed -= Refresh;
    }

    public override void OnProcess(double delta)
    {
        _timer += delta;
        if (_timer < 0.0542)
        {
            return;
        }
        _timer = 0.0;
        Refresh();
    }

    private void Refresh()
    {
        PlaybackService playback = Context.Playback;
        string ring = playback.IsRingOpen ? "已打开" : "未打开";
        string state = playback.IsPlaying ? "播放中" : (playback.IsPaused ? "已暂停" : "空闲");
        string loss = playback.PlaybackDiscarded > 0 ? $" | 播放丢帧 {playback.PlaybackDiscarded:N0}" : "";
        StatusLabel.Text = $"ApoWriter | 环形缓冲: {ring} | {state} | 已写入 {playback.FramesWritten:N0} 帧{loss} | {Context.Status.Message}";
        Context.Overlay.Refresh();
    }
}
