using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Logging;
using Xunit;

namespace SyncCast.Tests;

public class MockEndpointStream : IEndpointAudioStream
{
    public string EndpointId { get; }
    public string EndpointName { get; }
    public DeviceTransportType Transport { get; }
    public StreamPlaybackState State { get; private set; } = StreamPlaybackState.Stopped;
    public string? ErrorMessage { get; private set; }
    public bool ShouldFail { get; set; }
    public bool IsNativeSource { get; set; }
    public bool HasRenderedAudioData { get; set; }
    public List<byte[]> ReceivedAudioChunks { get; } = new();

    public double DeviceVolumePercent { get; set; } = 100.0;
    public bool IsMuted { get; set; } = false;
    public double MasterVolumePercent { get; set; } = 100.0;
    public bool IsMasterMuted { get; set; } = false;
    public double EffectiveGainPercent => (IsMuted || IsMasterMuted) ? 0.0 : (MasterVolumePercent * DeviceVolumePercent / 100.0);

    public event EventHandler<OutputStreamStatus>? StatusChanged;

    public OutputStreamStatus Status => new()
    {
        EndpointId = EndpointId,
        EndpointName = EndpointName,
        Transport = Transport,
        State = State,
        TargetFormat = "48,000 Hz • Stereo • Float",
        LatencyMs = 100,
        ErrorMessage = ErrorMessage,
        IsNativeSource = IsNativeSource,
        HasRenderedAudioData = HasRenderedAudioData,
        DeviceVolume = DeviceVolumePercent,
        IsMuted = IsMuted,
        EffectiveGainPercent = EffectiveGainPercent,
        SyncDelayMs = SyncDelayMs,
        IsSyncReference = IsSyncReference,
        EstimatedDriftMs = EstimatedDriftMs,
        LastBlockSequence = LastBlockSequence
    };

    public double SyncDelayMs { get; private set; } = 0.0;
    public bool IsSyncReference { get; set; } = false;
    public long LastBlockSequence { get; private set; } = 0;
    public double EstimatedDriftMs { get; private set; } = 0.0;

    public void SetSyncDelay(double delayMs, bool isReference = false)
    {
        SyncDelayMs = Math.Clamp(delayMs, 0.0, 1000.0);
        IsSyncReference = isReference;
        StatusChanged?.Invoke(this, Status);
    }

    public void UpdateTimelineMetrics(long lastSequence, double estimatedDriftMs)
    {
        LastBlockSequence = lastSequence;
        EstimatedDriftMs = estimatedDriftMs;
        StatusChanged?.Invoke(this, Status);
    }

    public void SetVolume(double deviceVolumePercent, bool isMuted, double masterVolumePercent, bool isMasterMuted)
    {
        DeviceVolumePercent = deviceVolumePercent;
        IsMuted = isMuted;
        MasterVolumePercent = masterVolumePercent;
        IsMasterMuted = isMasterMuted;
        StatusChanged?.Invoke(this, Status);
    }

    public void MarkAsDisconnected(string reason)
    {
        ErrorMessage = reason;
        State = StreamPlaybackState.Disconnected;
        StatusChanged?.Invoke(this, Status);
    }

    public void MarkAsRecovering()
    {
        State = StreamPlaybackState.Recovering;
        StatusChanged?.Invoke(this, Status);
    }

    public MockEndpointStream(AudioEndpointInfo info, bool shouldFail = false)
    {
        EndpointId = info.Id;
        EndpointName = info.FriendlyName;
        Transport = info.Transport;
        ShouldFail = shouldFail;
    }

    public Task<bool> StartAsync(ISampleSource sampleSource, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            State = StreamPlaybackState.Failed;
            ErrorMessage = "Simulated WASAPI Device Open Failure";
            StatusChanged?.Invoke(this, Status);
            return Task.FromResult(false);
        }

