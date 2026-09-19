using System;
using System.IO;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class PlaylistService
{
    public const string UserPlaylistPath = "user://playlist.json";

    private const double AutoSaveDelay = 1.0;

    private readonly SoundpadContext _context;
    private string _currentPath = string.Empty;
    private bool _dirty;
    private bool _loading;
    private double _autoSaveTimer = -1.0;

    public bool AutoSave { get; set; } = true;

    public string ConfiguredDefaultPath { get; set; } = string.Empty;

    public event Action Changed;

    public PlaylistService(SoundpadContext context)
    {
        _context = context;
    }

    public string DefaultPath => string.IsNullOrWhiteSpace(ConfiguredDefaultPath) ? UserPlaylistPath : ConfiguredDefaultPath;

    public string CurrentPath => _currentPath;

    public bool IsDirty => _dirty;

    public string DisplayName => string.IsNullOrEmpty(_currentPath) ? "(默认)" : Path.GetFileName(_currentPath);

    public string Tooltip => string.IsNullOrEmpty(_currentPath)
        ? $"未加载歌单文件 (默认: {DefaultPath})"
        : ProjectSettings.GlobalizePath(_currentPath);

    public void Initialize()
    {
        _context.Tracks.ContentChanged += MarkDirty;
        _context.Hotkeys.BindingsChanged += MarkDirty;
    }

    public void Shutdown()
    {
        _context.Tracks.ContentChanged -= MarkDirty;
        _context.Hotkeys.BindingsChanged -= MarkDirty;
        Flush();
    }

    public void Process(double delta)
    {
        if (!AutoSave || !_dirty || _autoSaveTimer < 0.0)
        {
            return;
        }
        _autoSaveTimer -= delta;
        if (_autoSaveTimer > 0.0)
        {
            return;
        }
        _autoSaveTimer = -1.0;
        SaveToCurrent();
    }

    public void MarkDirty()
    {
        if (_loading)
        {
            return;
        }
        _dirty = true;
        _autoSaveTimer = AutoSave ? AutoSaveDelay : -1.0;
        Changed?.Invoke();
    }

    public void Flush()
    {
        if (_dirty)
        {
            SaveToCurrent();
        }
    }

    public void LoadDefaultIfExists()
    {
        string path = DefaultPath;
        if (!string.IsNullOrEmpty(path) && Godot.FileAccess.FileExists(path))
        {
            Load(path, false);
            return;
        }
        Changed?.Invoke();
    }

    public void Save(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            path = DefaultPath;
        }

        var tracks = new Godot.Collections.Array();
        foreach (Track track in _context.Tracks.Items)
        {
            if (track.Path == "(built-in)")
            {
                continue;
            }
            tracks.Add(new Godot.Collections.Dictionary
            {
                { "path", track.Path },
                { "name", track.Name },
                { "hotkey", track.Hotkey },
                { "passthrough", track.HotkeyPassthrough },
                { "loop", track.Loop },
            });
        }

        var root = new Godot.Collections.Dictionary
        {
            { "tracks", tracks },
            {
                "hotkeys", new Godot.Collections.Dictionary
                {
                    { "play", BindingToDictionary(_context.Hotkeys.Play) },
                    { "pause", BindingToDictionary(_context.Hotkeys.Pause) },
                    { "prev", BindingToDictionary(_context.Hotkeys.Prev) },
                    { "next", BindingToDictionary(_context.Hotkeys.Next) },
                }
            },
        };

        using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            _autoSaveTimer = -1.0;
            _context.Status.Set($"保存歌单失败: {Godot.FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(Json.Stringify(root, "  "));
        _currentPath = path;
        _dirty = false;
        _autoSaveTimer = -1.0;
        _context.Status.Set($"歌单已保存: {path}");
        Changed?.Invoke();
    }

    public void Load(string path, bool append)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            _context.Status.Set($"歌单不存在: {path}");
            return;
        }

        using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            _context.Status.Set($"读取歌单失败: {Godot.FileAccess.GetOpenError()}");
            return;
        }

        Variant parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            _context.Status.Set("歌单格式错误");
            return;
        }
        Godot.Collections.Dictionary root = parsed.AsGodotDictionary();

        Track firstAdded = null;
        _context.Tracks.BeginBatch();
        _loading = true;
        try
        {
            if (!append)
            {
                _context.Tracks.Clear();
            }

            if (root.ContainsKey("tracks"))
            {
                foreach (Variant item in root["tracks"].AsGodotArray())
                {
                    if (item.VariantType != Variant.Type.Dictionary)
                    {
                        continue;
                    }
                    Godot.Collections.Dictionary entry = item.AsGodotDictionary();
                    string trackPath = entry.ContainsKey("path") ? entry["path"].AsString() : string.Empty;
                    if (string.IsNullOrEmpty(trackPath))
                    {
                        continue;
                    }
                    string name = entry.ContainsKey("name") ? entry["name"].AsString() : null;
                    string hotkey = entry.ContainsKey("hotkey") ? entry["hotkey"].AsString() : string.Empty;
                    bool passthrough = entry.ContainsKey("passthrough") && entry["passthrough"].AsBool();
                    bool loop = entry.ContainsKey("loop") && entry["loop"].AsBool();
                    Track track = _context.Tracks.AddLoaded(trackPath, name, hotkey, passthrough, loop);
                    if (track == null)
                    {
                        continue;
                    }
                    firstAdded ??= track;
                }
            }

            if (root.ContainsKey("hotkeys") && root["hotkeys"].VariantType == Variant.Type.Dictionary)
            {
                Godot.Collections.Dictionary hotkeys = root["hotkeys"].AsGodotDictionary();
                ReadBinding(hotkeys, "play", _context.Hotkeys.Play);
                ReadBinding(hotkeys, "pause", _context.Hotkeys.Pause);
                ReadBinding(hotkeys, "prev", _context.Hotkeys.Prev);
                ReadBinding(hotkeys, "next", _context.Hotkeys.Next);
                _context.Hotkeys.ApplyGlobalBindings();
            }

            _currentPath = path;
        }
        finally
        {
            _context.Tracks.EndBatch();
            _loading = false;
        }

        _dirty = false;
        _autoSaveTimer = -1.0;
        Changed?.Invoke();

        if (firstAdded != null)
        {
            _context.Playback.Select(firstAdded, _context.Playback.IsPlaying);
        }
        _context.Status.Set(append ? $"已混合歌单: 共 {_context.Tracks.Count} 首" : $"歌单已读取: {_context.Tracks.Count} 首");
    }

    public void RestoreDefault()
    {
        string path = DefaultPath;
        if (!string.IsNullOrEmpty(path) && Godot.FileAccess.FileExists(path))
        {
            Load(path, false);
            return;
        }

        _context.Tracks.Clear();
        _currentPath = path;
        _dirty = true;
        _autoSaveTimer = AutoSave ? AutoSaveDelay : -1.0;
        Changed?.Invoke();
        _context.Status.Set("已恢复默认歌单 (空)");
    }

    private void SaveToCurrent()
    {
        Save(string.IsNullOrEmpty(_currentPath) ? DefaultPath : _currentPath);
    }

    private static Godot.Collections.Dictionary BindingToDictionary(HotkeyBinding binding)
    {
        return new Godot.Collections.Dictionary
        {
            { "combo", binding.Combo },
            { "passthrough", binding.Passthrough },
        };
    }

    private static void ReadBinding(Godot.Collections.Dictionary source, string key, HotkeyBinding binding)
    {
        if (!source.ContainsKey(key) || source[key].VariantType != Variant.Type.Dictionary)
        {
            return;
        }
        Godot.Collections.Dictionary entry = source[key].AsGodotDictionary();
        binding.Combo = entry.ContainsKey("combo") ? entry["combo"].AsString() : string.Empty;
        binding.Passthrough = entry.ContainsKey("passthrough") && entry["passthrough"].AsBool();
    }
}
