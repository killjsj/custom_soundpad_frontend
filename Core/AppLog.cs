using System;
using System.IO;
using Godot;

namespace CustomSoundpad;

public static class AppLog
{
    private static readonly object Sync = new object();
    private static string _path;

    public static void Initialize()
    {
        string directory = ProjectSettings.GlobalizePath("user://logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "frontend.log");
        string previous = Path.Combine(directory, "frontend_prev.log");
        try
        {
            if (File.Exists(_path))
            {
                File.Move(_path, previous, true);
            }
        }
        catch
        {
        }
        Write("frontend started");
    }

    public static void Write(string message)
    {
        string path = _path;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        lock (Sync)
        {
            try
            {
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch
            {
            }
        }
    }
}
