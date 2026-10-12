using System.Runtime.InteropServices;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// Adapts an ISampleProvider source to match an MMDevice's native MixFormat.
/// Handles sample rate conversion, channel mapping, and bit depth encoding.
/// </summary>
public static class AudioFormatAdapter
{
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00AA00389B71");

    public static IWaveProvider AdaptToTargetFormat(ISampleProvider source, WaveFormat targetFormat)
    {
        ISampleProvider current = source;

        // 1. Resample if sample rates differ
        if (current.WaveFormat.SampleRate != targetFormat.SampleRate)
        {
            current = new WdlResamplingSampleProvider(current, targetFormat.SampleRate);
        }

        // 2. Channel conversion
        if (current.WaveFormat.Channels != targetFormat.Channels)
        {
            current = AdaptChannels(current, targetFormat.Channels);
        }

        // 3. Convert sample stream to byte stream matching the endpoint's exact WaveFormat
        return new FormattedWaveProvider(current, targetFormat);
    }

    private static ISampleProvider AdaptChannels(ISampleProvider source, int targetChannels)
    {
        int sourceChannels = source.WaveFormat.Channels;

        if (sourceChannels == 1 && targetChannels == 2)
        {
            return new MonoToStereoSampleProvider(source);
        }

        if (sourceChannels == 2 && targetChannels == 1)
        {
            return new StereoToMonoSampleProvider(source);
        }

        // Custom multi-channel mapper for > 2 channels (e.g. 5.1 / 7.1) or arbitrary channel counts
        return new MultiChannelAdapterSampleProvider(source, targetChannels);
    }

    private class MultiChannelAdapterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _targetChannels;
        private float[] _sourceBuffer;

        public WaveFormat WaveFormat { get; }

        public MultiChannelAdapterSampleProvider(ISampleProvider source, int targetChannels)
        {
            _source = source;
            _targetChannels = targetChannels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
            _sourceBuffer = new float[4096];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceChannels = _source.WaveFormat.Channels;
            int framesRequested = count / _targetChannels;
            int sourceSamplesNeeded = framesRequested * sourceChannels;

            if (_sourceBuffer.Length < sourceSamplesNeeded)
            {
                Array.Resize(ref _sourceBuffer, sourceSamplesNeeded);
            }

            int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            int framesRead = sourceRead / sourceChannels;

            int writeIdx = offset;
            int readIdx = 0;

            for (int f = 0; f < framesRead; f++)
            {
                float left = _sourceBuffer[readIdx++];
                float right = sourceChannels > 1 ? _sourceBuffer[readIdx++] : left;

                for (int ch = 0; ch < _targetChannels; ch++)
                {
                    buffer[writeIdx++] = (ch % 2 == 0) ? left : right;
                }
            }

            return framesRead * _targetChannels;
        }
    }

    /// <summary>
    /// Bridges an ISampleProvider into an IWaveProvider conforming to the endpoint's native WaveFormat.
    /// Handles 32-bit Float, 16-bit PCM, 24-bit PCM, and 32-bit PCM representations.
    /// </summary>
    private class FormattedWaveProvider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private readonly bool _isFloat;
        private readonly int _bytesPerSample;
        private float[] _sampleBuffer = new float[2048];

        public WaveFormat WaveFormat { get; }

        public FormattedWaveProvider(ISampleProvider source, WaveFormat targetFormat)
        {
            _source = source;
            WaveFormat = targetFormat;

            _bytesPerSample = targetFormat.BitsPerSample / 8;
            if (_bytesPerSample <= 0) _bytesPerSample = 2; // Default to 16-bit if unspecified

            _isFloat = IsFloatFormat(targetFormat);
        }

        private static bool IsFloatFormat(WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                return true;

            if (format is WaveFormatExtensible extensible)
            {
                return extensible.SubFormat == SubtypeIeeeFloat;
            }

            return false;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int samplesRequested = count / _bytesPerSample;
            if (_sampleBuffer.Length < samplesRequested)
            {
                _sampleBuffer = new float[samplesRequested];
            }

            int samplesRead = _source.Read(_sampleBuffer, 0, samplesRequested);
            int writeByteIdx = offset;

            if (_isFloat && _bytesPerSample == 4)
            {
                // IEEE Float (32-bit): zero-allocation bulk copy
                int bytesToCopy = samplesRead * 4;
                Buffer.BlockCopy(_sampleBuffer, 0, buffer, offset, bytesToCopy);
                return bytesToCopy;
            }
            else if (_bytesPerSample == 2)
            {
                // 16-bit PCM
                for (int i = 0; i < samplesRead; i++)
                {
                    float val = Math.Clamp(_sampleBuffer[i], -1.0f, 1.0f);
                    short sample16 = (short)(val * short.MaxValue);
                    buffer[writeByteIdx++] = (byte)(sample16 & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample16 >> 8) & 0xFF);
                }
            }
            else if (_bytesPerSample == 3)
            {
                // 24-bit PCM
                for (int i = 0; i < samplesRead; i++)
                {
                    float val = Math.Clamp(_sampleBuffer[i], -1.0f, 1.0f);
                    int sample24 = (int)(val * 8388607.0f);
                    buffer[writeByteIdx++] = (byte)(sample24 & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample24 >> 8) & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample24 >> 16) & 0xFF);
                }
            }
            else if (_bytesPerSample == 4)
            {
                // 32-bit PCM
                for (int i = 0; i < samplesRead; i++)
                {
                    float val = Math.Clamp(_sampleBuffer[i], -1.0f, 1.0f);
                    int sample32 = (int)(val * int.MaxValue);
                    buffer[writeByteIdx++] = (byte)(sample32 & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample32 >> 8) & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample32 >> 16) & 0xFF);
                    buffer[writeByteIdx++] = (byte)((sample32 >> 24) & 0xFF);
                }
            }

            return samplesRead * _bytesPerSample;
        }
    }
}
