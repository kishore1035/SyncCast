using System.Diagnostics;
using NAudio.Wave;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// Generates quiet, gentle Hann-windowed acoustic calibration pulses for manual delay alignment.
/// Emits a brief 40ms 880 Hz ping every 800ms with smooth raised-cosine envelope ramping.
/// Avoids harsh transients or loud bursts, allowing human ears to discern milliseconds of timing differences.
/// </summary>
public class CalibrationPulseSampleSource : ISampleSource
{
    public const int DefaultSampleRate = 48000;
    public const int DefaultChannels = 2; // Stereo
    public const float DefaultVolume = 0.10f; // Safe low-volume level (-20 dBFS)
    public const double DefaultFrequency = 880.0; // Clear musical pitch (A5)
    public const double DefaultPulseDurationSec = 0.040; // 40ms gentle pulse
    public const double DefaultPulseIntervalSec = 0.800; // 800ms metronome interval

    private readonly object _lock = new();
    private float _volume = DefaultVolume;
    private double _frequency = DefaultFrequency;
    private bool _isMuted = false;
    private long _epochTimestamp = Stopwatch.GetTimestamp();

    public WaveFormat MasterFormat { get; }
    public double PulseDurationSec { get; set; } = DefaultPulseDurationSec;
    public double PulseIntervalSec { get; set; } = DefaultPulseIntervalSec;

    public float Volume
    {
        get => _volume;
        set
        {
            lock (_lock)
            {
                _volume = Math.Clamp(value, 0.0f, 0.35f);
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
                _frequency = Math.Clamp(value, 200.0, 2000.0);
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

    public CalibrationPulseSampleSource(int sampleRate = DefaultSampleRate, int channels = DefaultChannels)
    {
        MasterFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        ResetEpoch();
    }

    public void ResetEpoch()
    {
        lock (_lock)
        {
            _epochTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public ISampleProvider CreateStreamSampleProvider()
    {
        return new CalibrationPulseStreamSampleProvider(this);
    }

    private class CalibrationPulseStreamSampleProvider : ISampleProvider
    {
        private readonly CalibrationPulseSampleSource _source;
        private long _renderedFrames = 0;

        public WaveFormat WaveFormat => _source.MasterFormat;

        public CalibrationPulseStreamSampleProvider(CalibrationPulseSampleSource source)
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

            int channels = WaveFormat.Channels;
            int sampleRate = WaveFormat.SampleRate;
            int frameCount = count / channels;

            float volume;
            double freq;
            double pulseDurSec;
            double intervalSec;

            lock (_source._lock)
            {
                volume = _source._volume;
                freq = _source._frequency;
                pulseDurSec = _source.PulseDurationSec;
                intervalSec = _source.PulseIntervalSec;
            }

            int intervalFrames = Math.Max(1, (int)(intervalSec * sampleRate));
            int pulseFrames = Math.Max(1, (int)(pulseDurSec * sampleRate));
            double twoPi = 2.0 * Math.PI;

            int writeIdx = offset;
            for (int f = 0; f < frameCount; f++)
            {
                long currentFrame = _renderedFrames++;
                long posInInterval = currentFrame % intervalFrames;

                float sampleVal = 0.0f;
                if (posInInterval < pulseFrames)
                {
                    // Hann window (raised cosine): 0.5 * (1 - cos(2*pi*pos / pulseFrames))
                    double window = 0.5 * (1.0 - Math.Cos(twoPi * posInInterval / pulseFrames));
                    double sinWave = Math.Sin(twoPi * freq * ((double)posInInterval / sampleRate));
                    sampleVal = (float)(sinWave * window * volume);
                }

                for (int c = 0; c < channels; c++)
                {
                    buffer[writeIdx++] = sampleVal;
                }
            }

            return count;
        }
    }
}
