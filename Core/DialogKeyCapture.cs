using System;
using Godot;

namespace CustomSoundpad;

public sealed partial class DialogKeyCapture : Node
{
    public Func<InputEventKey, bool> KeyPressed { get; set; }

    public override void _Ready()
    {
        SetProcessInput(true);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (KeyPressed != null && KeyPressed(key))
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
