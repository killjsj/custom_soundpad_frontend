using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class DevicePlugin : SoundpadPlugin
{
    [Export] public VBoxContainer DeviceTable { get; set; }
    [Export] public PackedScene DeviceRowScene { get; set; }
    [Export] public Button RegisterButton { get; set; }
    [Export] public Button UnbindButton { get; set; }
    [Export] public Button UnregisterButton { get; set; }
    [Export] public float RefreshIntervalSeconds { get; set; } = 3.0f;

    private sealed class DeviceRowEntry
    {
        public string Id;
        public DeviceRow Row;
        public CheckBox Check;
    }

    private readonly List<DeviceRowEntry> _rows = new();
    private readonly HashSet<string> _checked = new();
    private double _refreshTimer;
    private string _lastSignature = string.Empty;

    public override void OnReady()
    {
        RegisterButton.Pressed += () => StartSetup(true);
        UnbindButton.Pressed += () => StartSetup(false);
        UnregisterButton.Pressed += Context.Devices.UnregisterApo;
        Context.Devices.DevicesChanged += UpdateTable;
        UpdateTable();
    }

    public override void OnUnload()
    {
        Context.Devices.DevicesChanged -= UpdateTable;
    }

    public override void OnProcess(double delta)
    {
        if (RefreshIntervalSeconds <= 0.0f || Context.Devices.IsBusy)
        {
            return;
        }
        _refreshTimer += delta;
        if (_refreshTimer < RefreshIntervalSeconds)
        {
            return;
        }
        _refreshTimer = 0.0;

        Godot.Collections.Dictionary devices = QueryDevices();
        if (devices == null)
        {
            return;
        }
        string signature = BuildSignature(devices);
        if (signature == _lastSignature)
        {
            return;
        }
        RebuildRows(devices, signature);
    }

    private void StartSetup(bool bind)
    {
        var selected = new List<string>();
        foreach (DeviceRowEntry entry in _rows)
        {
            if (entry.Check != null && entry.Check.ButtonPressed && !string.IsNullOrEmpty(entry.Id))
            {
                selected.Add(entry.Id);
            }
        }

        if (selected.Count == 0)
        {
            Context.Status.Set(bind ? "请先勾选要注册的设备" : "请先勾选要取消绑定的设备");
            return;
        }

        if (bind)
        {
            Context.Devices.Bind(selected);
        }
        else
        {
            Context.Devices.Unbind(selected);
        }
    }

    private void UpdateTable()
    {
        Godot.Collections.Dictionary devices = QueryDevices();
        RebuildRows(devices, BuildSignature(devices));
    }

    private Godot.Collections.Dictionary QueryDevices()
    {
        try
        {
            return Context.Devices.GetDevices();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"枚举采集设备失败: {exception.Message}");
            return null;
        }
    }

    private string BuildSignature(Godot.Collections.Dictionary devices)
    {
        if (devices == null || devices.Count == 0)
        {
            return "(empty)";
        }
        var entries = new List<string>();
        foreach (Variant key in devices.Keys)
        {
            string deviceId = key.AsString();
            entries.Add($"{deviceId}|{devices[key].AsString()}|{Context.Devices.IsBound(deviceId)}");
        }
        entries.Sort(StringComparer.Ordinal);
        return string.Join("\n", entries);
    }

    private void RebuildRows(Godot.Collections.Dictionary devices, string signature)
    {
        CaptureChecked();

        foreach (DeviceRowEntry entry in _rows)
        {
            entry.Row.QueueFree();
        }
        _rows.Clear();

        if (devices == null || devices.Count == 0)
        {
            AddRow(string.Empty, "(未检测到采集设备)", false, false);
        }
        else
        {
            foreach (Variant key in devices.Keys)
            {
                string deviceId = key.AsString();
                AddRow(deviceId, devices[key].AsString(), Context.Devices.IsBound(deviceId), true);
            }
        }
        _lastSignature = signature;
    }

    private void CaptureChecked()
    {
        _checked.Clear();
        foreach (DeviceRowEntry entry in _rows)
        {
            if (entry.Check != null && entry.Check.ButtonPressed && !string.IsNullOrEmpty(entry.Id))
            {
                _checked.Add(entry.Id);
            }
        }
    }

    private void AddRow(string deviceId, string name, bool bound, bool selectable)
    {
        DeviceRow row = DeviceRowScene.Instantiate<DeviceRow>();
        row.Name = "DeviceRow";
        row.Visible = true;
        row.Check.Disabled = !selectable;
        row.Check.ButtonPressed = selectable && _checked.Contains(deviceId);
        row.NameLabel.Text = name;
        row.StatusLabel.Text = bound ? "已绑定" : "未绑定";
        DeviceTable.AddChild(row);
        _rows.Add(new DeviceRowEntry { Id = deviceId, Row = row, Check = row.Check });
    }
}
