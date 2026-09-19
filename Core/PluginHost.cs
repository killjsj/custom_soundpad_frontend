using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed partial class PluginHost : Node
{
    private readonly List<SoundpadPlugin> _plugins = new();

    public IReadOnlyList<SoundpadPlugin> Plugins => _plugins;

    public void Load(SoundpadContext context)
    {
        foreach (Node child in GetChildren())
        {
            if (child is not SoundpadPlugin plugin)
            {
                continue;
            }

            plugin.Bind(context);
            _plugins.Add(plugin);
            Guard(plugin, "OnLoad", plugin.OnLoad);
        }

        foreach (SoundpadPlugin plugin in _plugins)
        {
            Guard(plugin, "OnReady", plugin.OnReady);
        }

        GD.Print($"[PluginHost] 已加载 {_plugins.Count} 个插件");
    }

    public override void _Process(double delta)
    {
        foreach (SoundpadPlugin plugin in _plugins)
        {
            SoundpadPlugin captured = plugin;
            Guard(captured, "OnProcess", () => captured.OnProcess(delta));
        }
    }

    public void Unload()
    {
        for (int i = _plugins.Count - 1; i >= 0; --i)
        {
            SoundpadPlugin plugin = _plugins[i];
            Guard(plugin, "OnUnload", plugin.OnUnload);
        }
        _plugins.Clear();
    }

    public bool CanDropData(Vector2 atPosition, Variant data)
    {
        foreach (SoundpadPlugin plugin in _plugins)
        {
            bool handled = false;
            SoundpadPlugin captured = plugin;
            Guard(captured, "CanDropData", () => handled = captured.CanDropData(atPosition, data));
            if (handled)
            {
                return true;
            }
        }
        return false;
    }

    public bool DropData(Vector2 atPosition, Variant data)
    {
        foreach (SoundpadPlugin plugin in _plugins)
        {
            bool handled = false;
            SoundpadPlugin captured = plugin;
            Guard(captured, "DropData", () => handled = captured.DropData(atPosition, data));
            if (handled)
            {
                return true;
            }
        }
        return false;
    }

    private static void Guard(SoundpadPlugin plugin, string action, Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            GD.PushError($"[PluginHost] {plugin.PluginName}.{action} 失败: {exception}");
        }
    }
}
