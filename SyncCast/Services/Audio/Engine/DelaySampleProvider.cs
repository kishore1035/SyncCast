using NAudio.Wave;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// High-performance, click-free software audio delay buffer.
/// Implements a bounded circular delay line with smooth fractional delay slewing
/// to prevent audible clicks, pops, or harsh discontinuities when adjusting delays during playback.
/// </summary>
public class DelaySampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _maxDelayFrames;
    private readonly int _bufferFrameCapacity;
    private readonly float[] _ringBuffer;

    private int _writeFrameIndex = 0;
    private double _currentDelayFrames = 0.0;
    private double _targetDelayFrames = 0.0;
    private readonly object _delayLock = new();

    // Slew rate per frame: 0.002 yields ~0.2% subtle pitch shift during delay changes,
    // seamlessly adjusting without clicks or pops.
    private const double MaxSlewPerFrame = 0.002;

    public const double MinDelayMs = 0.0;
    public const double MaxDelayMs = 1000.0;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public double CurrentDelayMs
    {
        get
        {
            lock (_delayLock)
            {
                return _sampleRate > 0 ? (_currentDelayFrames / _sampleRate) * 1000.0 : 0.0;
            }
        }
    }

    public double TargetDelayMs
    {
        get
        {
            lock (_delayLock)
            {
                return _sampleRate > 0 ? (_targetDelayFrames / _sampleRate) * 1000.0 : 0.0;
            }
        }
    }

    public DelaySampleProvider(ISampleProvider source, double initialDelayMs = 0.0, double maxDelayMs = MaxDelayMs)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _sampleRate = Math.Max(8000, source.WaveFormat.SampleRate);

        double effectiveMaxMs = Math.Clamp(maxDelayMs, 100.0, 5000.0);
        _maxDelayFrames = (int)Math.Ceiling((effectiveMaxMs / 1000.0) * _sampleRate);
        // Extra frame headroom for safety and circular interpolation
        _bufferFrameCapacity = _maxDelayFrames + 1024;
        _ringBuffer = new float[_bufferFrameCapacity * _channels];

        double initialClamped = Math.Clamp(initialDelayMs, MinDelayMs, MaxDelayMs);
        double initialFrames = (initialClamped / 1000.0) * _sampleRate;
        _currentDelayFrames = initialFrames;
        _targetDelayFrames = initialFrames;
    }

    /// <summary>
    /// Sets the target delay in milliseconds. Thread-safe and click-free.
    /// If snapImmediately is true (e.g. at initial stream creation), jumps to delay without slewing.
    /// </summary>
    public void SetDelayMs(double delayMs, bool snapImmediately = false)
    {
        double clampedMs = Math.Clamp(delayMs, MinDelayMs, MaxDelayMs);
        lock (_delayLock)
        {
            _targetDelayFrames = (clampedMs / 1000.0) * _sampleRate;
            if (snapImmediately)
            {
                _currentDelayFrames = _targetDelayFrames;
            }
        }
    }

    /// <summary>
    /// Resets the delay line and clears buffered historical samples.
    /// </summary>
    public void Reset()
    {
        lock (_delayLock)
        {
            _targetDelayFrames = 0.0;
            _currentDelayFrames = 0.0;
            _writeFrameIndex = 0;
            Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0) return 0;

        int framesToProcess = samplesRead / _channels;
        if (framesToProcess == 0) return samplesRead;

        lock (_delayLock)
        {
            double targetDelay = _targetDelayFrames;
            double currentDelay = _currentDelayFrames;
            int writeFrame = _writeFrameIndex;
            int capacity = _bufferFrameCapacity;
            int ch = _channels;

            // Fast path: zero delay and not slewing
            if (currentDelay == 0.0 && targetDelay == 0.0)
            {
                // Write incoming samples to ring buffer so history is available if delay is increased later
                for (int f = 0; f < framesToProcess; f++)
                {
                    int ringBase = writeFrame * ch;
                    int srcBase = offset + (f * ch);
                    for (int c = 0; c < ch; c++)
                    {
                        _ringBuffer[ringBase + c] = buffer[srcBase + c];
                    }
                    writeFrame = (writeFrame + 1) % capacity;
                }
                _writeFrameIndex = writeFrame;
                return samplesRead;
            }

            // Delayed path with smooth fractional delay slewing
            for (int f = 0; f < framesToProcess; f++)
            {
                // 1. Store input frame into ring buffer at writeFrame
                int ringBase = writeFrame * ch;
                int srcBase = offset + (f * ch);
                for (int c = 0; c < ch; c++)
                {
                    _ringBuffer[ringBase + c] = buffer[srcBase + c];
                }

                // 2. Slew currentDelay toward targetDelay
                if (currentDelay < targetDelay)
                {
                    currentDelay = Math.Min(targetDelay, currentDelay + MaxSlewPerFrame);
                }
                else if (currentDelay > targetDelay)
                {
                    currentDelay = Math.Max(targetDelay, currentDelay - MaxSlewPerFrame);
                }

                // 3. Compute read frame position (with fractional part for linear interpolation)
                double readPos = writeFrame - currentDelay;
                while (readPos < 0) readPos += capacity;
                while (readPos >= capacity) readPos -= capacity;

                int frame0 = (int)readPos;
                int frame1 = (frame0 + 1) % capacity;
                float frac = (float)(readPos - frame0);

                int readBase0 = frame0 * ch;
                int readBase1 = frame1 * ch;

                // 4. Output interpolated delayed samples to buffer
                for (int c = 0; c < ch; c++)
                {
                    float s0 = _ringBuffer[readBase0 + c];
                    float s1 = _ringBuffer[readBase1 + c];
                    buffer[srcBase + c] = s0 + frac * (s1 - s0);
                }

                writeFrame = (writeFrame + 1) % capacity;
            }

            _writeFrameIndex = writeFrame;
            _currentDelayFrames = currentDelay;
        }

        return samplesRead;
    }
}
