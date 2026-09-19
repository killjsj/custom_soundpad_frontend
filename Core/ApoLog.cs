using System;
using System.IO;

namespace CustomSoundpad;

public static class ApoLog
{
    public const string DirectoryPath = @"C:\ProgramData\InjectAudio";
    public const string LogName = "InjectAudio.log";
    public const string PreviousName = "InjectAudio_prev.log";

    public static void Rotate()
    {
        try
        {
            string current = Path.Combine(DirectoryPath, LogName);
            if (!File.Exists(current))
            {
                return;
            }
            string previous = Path.Combine(DirectoryPath, PreviousName);
            File.Move(current, previous, true);
        }
        catch (Exception exception)
        {
            AppLog.Write($"rotate {LogName} failed: {exception.Message}");
        }
    }
}
