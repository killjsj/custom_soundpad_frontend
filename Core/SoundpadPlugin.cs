using System.Runtime.Versioning;
using Godot;

namespace CustomSoundpad;

[SupportedOSPlatform("windows")]
public abstract partial class SoundpadPlugin : Node
{
    public SoundpadContext Context { get; private set; }

    public virtual string PluginName => GetType().Name;

    internal void Bind(SoundpadContext context)
    {
        Context = context;
    }

    public virtual void OnLoad()
    {
    }

    public virtual void OnReady()
    {
    }

    public virtual void OnProcess(double delta)
    {
    }

    public virtual void OnUnload()
    {
    }

    public virtual bool CanDropData(Vector2 atPosition, Variant data) => false;

    public virtual bool DropData(Vector2 atPosition, Variant data) => false;
}
