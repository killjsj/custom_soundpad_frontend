using Godot;

namespace CustomSoundpad;

public sealed class Track
{
    public string Name = string.Empty;
    public string Path = string.Empty;
    public AudioStream Stream;
    public string Hotkey = string.Empty;
    public bool HotkeyPassthrough;
    public bool Loop;
    public int HotkeyId;
}
