using Godot;

public partial class DeviceRow : HBoxContainer
{
    [Export] public CheckBox Check { get; set; }
    [Export] public Label NameLabel { get; set; }
    [Export] public Label StatusLabel { get; set; }
}