        State = StreamPlaybackState.Playing;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(true);
    }

    public Task<bool> StartWithLiveCaptureAsync(WaveFormat captureFormat, CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            State = StreamPlaybackState.Failed;
            ErrorMessage = "Simulated WASAPI Device Open Failure";
            StatusChanged?.Invoke(this, Status);
            return Task.FromResult(false);
        }

        State = StreamPlaybackState.Playing;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(true);
    }

    public void MarkAsNativeSource()
    {
        IsNativeSource = true;
        State = StreamPlaybackState.NativeSourcePlaying;
        StatusChanged?.Invoke(this, Status);
    }

    public void EnqueueAudioData(byte[] buffer, int offset, int count)
    {
        byte[] copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        ReceivedAudioChunks.Add(copy);
        HasRenderedAudioData = true;
    }

    public Task StopAsync()
    {
        State = StreamPlaybackState.Stopped;
        StatusChanged?.Invoke(this, Status);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        State = StreamPlaybackState.Stopped;
    }
}

public class AudioEngineTests
{
    [Fact]
    public void ToneGenerator_GeneratesContinuousValidSamples()
    {
        var generator = new ToneGeneratorSampleSource(48000, 2);
        generator.Volume = 0.1f;
        generator.Frequency = 440.0;

        var streamProvider = generator.CreateStreamSampleProvider();
        float[] buffer = new float[960]; // 10 ms of stereo audio

        int read = streamProvider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        // Verify samples are non-zero, bounded, and without NaNs
        bool hasNonZero = false;
        for (int i = 0; i < read; i++)
        {
            Assert.False(float.IsNaN(buffer[i]));
            Assert.False(float.IsInfinity(buffer[i]));
            Assert.InRange(buffer[i], -0.15f, 0.15f);
            if (Math.Abs(buffer[i]) > 0.001f) hasNonZero = true;
        }
        Assert.True(hasNonZero, "Tone generator should produce audio waveform");
    }

    [Fact]
    public void ToneGenerator_MuteZeroesOutput()
    {
        var generator = new ToneGeneratorSampleSource();
        generator.IsMuted = true;

        var streamProvider = generator.CreateStreamSampleProvider();
        float[] buffer = new float[480];

        int read = streamProvider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        for (int i = 0; i < read; i++)
        {
            Assert.Equal(0.0f, buffer[i]);
        }
    }

    [Fact]
    public void AudioFormatAdapter_AdaptsSampleRateAndEncoding()
    {
        var generator = new ToneGeneratorSampleSource(48000, 2);
        var sourceProvider = generator.CreateStreamSampleProvider();

        // Target: 44,100 Hz, 16-bit PCM Stereo
        var targetFormat = new WaveFormat(44100, 16, 2);
        var adaptedProvider = AudioFormatAdapter.AdaptToTargetFormat(sourceProvider, targetFormat);

        Assert.Equal(44100, adaptedProvider.WaveFormat.SampleRate);
        Assert.Equal(16, adaptedProvider.WaveFormat.BitsPerSample);
        Assert.Equal(2, adaptedProvider.WaveFormat.Channels);

        byte[] buffer = new byte[882]; // 10 ms at 44.1 kHz 16-bit stereo (441 frames * 4 bytes)
        int readBytes = adaptedProvider.Read(buffer, 0, buffer.Length);

        Assert.True(readBytes > 0);
    }

    [Fact]
    public void AudioFormatAdapter_AdaptsChannelCounts()
    {
        var generator = new ToneGeneratorSampleSource(48000, 2);
        var sourceProvider = generator.CreateStreamSampleProvider();

        // Target: 48,000 Hz, Mono 16-bit
        var monoTarget = new WaveFormat(48000, 16, 1);
        var monoAdapted = AudioFormatAdapter.AdaptToTargetFormat(sourceProvider, monoTarget);

        Assert.Equal(1, monoAdapted.WaveFormat.Channels);
        byte[] buffer = new byte[480];
        int read = monoAdapted.Read(buffer, 0, buffer.Length);
        Assert.True(read > 0);

        // Target: 48,000 Hz, 6-Channel (5.1 surround)
        var surroundTarget = WaveFormat.CreateIeeeFloatWaveFormat(48000, 6);
        var surroundAdapted = AudioFormatAdapter.AdaptToTargetFormat(sourceProvider, surroundTarget);
        Assert.Equal(6, surroundAdapted.WaveFormat.Channels);
        byte[] surroundBuffer = new byte[480 * 6 * 4];
        int surroundRead = surroundAdapted.Read(surroundBuffer, 0, surroundBuffer.Length);
        Assert.True(surroundRead > 0);
    }

