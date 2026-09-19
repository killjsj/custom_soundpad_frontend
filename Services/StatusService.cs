using System;
using System.Runtime.Versioning;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public sealed class StatusService
{
    private readonly SoundpadContext _context;

    public string Message { get; private set; } = "就绪";

    public event Action Changed;

    public StatusService(SoundpadContext context)
    {
        _context = context;
    }

    public void Set(string message)
    {
        if (_context.Exiting)
        {
            return;
        }
        Message = message;
        AppLog.Write(message);
        Changed?.Invoke();
    }
}
