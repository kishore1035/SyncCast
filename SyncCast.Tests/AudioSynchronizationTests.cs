using System.IO;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using Xunit;

namespace SyncCast.Tests;

/// <summary>
/// Unit tests for Chunk 5:
/// - Shared audio timeline & monotonic sequence numbering
/// - Bounded delay sample buffer with click-free slewing
/// - Calibration pulse generator (Hann envelope, safe peak level, periodicity)
/// - Per-device delay configuration, range clamping (0–1000 ms), and resets
/// - Timing reference designation and removal
/// - Multi-output synchronization independence and failure isolation
/// - Settings persistence for delay compensation and reference device
/// </summary>
public class AudioSynchronizationTests
{
    private class DummySampleProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; }
        private readonly float[] _pattern;
        private int _position;

        public DummySampleProvider(WaveFormat format, float[] pattern)
        {
            WaveFormat = format;
            _pattern = pattern;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesWritten = 0;
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _pattern[_position % _pattern.Length];
                _position++;
                samplesWritten++;
            }
            return samplesWritten;
        }
    }

    [Fact]
    public void SharedAudioTimeline_MonotonicSequencing_IncrementsCorrectly()
    {
        var timeline = new SharedAudioTimeline();

        Assert.Equal(0, timeline.TotalSourceFrames);
        Assert.Equal(0, timeline.CurrentSequenceNumber);

        var (seq1, pos1) = timeline.AdvanceSourceBlock(480);
        Assert.Equal(1, seq1);
        Assert.Equal(480, pos1);
        Assert.Equal(480, timeline.TotalSourceFrames);
        Assert.Equal(1, timeline.CurrentSequenceNumber);

        var (seq2, pos2) = timeline.AdvanceSourceBlock(480);
        Assert.Equal(2, seq2);
        Assert.Equal(960, pos2);
        Assert.Equal(960, timeline.TotalSourceFrames);
        Assert.Equal(2, timeline.CurrentSequenceNumber);
    }

    [Fact]
    public void SharedAudioTimeline_EndpointTracking_UpdatesDriftAndSequence()
    {
        var timeline = new SharedAudioTimeline();
        timeline.RegisterDestination("dev-1", configuredDelayMs: 0.0);

        timeline.SetReferenceEndpoint("dev-1");
        Assert.Equal("dev-1", timeline.ReferenceEndpointId);

        timeline.AdvanceSourceBlock(480);
        timeline.RecordBlockEnqueued("dev-1", sequenceNumber: 1, frameCount: 480, sampleRate: 48000);

        var status = timeline.GetDestinationStatus("dev-1");
        Assert.NotNull(status);
        Assert.Equal("dev-1", status.EndpointId);
        Assert.Equal(1, status.LastBlockSequence);
        Assert.Equal(480, status.FramesEnqueued);
        Assert.Equal(0.0, status.EstimatedDriftMs);
    }

    [Fact]
    public void DelaySampleProvider_ZeroDelay_PassesAudioUnmodified()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var pattern = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };
        var source = new DummySampleProvider(format, pattern);
        var delayProvider = new DelaySampleProvider(source, initialDelayMs: 0.0, maxDelayMs: 1000);

        var buffer = new float[8];
        int read = delayProvider.Read(buffer, 0, buffer.Length);

        Assert.Equal(8, read);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(pattern[i % pattern.Length], buffer[i], precision: 4);
        }
    }

    [Fact]
    public void DelaySampleProvider_ConfiguredDelay_DelaysSamplesCorrectly()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1); // 1 channel
        var pattern = new float[100];
        pattern[0] = 1.0f;

        var source = new DummySampleProvider(format, pattern);
        // Delay by 10 ms at 44100 Hz = 441 samples
        var delayProvider = new DelaySampleProvider(source, initialDelayMs: 10.0, maxDelayMs: 1000);

        Assert.Equal(10.0, delayProvider.TargetDelayMs);

        // Read first 441 samples: should be initial buffer silence
        var initialSilence = new float[441];
        int readSilence = delayProvider.Read(initialSilence, 0, initialSilence.Length);
        Assert.Equal(441, readSilence);
        Assert.All(initialSilence, s => Assert.Equal(0.0f, s));

        // Next sample read should contain our impulse
        var impulseBuffer = new float[10];
        int readImpulse = delayProvider.Read(impulseBuffer, 0, impulseBuffer.Length);
        Assert.Equal(10, readImpulse);
        Assert.True(impulseBuffer[0] > 0.8f);
    }

    [Fact]
    public void DelaySampleProvider_ClampsInvalidDelaysToSupportedRange()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var source = new DummySampleProvider(format, new float[] { 0.1f });
        var delayProvider = new DelaySampleProvider(source, initialDelayMs: -50.0, maxDelayMs: 1000);

        // Negative should be clamped to 0
        Assert.Equal(0.0, delayProvider.TargetDelayMs);

        // Values over maxDelayMs (1000) should be clamped to 1000
        delayProvider.SetDelayMs(1500.0);
        Assert.Equal(1000.0, delayProvider.TargetDelayMs);

        // Reset to 0
        delayProvider.SetDelayMs(0.0);
        Assert.Equal(0.0, delayProvider.TargetDelayMs);
    }

    [Fact]
    public void CalibrationPulseSampleSource_GeneratesGentleHannWindowedTone()
    {
        var pulseSource = new CalibrationPulseSampleSource(sampleRate: 44100, channels: 2)
        {
            Frequency = 880.0,
            PulseDurationSec = 0.040,
            PulseIntervalSec = 0.800,
            Volume = 0.10f // -20 dBFS safe volume
        };

        var provider = pulseSource.CreateStreamSampleProvider();
        Assert.Equal(44100, provider.WaveFormat.SampleRate);
        Assert.Equal(2, provider.WaveFormat.Channels);

        // Read 1000 stereo samples (2000 floats)
        var buffer = new float[2000];
        int read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(2000, read);

        // Peak magnitude should never exceed safe peak volume (0.10f)
        float maxMag = 0.0f;
        for (int i = 0; i < read; i++)
        {
            float mag = Math.Abs(buffer[i]);
            if (mag > maxMag) maxMag = mag;
        }

        Assert.True(maxMag <= 0.1001f);
        Assert.True(maxMag > 0.05f); // Ensure pulse actually generated sound
    }

    [Fact]
    public void CalibrationPulseSampleSource_GeneratesSilenceBetweenIntervals()
    {
        var pulseSource = new CalibrationPulseSampleSource(sampleRate: 44100, channels: 2)
        {
            Frequency = 880.0,
            PulseDurationSec = 0.040, // 40 ms pulse
            PulseIntervalSec = 0.800, // 800 ms interval
            Volume = 0.10f
        };

        var provider = pulseSource.CreateStreamSampleProvider();

        // Skip first 100 ms (pulse duration is 40ms, so 100ms is well inside silent region)
        int skipFloats = (int)(44100 * 0.100 * 2);
        var skipBuf = new float[skipFloats];
        provider.Read(skipBuf, 0, skipFloats);

        // Now read 200 ms of audio (all inside the silent interval between pulses)
        int testFloats = (int)(44100 * 0.200 * 2);
        var testBuf = new float[testFloats];
        int read = provider.Read(testBuf, 0, testFloats);

        Assert.Equal(testFloats, read);
        Assert.All(testBuf, s => Assert.Equal(0.0f, s));
    }

    [Fact]
    public void EndpointAudioStream_SetSyncDelay_UpdatesStatusSafely()
    {
        var log = new LogService();
        var info = new AudioEndpointInfo
        {
            Id = "dev-1",
            FriendlyName = "Bluetooth Earbuds",
            Transport = DeviceTransportType.Bluetooth,
            State = EndpointActiveState.Active
        };
        var stream = new EndpointAudioStream(info, log);

        Assert.Equal(0.0, stream.SyncDelayMs);
        Assert.False(stream.IsSyncReference);

        stream.SetSyncDelay(50.0);
        Assert.Equal(50.0, stream.SyncDelayMs);
        Assert.Equal(50.0, stream.Status.SyncDelayMs);

        stream.SetSyncDelay(0.0);
        Assert.Equal(0.0, stream.SyncDelayMs);
    }

    [Fact]
    public void MultiOutputAudioEngine_SetReferenceAndDelay_ConfiguresActiveEndpoints()
    {
        var log = new LogService();
        var engine = new MultiOutputAudioEngine(log);

        engine.SetReferenceEndpoint("dev-ref");
        Assert.Equal("dev-ref", engine.ReferenceEndpointId);

        engine.SetDeviceSyncDelay("dev-fast", 35.0);
        Assert.Equal(35.0, engine.GetDeviceSyncDelay("dev-fast"));

        // Reset all delays
        engine.ResetAllSyncDelays();
        Assert.Equal(0.0, engine.GetDeviceSyncDelay("dev-fast"));
    }

    [Fact]
    public async Task SettingsService_SavesAndLoads_Chunk5SyncPreferences()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"synccast_test_{Guid.NewGuid():N}.json");
        try
        {
            var log = new LogService();
            var settingsService = new SettingsService(log, tempFile);

            var original = new SyncCastSettings
            {
                MasterVolumePercent = 85.0,
                IsMasterMuted = false,
                AutoReconnectEnabled = true,
                ReferenceEndpointId = "bt-headphones-1",
                DriftMonitoringEnabled = true
            };
            original.DeviceSettings["bt-headphones-2"] = new DeviceVolumeSetting
            {
                DeviceFriendlyName = "Headphones 2",
                VolumePercent = 80.0,
                SyncDelayMs = 45.0
            };
            original.DeviceSettings["bt-headphones-3"] = new DeviceVolumeSetting
            {
                DeviceFriendlyName = "Headphones 3",
                VolumePercent = 100.0,
                SyncDelayMs = 120.0
            };

            await settingsService.SaveSettingsAsync(original);

            // Load back
            var loaded = await settingsService.LoadSettingsAsync();

            Assert.Equal("bt-headphones-1", loaded.ReferenceEndpointId);
            Assert.True(loaded.DriftMonitoringEnabled);
            Assert.Equal(45.0, loaded.DeviceSettings["bt-headphones-2"].SyncDelayMs);
            Assert.Equal(120.0, loaded.DeviceSettings["bt-headphones-3"].SyncDelayMs);
            Assert.Equal(80.0, loaded.DeviceSettings["bt-headphones-2"].VolumePercent);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task SettingsService_SanitizesInvalidDelaysOnLoad()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"synccast_test_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            string corruptJson = @"{
                ""MasterVolumePercent"": 150.0,
                ""ReferenceEndpointId"": """",
                ""DeviceSettings"": {
                    ""dev-neg"": { ""VolumePercent"": 50.0, ""SyncDelayMs"": -100.0 },
                    ""dev-huge"": { ""VolumePercent"": 50.0, ""SyncDelayMs"": 9999.0 },
                    ""dev-valid"": { ""VolumePercent"": 50.0, ""SyncDelayMs"": 25.0 }
                }
            }";
            File.WriteAllText(tempFile, corruptJson);

            var log = new LogService();
            var settingsService = new SettingsService(log, tempFile);
            var loaded = await settingsService.LoadSettingsAsync();

            // Clamping checks
            Assert.Equal(100.0, loaded.MasterVolumePercent);
            Assert.Equal(0.0, loaded.DeviceSettings["dev-neg"].SyncDelayMs);
            Assert.Equal(1000.0, loaded.DeviceSettings["dev-huge"].SyncDelayMs);
            Assert.Equal(25.0, loaded.DeviceSettings["dev-valid"].SyncDelayMs);
            Assert.Null(loaded.ReferenceEndpointId);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void DelaySampleProvider_LiveDelayAdjustment_SlewsWithoutCrashing()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var source = new DummySampleProvider(format, new float[] { 0.2f, -0.2f, 0.4f, -0.4f });
        var delayProvider = new DelaySampleProvider(source, initialDelayMs: 0.0, maxDelayMs: 1000);

        var buf = new float[1024];

        // Read at 0ms delay
        int read1 = delayProvider.Read(buf, 0, buf.Length);
        Assert.Equal(1024, read1);

        // Adjust delay live to 50ms
        delayProvider.SetDelayMs(50.0);
        Assert.Equal(50.0, delayProvider.TargetDelayMs);

        // Read while slewing
        for (int i = 0; i < 10; i++)
        {
            int readSlew = delayProvider.Read(buf, 0, buf.Length);
            Assert.Equal(1024, readSlew);
            // Verify no NaN or infinite samples
            Assert.All(buf, s => Assert.False(float.IsNaN(s) || float.IsInfinity(s)));
        }

        // Adjust delay live to 0ms
        delayProvider.SetDelayMs(0.0);
        Assert.Equal(0.0, delayProvider.TargetDelayMs);

        int readReset = delayProvider.Read(buf, 0, buf.Length);
        Assert.Equal(1024, readReset);
        Assert.All(buf, s => Assert.False(float.IsNaN(s) || float.IsInfinity(s)));
    }
}