    [Fact]
    public async Task MultiOutputEngine_StartsMultipleStreamsIndependently()
    {
        var log = new LogService();
        var sampleSource = new ToneGeneratorSampleSource();

        var engine = new MultiOutputAudioEngine(
            log,
            sampleSource,
            (endpoint, _) => new MockEndpointStream(endpoint));

        var endpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "dev-1", FriendlyName = "Output 1", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "dev-2", FriendlyName = "Output 2", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired },
            new() { Id = "dev-3", FriendlyName = "Output 3", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth }
        };

        var result = await engine.StartPlaybackAsync(endpoints);

        Assert.True(engine.IsRunning);
        Assert.Equal(3, result.AttemptedCount);
        Assert.Equal(3, result.SuccessfullyPlayingCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(3, engine.ActivePlayingCount);

        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
        Assert.Equal(0, engine.ActivePlayingCount);
    }

    [Fact]
    public async Task MultiOutputEngine_IsolatesIndividualStreamFailures_WithoutCrashingSession()
    {
        var log = new LogService();
        var sampleSource = new ToneGeneratorSampleSource();

        // Device 2 will fail to open, while Devices 1 & 3 succeed
        var engine = new MultiOutputAudioEngine(
            log,
            sampleSource,
            (endpoint, _) => new MockEndpointStream(endpoint, shouldFail: endpoint.Id == "dev-2"));

        var endpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "dev-1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "dev-2", FriendlyName = "Faulty BT Earphones", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "dev-3", FriendlyName = "Speakers C", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        var result = await engine.StartPlaybackAsync(endpoints);

        // Crucial requirement: report which streams actually opened and played; NEVER count selected endpoints as successful playback
        Assert.Equal(3, result.AttemptedCount);
        Assert.Equal(2, result.SuccessfullyPlayingCount);
        Assert.Equal(1, result.FailedCount);
        Assert.True(result.HasAnyPlaying);
        Assert.False(result.AllSucceeded);

        // Verify Device 2 reported explicit failure
        var failedStream = result.StreamStatuses.First(s => s.EndpointId == "dev-2");
        Assert.Equal(StreamPlaybackState.Failed, failedStream.State);
        Assert.Contains("Simulated WASAPI Device Open Failure", failedStream.ErrorMessage);

        // Verify Devices 1 & 3 are still playing
        Assert.Equal(StreamPlaybackState.Playing, result.StreamStatuses.First(s => s.EndpointId == "dev-1").State);
        Assert.Equal(StreamPlaybackState.Playing, result.StreamStatuses.First(s => s.EndpointId == "dev-3").State);

        await engine.StopPlaybackAsync();
    }

    [Fact]
    public async Task MultiOutputEngine_EnforcesMaxFiveEndpointsCap()
    {
        var log = new LogService();
        var engine = new MultiOutputAudioEngine(
            log,
            new ToneGeneratorSampleSource(),
            (endpoint, _) => new MockEndpointStream(endpoint));

        var sixEndpoints = Enumerable.Range(1, 6).Select(i => new AudioEndpointInfo
        {
            Id = $"id-{i}",
            FriendlyName = $"Device {i}",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        }).ToList();

        var result = await engine.StartPlaybackAsync(sixEndpoints);

        // Strictly capped at 5
        Assert.Equal(5, result.AttemptedCount);
        Assert.Equal(5, result.SuccessfullyPlayingCount);

        await engine.StopPlaybackAsync();
    }
}
