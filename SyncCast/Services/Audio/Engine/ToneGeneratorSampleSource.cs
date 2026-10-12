using NAudio.Wave;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// Source of audio samples that can spawn independent stream providers for multiple endpoints.
/// </summary>
public interface ISampleSource
{
    WaveFormat MasterFormat { get; }
    float Volume { get; set; }
    double Frequency { get; set; }
    bool IsMuted { get; set; }

    /// <summary>
    /// Creates a dedicated ISampleProvider instance for an output stream so that
    /// independent WASAPI render threads can pull samples at their own clock rates.
    /// </summary>
    ISampleProvider CreateStreamSampleProvider();
}

/// <summary>
/// Generates a continuous, low-volume test tone (sine wave) for audio diagnostic testing.
/// Default volume is set to a safe, comfortable level (-22 dBFS / 0.08) to prevent hearing strain.
/// </summary>
public class ToneGeneratorSampleSource : ISampleSource
{
    public const int DefaultSampleRate = 48000;
    public const int DefaultChannels = 2; // Stereo
    public const float DefaultVolume = 0.08f; // Safe low-volume level (-22 dBFS)
    public const double DefaultFrequency = 440.0; // Standard concert A reference

    private readonly object _lock = new();
    private float _volume = DefaultVolume;
    private double _frequency = DefaultFrequency;
    private bool _isMuted = false;

    public WaveFormat MasterFormat { get; }

    public float Volume
    {
        get => _volume;
        set
        {
            lock (_lock)
            {
                // Clamp volume between 0.0 and 0.5 (maximum cap at -6 dBFS for safety)
                _volume = Math.Clamp(value, 0.0f, 0.5f);
            }
        }
    }

    public double Frequency
    {
        get => _frequency;
        set
        {
            lock (_lock)
            {
                _frequency = Math.Clamp(value, 40.0, 4000.0);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            lock (_lock)
            {
                _isMuted = value;
            }
        }
    }

    public ToneGeneratorSampleSource(int sampleRate = DefaultSampleRate, int channels = DefaultChannels)
    {
        MasterFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public ISampleProvider CreateStreamSampleProvider()
    {
        return new ToneStreamSampleProvider(this);
    }

    private class ToneStreamSampleProvider : ISampleProvider
    {
        private readonly ToneGeneratorSampleSource _source;
        private double _phase = 0.0;

        public WaveFormat WaveFormat => _source.MasterFormat;

        public ToneStreamSampleProvider(ToneGeneratorSampleSource source)
        {
            _source = source;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_source.IsMuted)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            float currentVolume = _source.Volume;
            double currentFrequency = _source.Frequency;
            int channels = WaveFormat.Channels;
            double sampleRate = WaveFormat.SampleRate;
            double phaseIncrement = (2.0 * Math.PI * currentFrequency) / sampleRate;

            int sampleCount = count / channels;
            int writeIndex = offset;

            for (int i = 0; i < sampleCount; i++)
            {
                float sampleVal = (float)(Math.Sin(_phase) * currentVolume);
                _phase += phaseIncrement;
                if (_phase >= 2.0 * Math.PI)
                {
                    _phase -= 2.0 * Math.PI;
                }

                // Write to all channels for stereo / multi-channel imaging
                for (int ch = 0; ch < channels; ch++)
                {
                    buffer[writeIndex++] = sampleVal;
                }
            }

            return count;
        }
    }
}
