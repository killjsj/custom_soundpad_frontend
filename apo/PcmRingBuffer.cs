using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CustomSoundpad.Apo;


internal static unsafe class PcmRingBuffer
{
    public const uint Magic = 0x50434D52u;
    public const uint Version = 4u;
    public const uint SampleRate = 48000u;
    public const uint MaxChannels = 8u;
    public const uint CapacityFrames = 48000u;
    public const string MappingName = @"Global\InjectAudioPcmRingBuffer";

    public const uint FlagFilterBlockVoice = 0x00000001u;
    public const uint FlagDisableInject = 0x00000002u;

    [StructLayout(LayoutKind.Sequential)]
    public struct Shared
    {
        public uint magic;
        public uint version;
        public uint sampleRate;
        public uint channels;
        public uint capacityFrames;

        public uint flags;
        public float micAmpInDb;
        public uint logLevel;

        public int writeFrame;
        public int readFrame;

        public float musicVolume;
        public uint reserved0;

        public fixed float samples[(int)(CapacityFrames * MaxChannels)];
    }

    public static long Size => sizeof(Shared);

    public static bool IsValid(Shared* ring)
    {
        return ring != null &&
            ring->magic == Magic &&
            ring->version == Version &&
            ring->sampleRate == SampleRate &&
            ring->channels >= 1 &&
            ring->channels <= MaxChannels &&
            ring->capacityFrames == CapacityFrames;
    }

    public static void Initialize(Shared* ring, uint channels)
    {
        if (ring == null || channels == 0 || channels > MaxChannels)
        {
            return;
        }

        new Span<byte>((void*)ring, (int)Size).Clear();
        ring->magic = Magic;
        ring->version = Version;
        ring->sampleRate = SampleRate;
        ring->channels = channels;
        ring->capacityFrames = CapacityFrames;
        ring->flags = 0;
        ring->micAmpInDb = 0.0f;
        ring->logLevel = 0;
        ring->musicVolume = 1.0f;
        ring->reserved0 = 0;
        Volatile.Write(ref ring->writeFrame, 0);
        Volatile.Write(ref ring->readFrame, 0);
    }

    public static void SetConfig(Shared* ring, uint flags, float micAmpInDb, uint logLevel)
    {
        if (!IsValid(ring))
        {
            return;
        }
        ring->flags = flags;
        ring->micAmpInDb = micAmpInDb;
        ring->logLevel = logLevel;
    }

    public static void SetMusicVolume(Shared* ring, float volume)
    {
        if (!IsValid(ring))
        {
            return;
        }
        ring->musicVolume = Math.Clamp(volume, 0.0f, 1.0f);
    }

    public static bool WriteFrame(Shared* ring, float* frame, uint inputChannels)
    {
        if (!IsValid(ring) || frame == null || inputChannels != ring->channels)
        {
            return false;
        }

        int* writePointer = &ring->writeFrame;
        int write = Volatile.Read(ref *writePointer);
        uint ringFrame = (uint)write % ring->capacityFrames;
        float* destination = ring->samples + (int)(ringFrame * ring->channels);
        for (uint channel = 0; channel < ring->channels; ++channel)
        {
            destination[channel] = frame[channel];
        }

        Interlocked.MemoryBarrier();
        Volatile.Write(ref *writePointer, write + 1);
        return true;
    }

    
    public static bool WriteFramesStereo(Shared* ring, ReadOnlySpan<float> interleaved)
    {
        if (!IsValid(ring) || ring->channels != 2 || interleaved.Length < 2)
        {
            return false;
        }

        int* writePointer = &ring->writeFrame;
        int write = Volatile.Read(ref *writePointer);
        int frameCount = interleaved.Length / 2;
        for (int i = 0; i < frameCount; ++i)
        {
            uint ringFrame = (uint)(write + i) % ring->capacityFrames;
            float* destination = ring->samples + (int)(ringFrame * 2);
            destination[0] = interleaved[i * 2];
            destination[1] = interleaved[i * 2 + 1];
        }

        Interlocked.MemoryBarrier();
        Volatile.Write(ref *writePointer, write + frameCount);
        return true;
    }
}
