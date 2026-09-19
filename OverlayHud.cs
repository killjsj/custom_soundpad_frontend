using Godot;

public partial class OverlayHud : Control
{
    [Export] public int VisualizerMode { get; set; }
    [Export] public Label StatusLabel { get; set; }
    [Export] public Label FramesLabel { get; set; }
    [Export] public Label PlaylistLabel { get; set; }
    [Export] public Label TracksLabel { get; set; }
    [Export] public Label HotkeysLabel { get; set; }
    [Export] public AudioVisualizer Visualizer { get; set; }
    [Export] public ProgressBar ProgressBar { get; set; }
    [Export] public Label TimeLabel { get; set; }
    [Export] public Control Panel { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        if (Visualizer != null)
        {
            Visualizer.Mode = VisualizerMode;
        }
    }

    public void SetVisualizerMode(int mode)
    {
        VisualizerMode = mode;
        if (Visualizer != null)
        {
            Visualizer.Mode = mode;
        }
    }

    public void SetStatus(string text)
    {
        if (StatusLabel != null)
        {
            StatusLabel.Text = text;
        }
    }

    public void SetFrames(string text)
    {
        if (FramesLabel != null)
        {
            FramesLabel.Text = text;
        }
    }

    public void SetPlaylist(string text)
    {
        if (PlaylistLabel != null)
        {
            PlaylistLabel.Text = text;
        }
    }

    public void SetTracks(string text)
    {
        if (TracksLabel != null)
        {
            TracksLabel.Text = text;
        }
    }

    public void SetHotkeys(string text)
    {
        if (HotkeysLabel != null)
        {
            HotkeysLabel.Text = text;
        }
    }

    public void SetProgress(double ratio, string time)
    {
        if (ProgressBar != null)
        {
            ProgressBar.Value = Mathf.Clamp(ratio, 0.0, 1.0) * 100.0;
        }
        if (TimeLabel != null)
        {
            TimeLabel.Text = time;
        }
    }

    public int MeasureContentHeight()
    {
        return Panel == null ? 0 : (int)Mathf.Ceil(Panel.GetCombinedMinimumSize().Y);
    }
}
