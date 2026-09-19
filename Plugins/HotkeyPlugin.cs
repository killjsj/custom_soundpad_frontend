using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class HotkeyPlugin : SoundpadPlugin
{
    [Export] public Button HotkeySettingsButton { get; set; }
    [Export] public Window HotkeyDialog { get; set; }
    [Export] public Button HotkeyButton0 { get; set; }
    [Export] public Button HotkeyButton1 { get; set; }
    [Export] public Button HotkeyButton2 { get; set; }
    [Export] public Button HotkeyButton3 { get; set; }
    [Export] public CheckBox HotkeyPassthrough0 { get; set; }
    [Export] public CheckBox HotkeyPassthrough1 { get; set; }
    [Export] public CheckBox HotkeyPassthrough2 { get; set; }
    [Export] public CheckBox HotkeyPassthrough3 { get; set; }
    [Export] public Button HotkeyClear0 { get; set; }
    [Export] public Button HotkeyClear1 { get; set; }
    [Export] public Button HotkeyClear2 { get; set; }
    [Export] public Button HotkeyClear3 { get; set; }
    [Export] public Button HotkeyOkButton { get; set; }
    [Export] public Button HotkeyCancelButton { get; set; }

    private readonly Button[] _buttons = new Button[4];
    private readonly CheckBox[] _checks = new CheckBox[4];
    private readonly Button[] _clears = new Button[4];
    private readonly string[] _combos = new string[4];

    public override void OnReady()
    {
        _buttons[0] = HotkeyButton0;
        _buttons[1] = HotkeyButton1;
        _buttons[2] = HotkeyButton2;
        _buttons[3] = HotkeyButton3;
        _checks[0] = HotkeyPassthrough0;
        _checks[1] = HotkeyPassthrough1;
        _checks[2] = HotkeyPassthrough2;
        _checks[3] = HotkeyPassthrough3;
        _clears[0] = HotkeyClear0;
        _clears[1] = HotkeyClear1;
        _clears[2] = HotkeyClear2;
        _clears[3] = HotkeyClear3;

        HotkeySettingsButton.Pressed += OpenDialog;
        HotkeyDialog.CloseRequested += () =>
        {
            Context.Hotkeys.CancelCapture();
            HotkeyDialog.Hide();
        };
        HotkeyDialog.VisibilityChanged += OnDialogVisibilityChanged;

        var capture = new DialogKeyCapture { Name = "KeyCapture" };
        capture.KeyPressed = OnDialogKey;
        HotkeyDialog.AddChild(capture);

        for (int i = 0; i < 4; ++i)
        {
            int index = i;
            _buttons[index].Pressed += () => Context.Hotkeys.BeginCapture(combo =>
            {
                _combos[index] = combo;
                _buttons[index].Text = string.IsNullOrEmpty(combo) ? "未设置" : combo;
            });
            _clears[index].Pressed += () =>
            {
                _combos[index] = string.Empty;
                _buttons[index].Text = "未设置";
            };
        }

        HotkeyOkButton.Pressed += ApplyDialog;
        HotkeyCancelButton.Pressed += () =>
        {
            Context.Hotkeys.CancelCapture();
            HotkeyDialog.Hide();
        };
    }

    private bool OnDialogKey(InputEventKey key)
    {
        if (!Context.Hotkeys.IsCapturing)
        {
            return false;
        }
        return Context.Hotkeys.TryCompleteCapture(GlobalHotkeys.ComboFromGodot(key));
    }

    private void OnDialogVisibilityChanged()
    {
        if (!HotkeyDialog.Visible)
        {
            Context.Hotkeys.CancelCapture();
        }
    }

    private void OpenDialog()
    {
        HotkeyService hotkeys = Context.Hotkeys;
        HotkeyBinding[] bindings = { hotkeys.Play, hotkeys.Prev, hotkeys.Next, hotkeys.Pause };
        for (int i = 0; i < bindings.Length; ++i)
        {
            _combos[i] = bindings[i].Combo;
            _buttons[i].Text = string.IsNullOrEmpty(bindings[i].Combo) ? "未设置" : bindings[i].Combo;
            _checks[i].ButtonPressed = bindings[i].Passthrough;
        }
        HotkeyDialog.Popup();
    }

    private void ApplyDialog()
    {
        Context.Hotkeys.CancelCapture();
        HotkeyService hotkeys = Context.Hotkeys;
        hotkeys.Play.Combo = _combos[0];
        hotkeys.Play.Passthrough = _checks[0].ButtonPressed;
        hotkeys.Prev.Combo = _combos[1];
        hotkeys.Prev.Passthrough = _checks[1].ButtonPressed;
        hotkeys.Next.Combo = _combos[2];
        hotkeys.Next.Passthrough = _checks[2].ButtonPressed;
        hotkeys.Pause.Combo = _combos[3];
        hotkeys.Pause.Passthrough = _checks[3].ButtonPressed;
        hotkeys.ApplyGlobalBindings();
        hotkeys.NotifyChanged();
        Context.Status.Set("全局热键已更新");
        Context.Playlist.Flush();
        HotkeyDialog.Hide();
    }
}
