using Godot;
using System;

public partial class AudioVisualizer : Control
{
    private const string CaptureBusName = "SoundpadCapture";
    private const int BandCount = 36;
    private const float MinDb = -60.0f;
    private const double RedrawInterval = 1.0 / 30.0;

    [Export] public int Mode { get; set; }
    [Export] public float CircleSizeMultiplier { get; set; } = 1.5f;
    [Export] public float CircleRotationSpeed { get; set; } = 0.6f;

    private AudioEffectSpectrumAnalyzerInstance _analyzer;
    private readonly float[] _smoothed = new float[BandCount];
    private float _rotation;
    private double _redrawTimer;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }
        EnsureAnalyzer();
        _redrawTimer += delta;
        if (_redrawTimer < RedrawInterval)
        {
            return;
        }
        _redrawTimer = 0.0;
        _rotation += (float)delta * CircleRotationSpeed;
        if (_rotation > MathF.Tau)
        {
            _rotation -= MathF.Tau;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        EnsureAnalyzer();
        if (_analyzer == null || Size.X < 8.0f || Size.Y < 8.0f)
        {
            return;
        }

        if (Mode == 1)
        {
            DrawCircleSpectrum();
        }
        else
        {
            DrawBarSpectrum();
        }
    }

    private void EnsureAnalyzer()
    {
        if (_analyzer != null)
        {
            return;
        }

        int bus = AudioServer.GetBusIndex(CaptureBusName);
        if (bus < 0)
        {
            return;
        }
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); ++i)
        {
            if (AudioServer.GetBusEffectInstance(bus, i) is AudioEffectSpectrumAnalyzerInstance analyzer)
            {
                _analyzer = analyzer;
                return;
            }
        }
    }

    private float SampleBand(int index)
    {
        float t0 = (float)index / BandCount;
        float t1 = (float)(index + 1) / BandCount;
        float from = 20.0f * MathF.Pow(1000.0f, t0);
        float to = 20.0f * MathF.Pow(1000.0f, t1);
        Vector2 magnitude = _analyzer.GetMagnitudeForFrequencyRange(from, to, AudioEffectSpectrumAnalyzerInstance.MagnitudeMode.Average);
        float linear = (magnitude.X + magnitude.Y) * 0.5f;
        float level = Mathf.Clamp((Mathf.LinearToDb(linear) - MinDb) / -MinDb, 0.0f, 1.0f);
        _smoothed[index] = MathF.Max(level, _smoothed[index] * 0.85f);
        return _smoothed[index];
    }

    private void DrawBarSpectrum()
    {
        float gap = 2.0f;
        float width = MathF.Max(2.0f, Size.X / BandCount - gap);
        for (int i = 0; i < BandCount; ++i)
        {
            float level = SampleBand(i);
            float height = MathF.Max(1.0f, level * (Size.Y - 6.0f));
            float x = i * (width + gap);
            Color color = Color.FromHsv(0.52f - level * 0.32f, 0.85f, 0.95f, 0.9f);
            DrawRect(new Rect2(x, Size.Y - height, width, height), color);
        }
    }

    private void DrawCircleSpectrum()
    {
        Vector2 center = Size * 0.5f;
        float radius = MathF.Min(Size.X, Size.Y) * 0.22f * CircleSizeMultiplier;
        DrawArc(center, radius, 0.0f, MathF.Tau, 64, new Color(0.35f, 0.55f, 0.75f, 0.6f), 1.5f, true);
        for (int i = 0; i < BandCount; ++i)
        {
            float level = SampleBand(i);
            float angle = (float)i / BandCount * MathF.Tau - MathF.PI * 0.5f + _rotation;
            Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 from = center + direction * radius;
            Vector2 to = center + direction * (radius + 2.0f + level * radius * 1.4f);
            Color color = Color.FromHsv(0.52f - level * 0.32f, 0.85f, 0.95f, 0.9f);
            DrawLine(from, to, color, 2.5f, true);
        }
    }
}
