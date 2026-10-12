using NAudio.Wave;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// Thread-safe sample provider that applies smooth gain adjustments without clicks or pops.
/// Ramps gain smoothly sample-by-sample toward the target gain and prevents clipping.
/// </summary>
public class SmoothGainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _currentGain;
    private volatile float _targetGain;

    // Ramping rate: ~0.0005 per sample provides a smooth, click-free 10-20ms ramp at 44.1kHz-48kHz
    private const float GainRampStep = 0.0005f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentGain => _currentGain;
    public float TargetGain => _targetGain;

    public SmoothGainSampleProvider(ISampleProvider source, float initialGain = 1.0f)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _currentGain = Math.Clamp(initialGain, 0.0f, 1.0f);
        _targetGain = _currentGain;
    }

    /// <summary>
    /// Updates the target gain in real-time. Thread-safe.
    /// </summary>
    public void SetTargetGain(float targetGain)
    {
        _targetGain = Math.Clamp(targetGain, 0.0f, 1.0f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0) return 0;

        float target = _targetGain;
        float current = _currentGain;

        // Fast path 1: Complete silence (muted)
        if (current == 0.0f && target == 0.0f)
        {
            Array.Clear(buffer, offset, samplesRead);
            return samplesRead;
        }

        // Fast path 2: Unity gain (100% volume, not ramping)
        if (current == 1.0f && target == 1.0f)
        {
            return samplesRead;
        }

        // Smooth sample-by-sample gain application with gain ramp and clipping protection
        for (int i = 0; i < samplesRead; i++)
        {
            if (current < target)
            {
                current = Math.Min(target, current + GainRampStep);
            }
            else if (current > target)
            {
                current = Math.Max(target, current - GainRampStep);
            }

            int index = offset + i;
            float sample = buffer[index] * current;

            // Prevent digital clipping beyond [-1.0, 1.0]
            if (sample > 1.0f) sample = 1.0f;
            else if (sample < -1.0f) sample = -1.0f;

            buffer[index] = sample;
        }

        _currentGain = current;
        return samplesRead;
    }
}
