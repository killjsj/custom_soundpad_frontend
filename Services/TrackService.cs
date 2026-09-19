using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class TrackService
{
    private readonly SoundpadContext _context;
    private readonly List<Track> _items = new();
    private Track _current;
    private int _batch;

    public IReadOnlyList<Track> Items => _items;

    public Track Current => _current;

    public int Count => _items.Count;

    public event Action ContentChanged;

    public event Action<Track, bool> CurrentChanged;

    public TrackService(SoundpadContext context)
    {
        _context = context;
    }

    public int IndexOf(Track track) => track == null ? -1 : _items.IndexOf(track);

    public void SetCurrent(Track track, bool continuePlaying)
    {
        if (track == null)
        {
            return;
        }
        _current = track;
        CurrentChanged?.Invoke(track, continuePlaying);
    }

    public Track ImportFiles(IEnumerable<string> paths)
    {
        Track first = null;
        foreach (string path in paths)
        {
            if (!IsAudioFile(path))
            {
                continue;
            }
            AudioStream stream = LoadAudioFile(path);
            if (stream == null)
            {
                _context.Status.Set($"无法加载音频: {path}");
                continue;
            }

            var track = new Track { Name = Path.GetFileName(path), Path = path, Stream = stream };
            _items.Add(track);
            first ??= track;
        }

        if (first != null)
        {
            RaiseContentChanged();
        }
        return first;
    }

    public Track AddLoaded(string path, string name, string hotkey, bool passthrough, bool loop)
    {
        AudioStream stream = LoadAudioFile(path);
        if (stream == null)
        {
            return null;
        }

        var track = new Track
        {
            Name = string.IsNullOrEmpty(name) ? Path.GetFileName(path) : name,
            Path = path,
            Stream = stream,
            Hotkey = hotkey ?? string.Empty,
            HotkeyPassthrough = passthrough,
            Loop = loop,
        };
        ApplyLoop(track);
        _items.Add(track);
        return track;
    }

    public void Remove(Track track)
    {
        if (track == null)
        {
            return;
        }

        bool wasCurrent = track == _current;
        _items.Remove(track);
        if (wasCurrent)
        {
            _current = null;
            CurrentChanged?.Invoke(null, false);
            if (_items.Count > 0)
            {
                SetCurrent(_items[^1], false);
            }
        }
        RaiseContentChanged();
    }

    public void Clear()
    {
        _items.Clear();
        _current = null;
        CurrentChanged?.Invoke(null, false);
        RaiseContentChanged();
    }

    public void NotifyChanged() => RaiseContentChanged();

    public void BeginBatch() => _batch++;

    public void EndBatch()
    {
        if (_batch > 0)
        {
            _batch--;
        }
        if (_batch == 0)
        {
            ContentChanged?.Invoke();
        }
    }

    private void RaiseContentChanged()
    {
        if (_batch == 0)
        {
            ContentChanged?.Invoke();
        }
    }

    public static bool IsAudioFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    }

    public static AudioStream LoadAudioFile(string path)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".wav" => AudioStreamWav.LoadFromFile(path),
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                ".mp3" => AudioStreamMP3.LoadFromFile(path),
                _ => null,
            };
        }
        catch (Exception exception)
        {
            GD.PushWarning($"LoadAudioFile failed: {exception.Message}");
            return null;
        }
    }

    public static void ApplyLoop(Track track)
    {
        switch (track.Stream)
        {
            case AudioStreamWav wav:
                if (track.Loop)
                {
                    int bytesPerFrame = wav.Format switch
                    {
                        AudioStreamWav.FormatEnum.Format8Bits => wav.Stereo ? 2 : 1,
                        AudioStreamWav.FormatEnum.Format16Bits => wav.Stereo ? 4 : 2,
                        _ => 0,
                    };
                    wav.LoopBegin = 0;
                    wav.LoopEnd = bytesPerFrame > 0 && wav.Data != null ? wav.Data.Length / bytesPerFrame : 0;
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                }
                else
                {
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                }
                break;
            case AudioStreamOggVorbis ogg:
                ogg.Loop = track.Loop;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = track.Loop;
                break;
        }
    }
}
