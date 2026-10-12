using System.IO;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using Xunit;

namespace SyncCast.Tests;

/// <summary>
/// Unit tests for Chunk 4:
/// - Smooth gain provider and anti-clipping
/// - Master volume and per-device volume calculations
/// - Independent mute controls
/// - Buffer health and latency reporting
/// - Settings persistence with error handling
/// - Reconnection and backoff lifecycle
/// </summary>
public class VolumeAndHealthMonitoringTests
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
    public void SmoothGainProvider_AppliesSilence_WhenGainIsZero()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var source = new DummySampleProvider(format, new[] { 0.8f, -0.8f, 0.5f, -0.5f });
        var smoothGain = new SmoothGainSampleProvider(source, initialGain: 0.0f);

        var buffer = new float[128];
        int read = smoothGain.Read(buffer, 0, buffer.Length);

        Assert.Equal(128, read);
        Assert.All(buffer, sample => Assert.Equal(0.0f, sample));
    }

    [Fact]
    public void SmoothGainProvider_ClampsSamples_ToPreventClipping()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var source = new DummySampleProvider(format, new[] { 1.5f, -1.5f });
        var smoothGain = new SmoothGainSampleProvider(source, initialGain: 0.999f);

        var buffer = new float[10];
        int read = smoothGain.Read(buffer, 0, buffer.Length);

        Assert.Equal(10, read);
        Assert.All(buffer, sample => Assert.True(sample >= -1.0f && sample <= 1.0f, $"Sample {sample} out of bounds"));
    }

    [Fact]
    public void SmoothGainProvider_SmoothlyRampsGain()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var source = new DummySampleProvider(format, new[] { 1.0f });
        var smoothGain = new SmoothGainSampleProvider(source, initialGain: 0.0f);
        smoothGain.SetTargetGain(1.0f);

        var buffer = new float[100];
        smoothGain.Read(buffer, 0, buffer.Length);

        Assert.True(buffer[0] > 0.0f, "First sample should start ramping up");
        for (int i = 1; i < buffer.Length; i++)
        {
            Assert.True(buffer[i] >= buffer[i - 1], $"Sample at index {i} should be >= previous sample during ramp up");
        }
    }

    [Fact]
    public async Task AudioEngine_CalculatesEffectiveGain_Correctly()
    {
        var log = new LogService();
        var streams = new Dictionary<string, MockEndpointStream>();

        var engine = new MultiOutputAudioEngine(
            log,
            new ToneGeneratorSampleSource(),
            (info, _) =>
            {
                var s = new MockEndpointStream(info);
                streams[info.Id] = s;
                return s;
            });

        var dev1 = new AudioEndpointInfo { Id = "dev-1", FriendlyName = "Speaker 1", State = EndpointActiveState.Active };
        var dev2 = new AudioEndpointInfo { Id = "dev-2", FriendlyName = "Headphones 2", State = EndpointActiveState.Active };

        await engine.StartPlaybackAsync(new[] { dev1, dev2 });

        // Default: 100% master, 100% device => 100% effective
        Assert.Equal(100.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(100.0, streams["dev-2"].EffectiveGainPercent);

        // Set Master to 80%
        engine.SetMasterVolume(80.0, isMuted: false);
        Assert.Equal(80.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(80.0, streams["dev-2"].EffectiveGainPercent);

        // Set Device 1 to 50% -> Effective = 80 * 50 / 100 = 40%
        engine.SetDeviceVolume("dev-1", 50.0, isMuted: false);
        Assert.Equal(40.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(80.0, streams["dev-2"].EffectiveGainPercent); // Device 2 independent

        // Mute Device 1 -> Effective = 0%, Device 2 unaffected
        engine.SetDeviceVolume("dev-1", 50.0, isMuted: true);
        Assert.Equal(0.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(80.0, streams["dev-2"].EffectiveGainPercent);

        // Unmute Device 1 -> Restores 40%
        engine.SetDeviceVolume("dev-1", 50.0, isMuted: false);
        Assert.Equal(40.0, streams["dev-1"].EffectiveGainPercent);

        // Master Mute -> Silences all outputs
        engine.SetMasterVolume(80.0, isMuted: true);
        Assert.Equal(0.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(0.0, streams["dev-2"].EffectiveGainPercent);

        // Master Unmute -> Restores previous levels
        engine.SetMasterVolume(80.0, isMuted: false);
        Assert.Equal(40.0, streams["dev-1"].EffectiveGainPercent);
        Assert.Equal(80.0, streams["dev-2"].EffectiveGainPercent);

        await engine.StopPlaybackAsync();
    }

    [Fact]
    public async Task AudioEngine_DoesNotMultiplyGainTwice()
    {
        var log = new LogService();
        var streams = new Dictionary<string, MockEndpointStream>();

        var engine = new MultiOutputAudioEngine(
            log,
            new ToneGeneratorSampleSource(),
            (info, _) =>
            {
                var s = new MockEndpointStream(info);
                streams[info.Id] = s;
                return s;
            });

        var dev = new AudioEndpointInfo { Id = "dev-test", FriendlyName = "Test Output", State = EndpointActiveState.Active };
        await engine.StartPlaybackAsync(new[] { dev });

        engine.SetMasterVolume(50.0, isMuted: false);
        engine.SetDeviceVolume("dev-test", 50.0, isMuted: false);

        // Expected effective gain is exactly 25.0% (0.5 * 0.5 * 100)
        Assert.Equal(25.0, streams["dev-test"].EffectiveGainPercent);

        // Setting same volume again does not multiply
        engine.SetMasterVolume(50.0, isMuted: false);
        Assert.Equal(25.0, streams["dev-test"].EffectiveGainPercent);

        await engine.StopPlaybackAsync();
    }

    [Fact]
    public void EndpointAudioStream_TracksBufferMetrics_AndDropCount()
    {
        var log = new LogService();
        var dev = new AudioEndpointInfo { Id = "dev-1", FriendlyName = "Speaker" };
        var stream = new EndpointAudioStream(dev, log);

        // Stream initially reports zero bytes/frames and valid capacity
        var status = stream.Status;
        Assert.Equal(0, status.BytesWritten);
        Assert.Equal(0, status.FramesProcessed);
        Assert.Equal(0, status.BufferOverflowDropCount);
        Assert.Equal(0, status.BufferCapacityBytes);
        Assert.Contains("Hardware/Bluetooth dependent", status.AcousticLatencyText);

        stream.Dispose();
    }

    [Fact]
    public async Task SettingsService_SavesAndLoadsSettings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"syncast_settings_test_{Guid.NewGuid():N}.json");
        var log = new LogService();
        var service = new SettingsService(log, tempFile);

        try
        {
            var initialSettings = new SyncCastSettings
            {
                MasterVolumePercent = 75.0,
                IsMasterMuted = true,
                AutoReconnectEnabled = false,
                LastCaptureSourceId = "test-capture-id",
                DeviceSettings =
                {
                    ["device-A"] = new DeviceVolumeSetting
                    {
                        DeviceFriendlyName = "Headphones A",
                        VolumePercent = 60.0,
                        IsMuted = true
                    }
                }
            };

            await service.SaveSettingsAsync(initialSettings);

            var loaded = await service.LoadSettingsAsync();

            Assert.Equal(75.0, loaded.MasterVolumePercent);
            Assert.True(loaded.IsMasterMuted);
            Assert.False(loaded.AutoReconnectEnabled);
            Assert.Equal("test-capture-id", loaded.LastCaptureSourceId);
            Assert.True(loaded.DeviceSettings.ContainsKey("device-A"));
            Assert.Equal(60.0, loaded.DeviceSettings["device-A"].VolumePercent);
            Assert.True(loaded.DeviceSettings["device-A"].IsMuted);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SettingsService_ReturnsDefaults_WhenFileIsCorrupted()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"syncast_corrupted_test_{Guid.NewGuid():N}.json");
        var log = new LogService();
        var service = new SettingsService(log, tempFile);

        try
        {
            await File.WriteAllTextAsync(tempFile, "{ invalid json content !!!");

            var loaded = await service.LoadSettingsAsync();

            Assert.NotNull(loaded);
            Assert.Equal(100.0, loaded.MasterVolumePercent);
            Assert.False(loaded.IsMasterMuted);
            Assert.True(loaded.AutoReconnectEnabled);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AudioEngine_HandlesEndpointDisconnection_AndStopCancelsRetries()
    {
        var log = new LogService();
        var streams = new Dictionary<string, MockEndpointStream>();

        var engine = new MultiOutputAudioEngine(
            log,
            new ToneGeneratorSampleSource(),
            (info, _) =>
            {
                var s = new MockEndpointStream(info);
                streams[info.Id] = s;
                return s;
            });

        var dev1 = new AudioEndpointInfo { Id = "dev-1", FriendlyName = "Speaker 1", State = EndpointActiveState.Active };
        var dev2 = new AudioEndpointInfo { Id = "dev-2", FriendlyName = "Headphones 2", State = EndpointActiveState.Active };

        await engine.StartPlaybackAsync(new[] { dev1, dev2 });

        Assert.Equal(StreamPlaybackState.Playing, streams["dev-1"].State);
        Assert.Equal(StreamPlaybackState.Playing, streams["dev-2"].State);

        // Notify engine that dev-1 disconnected
        engine.NotifyEndpointDisconnected("dev-1", "Device unplugged");

        Assert.Equal(StreamPlaybackState.Disconnected, streams["dev-1"].State);
        // dev-2 should remain Playing!
        Assert.Equal(StreamPlaybackState.Playing, streams["dev-2"].State);

        // Stop engine should cancel everything cleanly
        await engine.StopPlaybackAsync();

        Assert.Equal(StreamPlaybackState.Stopped, streams["dev-2"].State);
    }
}
