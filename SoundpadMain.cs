using CustomSoundpad;
using Godot;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public partial class SoundpadMain : Control
{
    [Export] public Vector2I WindowStartSize { get; set; } = new Vector2I(1280, 720);
    [Export] public Vector2I WindowMinSize { get; set; } = new Vector2I(0, 0);
    [Export] public Vector2I WindowMaxSize { get; set; } = new Vector2I(0, 0);
    [Export] public float RingRetryIntervalSeconds { get; set; } = 2.5f;
    [Export] public bool OverlayEnabled { get; set; } = true;
    [Export] public Vector2I OverlaySize { get; set; } = new Vector2I(420, 300);
    [Export] public Vector2I OverlayOffset { get; set; } = new Vector2I(16, 16);
    [Export] public int OverlayOpacity { get; set; } = 205;
    [Export] public string DefaultPlaylistPath { get; set; } = string.Empty;
    [Export] public bool AutoSavePlaylist { get; set; } = true;

    [Export] public PluginHost PluginHost { get; set; }

    public SoundpadContext Context { get; private set; }

    public override void _Ready()
    {
        Window window = GetWindow();
        window.Unresizable = false;
        window.MinSize = WindowMinSize;
        window.InitialPosition = Window.WindowInitialPosition.CenterScreenWithMouseFocus;
        if (WindowMaxSize.X > 0 && WindowMaxSize.Y > 0)
        {
            window.MaxSize = WindowMaxSize;
        }
        if (WindowStartSize.X > 0 && WindowStartSize.Y > 0)
        {
            window.Size = WindowStartSize;
        }

        Context = new SoundpadContext(this);

        if (PluginHost == null)
        {
            GD.PushError("[SoundpadMain] PluginHost 未在场景中指定");
            return;
        }
        PluginHost.Load(Context);
    }

    public override void _Process(double delta)
    {
        Context?.Process(delta);
    }

    public override void _ExitTree()
    {
        if (Context == null)
        {
            return;
        }
        Context.Exiting = true;
        PluginHost?.Unload();
        Context.Dispose();
        Context = null;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return PluginHost != null && PluginHost.CanDropData(atPosition, data);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        PluginHost?.DropData(atPosition, data);
    }
}
