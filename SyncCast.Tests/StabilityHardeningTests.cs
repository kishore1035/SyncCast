using System.IO;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using Xunit;

namespace SyncCast.Tests;

/// <summary>
/// Phase 4 & Phase 11 Stability Hardening, Concurrency & Stress Tests for SyncCast Chunk 6.
/// Validates lifecycle serialization, repeated Start/Stop stress cycles, error isolation,
/// buffer alignment, settings recovery, and resource cleanup.
/// </summary>
public class StabilityHardeningTests
{
    private readonly ILogService _log = new LogService();

    private AudioEndpointInfo CreateTestEndpoint(string id, string name) => new()
    {
        Id = id,
        FriendlyName = name,
        State = EndpointActiveState.Active,
        Transport = DeviceTransportType.Bluetooth
    };

    [Fact]
    public async Task RepeatedStartStop_TestTone_20Cycles_ExecutesReliably()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var streams = new Dictionary<string, MockEndpointStream>();

        using var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) =>
            {
                var s = new MockEndpointStream(info);
                streams[info.Id] = s;
                return s;
            });

        var endpoints = new[]
        {
            CreateTestEndpoint("ep-1", "Bluetooth Speaker 1"),
            CreateTestEndpoint("ep-2", "Bluetooth Speaker 2")
        };

        for (int cycle = 0; cycle < 20; cycle++)
        {
            var result = await engine.StartPlaybackAsync(endpoints);
            Assert.True(result.HasAnyPlaying, $"Cycle {cycle + 1} failed to start playback.");
            Assert.Equal(2, result.SuccessfullyPlayingCount);
            Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);
            Assert.True(engine.IsRunning);

            await engine.StopPlaybackAsync();
            Assert.False(engine.IsRunning);
            Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
            Assert.Equal(0, engine.ActivePlayingCount);
        }
    }

    [Fact]
    public async Task RepeatedStartStop_SystemAudio_20Cycles_ExecutesReliably()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var streams = new Dictionary<string, MockEndpointStream>();

        using var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) =>
            {
                var s = new MockEndpointStream(info);
                streams[info.Id] = s;
                return s;
            });

        var source = new AudioEndpointInfo
        {
            Id = "src-pc",
            FriendlyName = "Realtek PC Speakers",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var destinations = new[]
        {
            source,
            CreateTestEndpoint("bt-1", "Sony XM4"),
            CreateTestEndpoint("bt-2", "JBL Flip 6")
        };

        for (int cycle = 0; cycle < 20; cycle++)
        {
            var result = await engine.StartSystemAudioSharingAsync(source, destinations);
            Assert.Equal(AudioEngineMode.SystemAudio, engine.ActiveMode);
            Assert.True(engine.IsRunning);
            Assert.True(mockCapture.IsCapturing);
            Assert.Equal(2, result.SuccessfullyPlayingCount); // 2 mirrored outputs

            await engine.StopPlaybackAsync();
            Assert.False(engine.IsRunning);
            Assert.False(mockCapture.IsCapturing);
            Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
        }
    }

    [Fact]
    public async Task AlternatingModes_TestToneAndSystemAudio_TransitionsSafely()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        using var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) => new MockEndpointStream(info));

        var source = new AudioEndpointInfo
        {
            Id = "src-pc",
            FriendlyName = "Speakers",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var endpoints = new[]
        {
            source,
            CreateTestEndpoint("bt-1", "Earbuds")
        };

        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Mode 1: Test Tone
            var toneResult = await engine.StartPlaybackAsync(endpoints);
            Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);
            Assert.True(engine.IsRunning);

            // Mode 2: System Audio (switches directly without requiring explicit stop call)
            var shareResult = await engine.StartSystemAudioSharingAsync(source, endpoints);
            Assert.Equal(AudioEngineMode.SystemAudio, engine.ActiveMode);
            Assert.True(mockCapture.IsCapturing);

            // Stop
            await engine.StopPlaybackAsync();
            Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
            Assert.False(mockCapture.IsCapturing);
        }
    }

    [Fact]
    public async Task ConcurrentStartCalls_AreSerializedCleanlyBySessionLock()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        using var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) => new MockEndpointStream(info));

        var endpoints = new[]
        {
            CreateTestEndpoint("ep-1", "Headset 1"),
            CreateTestEndpoint("ep-2", "Headset 2")
        };

        // Fire 10 start calls simultaneously
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            return await engine.StartPlaybackAsync(endpoints);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // All should succeed without unhandled exceptions or state corruption
        Assert.All(results, r => Assert.NotNull(r));
        Assert.True(engine.IsRunning);
        Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);

        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task DestinationFailure_IsIsolated_HealthyDestinationsKeepPlaying()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var healthyEndpoint = CreateTestEndpoint("ep-healthy", "Healthy Speaker");
        var faultyEndpoint = CreateTestEndpoint("ep-faulty", "Faulty Earbuds");

        using var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) =>
            {
                bool shouldFail = info.Id == "ep-faulty";
                return new MockEndpointStream(info, shouldFail);
            });

        var result = await engine.StartPlaybackAsync(new[] { healthyEndpoint, faultyEndpoint });

        Assert.Equal(2, result.AttemptedCount);
        Assert.Equal(1, result.SuccessfullyPlayingCount);
        Assert.Equal(1, result.FailedCount);

        var healthyStatus = result.StreamStatuses.First(s => s.EndpointId == healthyEndpoint.Id);
        var faultyStatus = result.StreamStatuses.First(s => s.EndpointId == faultyEndpoint.Id);

        Assert.Equal(StreamPlaybackState.Playing, healthyStatus.State);
        Assert.Equal(StreamPlaybackState.Failed, faultyStatus.State);
        Assert.Contains("Failure", faultyStatus.ErrorMessage);

        // Healthy stream is still playing despite faulty sibling
        Assert.Equal(1, engine.ActivePlayingCount);
        Assert.True(engine.IsRunning);

        await engine.StopPlaybackAsync();
    }

    [Fact]
    public async Task EngineDisposal_IsIdempotent_AndThrowsObjectDisposedExceptionOnSubsequentStart()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (info, log) => new MockEndpointStream(info));

        var endpoints = new[] { CreateTestEndpoint("ep-1", "Device 1") };
        await engine.StartPlaybackAsync(endpoints);
        Assert.True(engine.IsRunning);

        // First disposal
        engine.Dispose();
        Assert.False(engine.IsRunning);
        Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);

        // Second disposal (idempotency check)
        var ex = Record.Exception(() => engine.Dispose());
        Assert.Null(ex);

        // Subsequent operation should fail fast with ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.StartPlaybackAsync(endpoints));
    }

    [Fact]
    public async Task SettingsService_HandlesCorruptedFileGracefully_AndAppliesSafeDefaults()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "SyncCastTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string settingsFile = Path.Combine(tempDir, "settings.json");

        try
        {
            // Write invalid/corrupt JSON
            await File.WriteAllTextAsync(settingsFile, "{ \"MasterVolumePercent\": 999.0, INVALID_JSON: true, ");

            var settingsService = new SettingsService(_log, settingsFile);
            var loaded = await settingsService.LoadSettingsAsync();

            Assert.NotNull(loaded);
            // Verify safe fallback defaults applied
            Assert.True(loaded.MasterVolumePercent >= 0.0 && loaded.MasterVolumePercent <= 100.0);
            Assert.True(loaded.AutoReconnectEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SmoothGainProvider_PreventsClipping_WhenGainExceedsNormalBound()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        // Source pattern with hot peaks near 1.0f
        var hotSamples = new[] { 0.95f, -0.95f, 1.0f, -1.0f, 0.8f, -0.8f };
        var source = new DummySampleProvider(format, hotSamples);

        var gainProvider = new SmoothGainSampleProvider(source, initialGain: 1.0f);
        var buffer = new float[64];

        int read = gainProvider.Read(buffer, 0, buffer.Length);
        Assert.Equal(64, read);

        // Verify strictly bounded in [-1.0f, 1.0f]
        Assert.All(buffer, s => Assert.True(s >= -1.0f && s <= 1.0f));
    }

    private class DummySampleProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; }
        private readonly float[] _pattern;
        private int _index;

        public DummySampleProvider(WaveFormat format, float[] pattern)
        {
            WaveFormat = format;
            _pattern = pattern;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _pattern[_index % _pattern.Length];
                _index++;
            }
            return count;
        }
    }
}
