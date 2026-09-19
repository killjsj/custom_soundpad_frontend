using Godot;

public partial class TrackRow : HBoxContainer
{
    [Export] public Label NameLabel { get; set; }
    [Export] public Label StatusLabel { get; set; }
    [Export] public Button SettingsButton { get; set; }
    [Export] public Button DeleteButton { get; set; }
}
