using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class PlaybackPlugin : SoundpadPlugin
{
    [Export] public Button OpenButton { get; set; }
    [Export] public Button PlayButton { get; set; }
    [Export] public CheckButton MonitorToggle { get; set; }
    [Export] public OptionButton VisualizerMode { get; set; }
    [Export] public AudioVisualizer Visualizer { get; set; }
    [Export] public FileDialog AudioFileDialog { get; set; }
    [Export] public ProgressBar ProgressBar { get; set; }
    [Export] public Label TimeLabel { get; set; }

    public override void OnReady()
    {
        OpenButton.Pressed += OnOpenPressed;
        PlayButton.Pressed += Context.Playback.Play;
        MonitorToggle.Toggled += Context.Playback.SetMonitorEnabled;
        VisualizerMode.Clear();
        VisualizerMode.AddItem("条形图", 0);
        VisualizerMode.AddItem("圆圈", 1);
        VisualizerMode.Select(0);
        VisualizerMode.ItemSelected += OnVisualizerModeSelected;
        if (Visualizer != null)
        {
            Visualizer.Mode = 0;
        }
        Context.Overlay.SetVisualizerMode(0);
        AudioFileDialog.FilesSelected += OnFilesSelected;
        ProgressBar.GuiInput += OnProgressGuiInput;

        Context.Playback.StateChanged += UpdatePlayButton;
        Context.Playback.ProgressChanged += OnProgressChanged;
        UpdatePlayButton();
    }

    public override void OnUnload()
    {
        Context.Playback.StateChanged -= UpdatePlayButton;
        Context.Playback.ProgressChanged -= OnProgressChanged;
    }

    private void OnOpenPressed()
    {
        AudioFileDialog.PopupCentered(new Vector2I(760, 520));
    }

    private void OnFilesSelected(string[] paths)
    {
        Track first = Context.Tracks.ImportFiles(paths);
        if (first != null)
        {
            Context.Playback.Select(first, Context.Playback.IsPlaying);
        }
    }

    private void OnVisualizerModeSelected(long index)
    {
        if (Visualizer != null)
        {
            Visualizer.Mode = (int)index;
        }
        Context.Overlay.SetVisualizerMode((int)index);
    }

    private void OnProgressGuiInput(InputEvent @event)
    {
        Vector2 position;
        if (@event is InputEventMouseButton button)
        {
            if (!button.Pressed || button.ButtonIndex != MouseButton.Left)
            {
                return;
            }
            position = button.Position;
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
            {
                return;
            }
            position = motion.Position;
        }
        else
        {
            return;
        }

        double ratio = Mathf.Clamp(position.X / Mathf.Max(1.0f, ProgressBar.Size.X), 0.0f, 1.0f);
        Context.Playback.SeekRatio(ratio);
    }

    private void OnProgressChanged(double ratio, string text)
    {
        ProgressBar.Value = ratio * 100.0;
        TimeLabel.Text = text;
    }

    private void UpdatePlayButton()
    {
        PlayButton.Text = Context.Playback.IsPlaying ? "停止" : (Context.Playback.IsPaused ? "继续" : "播放");
    }
}
