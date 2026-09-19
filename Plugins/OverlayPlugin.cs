using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class OverlayPlugin : SoundpadPlugin
{
    [Export] public CheckButton OverlayToggle { get; set; }
    [Export] public CheckButton OverlayLockToggle { get; set; }
    [Export] public Window OverlayWindow { get; set; }
    [Export] public OverlayHud OverlayHud { get; set; }

    public override void OnReady()
    {
        OverlayToggle.Toggled += Context.Overlay.SetEnabled;
        OverlayLockToggle.Toggled += Context.Overlay.SetLocked;
        Context.Overlay.Initialize(OverlayWindow, OverlayHud, OverlayToggle, OverlayLockToggle);
    }

    public override void OnUnload()
    {
        OverlayToggle.Toggled -= Context.Overlay.SetEnabled;
        OverlayLockToggle.Toggled -= Context.Overlay.SetLocked;
    }
}
