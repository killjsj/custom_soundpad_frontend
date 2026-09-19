using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

[SupportedOSPlatform("windows")]
public partial class GlobalHotkeys : Node
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_QUIT = 0x0012;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP_REGISTER_HOTKEY = 0x8001;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int VK_LBUTTON = 0x01;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_ESCAPE = 0x1B;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private sealed class Entry
    {
        public int Id;
        public bool Passthrough;
        public Action Callback;
    }

    private static readonly (JoyButton Button, string Name)[] PadButtonMap =
    {
        (JoyButton.A, "手柄A"),
        (JoyButton.B, "手柄B"),
        (JoyButton.X, "手柄X"),
        (JoyButton.Y, "手柄Y"),
        (JoyButton.LeftShoulder, "手柄LB"),
        (JoyButton.RightShoulder, "手柄RB"),
        (JoyButton.Back, "手柄Back"),
        (JoyButton.Start, "手柄Start"),
        (JoyButton.LeftStick, "手柄L3"),
        (JoyButton.RightStick, "手柄R3"),
        (JoyButton.DpadUp, "手柄十字上"),
        (JoyButton.DpadDown, "手柄十字下"),
        (JoyButton.DpadLeft, "手柄十字左"),
        (JoyButton.DpadRight, "手柄十字右"),
    };

    private static readonly (JoyAxis Axis, string Name)[] PadAxisMap =
    {
        (JoyAxis.TriggerLeft, "手柄LT"),
        (JoyAxis.TriggerRight, "手柄RT"),
    };

    private readonly object _lock = new object();
    private readonly Dictionary<string, List<Entry>> _map = new Dictionary<string, List<Entry>>();
    private readonly Dictionary<uint, uint> _lastKeyDown = new Dictionary<uint, uint>();
    private readonly HashSet<string> _padHeld = new HashSet<string>();
    private readonly List<string> _padCurrent = new List<string>();
    private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
    private readonly Dictionary<string, int> _systemHotkeys = new Dictionary<string, int>();
    private readonly Dictionary<int, string> _systemHotkeyCombos = new Dictionary<int, string>();
    private readonly HashSet<string> _systemHotkeyPending = new HashSet<string>();
    private readonly ConcurrentQueue<(string Combo, uint Modifiers, uint Vk)> _pendingSystemRegister = new ConcurrentQueue<(string, uint, uint)>();
    private readonly ConcurrentQueue<int> _pendingSystemUnregister = new ConcurrentQueue<int>();
    private readonly HookProc _hookProc;
    private Thread _thread;
    private IntPtr _hook;
    private IntPtr _mouseHook;
    private uint _threadId;
    private volatile bool _capturing;
    private volatile bool _shutdown;
    private Action<string> _captureCallback;
    private int _nextId = 1;
    private int _nextSystemId = 1;
    private double _padPollTimer;
    private const double PadPollInterval = 1.0 / 20.0;

    public bool IsCapturing => _capturing;

    public Action<int, int> MouseDownCallback;
    public Action<int, int> MouseMoveCallback;
    public Action MouseUpCallback;

    public GlobalHotkeys()
    {
        _hookProc = HookCallback;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "GlobalHotkeys" };
        _thread.Start();
    }

    public override void _ExitTree()
    {
        _shutdown = true;
        IntPtr hook = _hook;
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            _hook = IntPtr.Zero;
        }
        IntPtr mouseHook = _mouseHook;
        if (mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public override void _Process(double delta)
    {
        while (_queue.TryDequeue(out Action action))
        {
            action?.Invoke();
        }

        _padPollTimer += delta;
        if (_padPollTimer >= PadPollInterval)
        {
            _padPollTimer = 0.0;
            PollGamepad();
        }
    }

    public int Register(string combo, bool passthrough, Action callback)
    {
        if (string.IsNullOrEmpty(combo) || callback == null)
        {
            return 0;
        }
        int newId;
        lock (_lock)
        {
            newId = _nextId++;
            if (!_map.TryGetValue(combo, out List<Entry> entries))
            {
                entries = new List<Entry>();
                _map[combo] = entries;
            }
            entries.Add(new Entry { Id = newId, Passthrough = passthrough, Callback = callback });
        }
        TryRegisterSystemHotkey(combo, passthrough);
        return newId;
    }

    public void Unregister(int id)
    {
        if (id == 0)
        {
            return;
        }
        bool releaseAny = false;
        lock (_lock)
        {
            foreach (List<Entry> entries in _map.Values)
            {
                entries.RemoveAll(entry => entry.Id == id);
            }
            foreach (KeyValuePair<string, List<Entry>> pair in _map)
            {
                if (pair.Value.Count > 0 || !_systemHotkeys.TryGetValue(pair.Key, out int systemId))
                {
                    continue;
                }
                _systemHotkeys.Remove(pair.Key);
                _systemHotkeyCombos.Remove(systemId);
                _pendingSystemUnregister.Enqueue(systemId);
                releaseAny = true;
            }
        }
        if (releaseAny)
        {
            WakeHookThread();
        }
    }

    public void SetPassthrough(int id, bool passthrough)
    {
        lock (_lock)
        {
            foreach (List<Entry> entries in _map.Values)
            {
                foreach (Entry entry in entries)
                {
                    if (entry.Id == id)
                    {
                        entry.Passthrough = passthrough;
                    }
                }
            }
        }
    }

    private void TryRegisterSystemHotkey(string combo, bool passthrough)
    {
        if (passthrough)
        {
            return;
        }
        if (!TryParseCombo(combo, out uint modifiers, out uint vk))
        {
            return;
        }
        lock (_lock)
        {
            if (_systemHotkeys.ContainsKey(combo) || !_systemHotkeyPending.Add(combo))
            {
                return;
            }
        }
        _pendingSystemRegister.Enqueue((combo, modifiers, vk));
        WakeHookThread();
    }

    private void WakeHookThread()
    {
        uint threadId = _threadId;
        if (threadId != 0)
        {
            PostThreadMessage(threadId, WM_APP_REGISTER_HOTKEY, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void ProcessSystemHotkeyQueue()
    {
        while (_pendingSystemRegister.TryDequeue(out (string Combo, uint Modifiers, uint Vk) item))
        {
            int id = _nextSystemId++;
            if (RegisterHotKey(IntPtr.Zero, id, item.Modifiers | MOD_NOREPEAT, item.Vk))
            {
                lock (_lock)
                {
                    _systemHotkeyPending.Remove(item.Combo);
                    _systemHotkeys[item.Combo] = id;
                    _systemHotkeyCombos[id] = item.Combo;
                }
                GD.Print($"[GlobalHotkeys] RegisterHotKey ok: {item.Combo} (id={id})");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                lock (_lock)
                {
                    _systemHotkeyPending.Remove(item.Combo);
                }
                GD.PushWarning($"[GlobalHotkeys] RegisterHotKey failed: {item.Combo} (err={error})");
            }
        }

        while (_pendingSystemUnregister.TryDequeue(out int id))
        {
            UnregisterHotKey(IntPtr.Zero, id);
        }
    }

    private bool IsSystemHotkey(string combo)
    {
        lock (_lock)
        {
            return _systemHotkeys.ContainsKey(combo);
        }
    }

    private static bool TryParseCombo(string combo, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrEmpty(combo))
        {
            return false;
        }
        string[] parts = combo.Split('+');
        if (parts.Length < 2)
        {
            return false;
        }
        for (int i = 0; i < parts.Length - 1; ++i)
        {
            switch (parts[i])
            {
                case "Ctrl":
                    modifiers |= MOD_CONTROL;
                    break;
                case "Alt":
                    modifiers |= MOD_ALT;
                    break;
                case "Shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "Win":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }
        return TryVkFromKeyName(parts[^1], out vk);
    }

    private static bool TryVkFromKeyName(string name, out uint vk)
    {
        vk = 0;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        if (name.Length == 1)
        {
            char c = name[0];
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                vk = c;
                return true;
            }
        }
        if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.Substring(1), out int functionKey) && functionKey >= 1 && functionKey <= 24)
        {
            vk = (uint)(0x6F + functionKey);
            return true;
        }
        if (name.Length == 4 && name.StartsWith("Num") && char.IsDigit(name[3]))
        {
            vk = (uint)(0x60 + (name[3] - '0'));
            return true;
        }
        switch (name)
        {
            case "Backspace": vk = 0x08; return true;
            case "Tab": vk = 0x09; return true;
            case "Enter": vk = 0x0D; return true;
            case "Pause": vk = 0x13; return true;
            case "CapsLock": vk = 0x14; return true;
            case "Esc": vk = 0x1B; return true;
            case "Space": vk = 0x20; return true;
            case "PageUp": vk = 0x21; return true;
            case "PageDown": vk = 0x22; return true;
            case "End": vk = 0x23; return true;
            case "Home": vk = 0x24; return true;
            case "Left": vk = 0x25; return true;
            case "Up": vk = 0x26; return true;
            case "Right": vk = 0x27; return true;
            case "Down": vk = 0x28; return true;
            case "PrintScreen": vk = 0x2C; return true;
            case "Insert": vk = 0x2D; return true;
            case "Delete": vk = 0x2E; return true;
            case "Num*": vk = 0x6A; return true;
            case "Num+": vk = 0x6B; return true;
            case "Num-": vk = 0x6D; return true;
            case "Num.": vk = 0x6E; return true;
            case "Num/": vk = 0x6F; return true;
            case ";": vk = 0xBA; return true;
            case "=": vk = 0xBB; return true;
            case ",": vk = 0xBC; return true;
            case "-": vk = 0xBD; return true;
            case ".": vk = 0xBE; return true;
            case "/": vk = 0xBF; return true;
            case "`": vk = 0xC0; return true;
            case "[": vk = 0xDB; return true;
            case "\\": vk = 0xDC; return true;
            case "]": vk = 0xDD; return true;
            case "'": vk = 0xDE; return true;
            default: return false;
        }
    }

    public void StartCapture(Action<string> callback)
    {
        _captureCallback = callback;
        _capturing = true;
    }

    public void CancelCapture()
    {
        _capturing = false;
        _captureCallback = null;
    }

    public bool TryCompleteCapture(string combo)
    {
        if (!_capturing || string.IsNullOrEmpty(combo))
        {
            return false;
        }
        CompleteCapture(combo);
        return true;
    }

    public static string ComboFromGodot(InputEventKey key)
    {
        var parts = new List<string>();
        if (key.CtrlPressed)
        {
            parts.Add("Ctrl");
        }
        if (key.AltPressed)
        {
            parts.Add("Alt");
        }
        if (key.ShiftPressed)
        {
            parts.Add("Shift");
        }
        if (key.MetaPressed)
        {
            parts.Add("Win");
        }
        Key code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        parts.Add(GodotKeyName(code));
        return string.Join("+", parts);
    }

    private static string GodotKeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }
        if (key >= Key.Key0 && key <= Key.Key9)
        {
            return ((char)('0' + (key - Key.Key0))).ToString();
        }
        if (key >= Key.F1 && key <= Key.F24)
        {
            return "F" + (key - Key.F1 + 1);
        }
        if (key >= Key.Kp0 && key <= Key.Kp9)
        {
            return "Num" + (key - Key.Kp0);
        }
        return key switch
        {
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Enter or Key.KpEnter => "Enter",
            Key.Space => "Space",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.Pageup => "PageUp",
            Key.Pagedown => "PageDown",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Capslock => "CapsLock",
            Key.Pause => "Pause",
            Key.Print => "PrintScreen",
            Key.Minus => "-",
            Key.Equal => "=",
            Key.Bracketleft => "[",
            Key.Bracketright => "]",
            Key.Backslash => "\\",
            Key.Semicolon => ";",
            Key.Apostrophe => "'",
            Key.Comma => ",",
            Key.Period => ".",
            Key.Slash => "/",
            Key.Quoteleft => "`",
            Key.KpMultiply => "Num*",
            Key.KpAdd => "Num+",
            Key.KpSubtract => "Num-",
            Key.KpPeriod => "Num.",
            Key.KpDivide => "Num/",
            _ => "Key" + (int)key,
        };
    }

    public string GetHookStatus()
    {
        return $"keyboard={( _hook == IntPtr.Zero ? "none" : "0x" + _hook.ToInt64().ToString("X"))} mouse={( _mouseHook == IntPtr.Zero ? "none" : "0x" + _mouseHook.ToInt64().ToString("X"))} thread={_threadId} capturing={_capturing}";
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            GD.PushError($"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");
        }
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
        {
            GD.PushError($"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
        }
        GD.Print($"[GlobalHotkeys] hooks installed (thread={_threadId})");

        ProcessSystemHotkeyQueue();

        while (!_shutdown && GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_REGISTER_HOTKEY)
            {
                ProcessSystemHotkeyQueue();
                continue;
            }
            if (msg.message == WM_HOTKEY)
            {
                int hotkeyId = msg.wParam.ToInt32();
                string combo = null;
                lock (_lock)
                {
                    _systemHotkeyCombos.TryGetValue(hotkeyId, out combo);
                }
                if (!string.IsNullOrEmpty(combo) && !_capturing)
                {
                    TriggerCombo(combo);
                }
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = (int)wParam;
            if (message >= 0x0200 && message <= 0x020E)
            {
                return MouseCallback(message, lParam);
            }

            bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
            KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = data.vkCode;

            if (down && _capturing)
            {
                if (!IsModifierKey(vk))
                {
                    CompleteCapture(BuildCombo(vk));
                    return new IntPtr(1);
                }
            }

            if (down)
            {
                uint now = data.time;
                if (_lastKeyDown.TryGetValue(vk, out uint last) && now >= last && now - last < 400)
                {
                    return CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                _lastKeyDown[vk] = now;
                string combo = BuildCombo(vk);
                if (IsSystemHotkey(combo))
                {
                    return CallNextHookEx(_hook, nCode, wParam, lParam);
                }
                if (TriggerCombo(combo))
                {
                    return new IntPtr(1);
                }
            }
            else if (up)
            {
                if (IsSwallowed(BuildCombo(vk)))
                {
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int message, IntPtr lParam)
    {
        if (message == WM_MOUSEMOVE)
        {
            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0)
            {
                MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int mouseX = data.x;
                int mouseY = data.y;
                Action<int, int> move = MouseMoveCallback;
                if (move != null)
                {
                    _queue.Enqueue(() => move(mouseX, mouseY));
                }
            }
        }
        else if (message == WM_LBUTTONDOWN && MouseDownCallback != null)
        {
            MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int mouseX = data.x;
            int mouseY = data.y;
            Action<int, int> downHandler = MouseDownCallback;
            _queue.Enqueue(() => downHandler(mouseX, mouseY));
        }
        else if (message == WM_LBUTTONUP && MouseUpCallback != null)
        {
            Action upHandler = MouseUpCallback;
            _queue.Enqueue(() => upHandler());
        }

        return CallNextHookEx(_mouseHook, 0, new IntPtr(message), lParam);
    }

    private void CompleteCapture(string combo)
    {
        Action<string> callback = _captureCallback;
        _capturing = false;
        _captureCallback = null;
        _queue.Enqueue(() => callback?.Invoke(combo));
    }

    private bool TriggerCombo(string combo)
    {
        List<Action> actions = null;
        bool swallow = false;
        lock (_lock)
        {
            if (_map.TryGetValue(combo, out List<Entry> entries))
            {
                actions = new List<Action>();
                foreach (Entry entry in entries)
                {
                    actions.Add(entry.Callback);
                    if (!entry.Passthrough)
                    {
                        swallow = true;
                    }
                }
            }
        }
        if (actions != null)
        {
            foreach (Action action in actions)
            {
                Action captured = action;
                _queue.Enqueue(() => captured?.Invoke());
            }
        }
        return swallow;
    }

    private bool IsSwallowed(string combo)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(combo, out List<Entry> entries))
            {
                foreach (Entry entry in entries)
                {
                    if (!entry.Passthrough)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void PollGamepad()
    {
        Godot.Collections.Array<int> devices = Input.GetConnectedJoypads();
        if (devices.Count == 0)
        {
            _padHeld.Clear();
            return;
        }

        _padCurrent.Clear();
        foreach (int device in devices)
        {
            foreach ((JoyButton button, string name) in PadButtonMap)
            {
                if (Input.IsJoyButtonPressed(device, button))
                {
                    _padCurrent.Add(name);
                }
            }
            foreach ((JoyAxis axis, string name) in PadAxisMap)
            {
                if (Input.GetJoyAxis(device, axis) > 0.5f)
                {
                    _padCurrent.Add(name);
                }
            }
        }

        foreach (string name in _padCurrent)
        {
            if (_padHeld.Contains(name))
            {
                continue;
            }
            if (_capturing)
            {
                CompleteCapture(name);
                break;
            }
            TriggerCombo(name);
        }

        _padHeld.Clear();
        foreach (string name in _padCurrent)
        {
            _padHeld.Add(name);
        }
    }

    private static bool IsModifierKey(uint vk)
    {
        return vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU || vk == VK_LWIN || vk == VK_RWIN ||
            vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5;
    }

    private static string BuildCombo(uint vk)
    {
        var parts = new List<string>();
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
        {
            parts.Add("Alt");
        }
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
        {
            parts.Add("Shift");
        }
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(KeyName(vk));
        return string.Join("+", parts);
    }

    public static string KeyName(uint vk)
    {
        if (vk >= 0x41 && vk <= 0x5A)
        {
            return ((char)vk).ToString();
        }
        if (vk >= 0x30 && vk <= 0x39)
        {
            return ((char)vk).ToString();
        }
        if (vk >= 0x60 && vk <= 0x69)
        {
            return "Num" + (vk - 0x60);
        }
        if (vk >= 0x70 && vk <= 0x87)
        {
            return "F" + (vk - 0x6F);
        }
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "VK" + vk,
        };
    }
}
