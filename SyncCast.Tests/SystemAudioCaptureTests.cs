using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Devices;
using SyncCast.Services.Diagnostics;
using SyncCast.Services.Logging;
using SyncCast.ViewModels;
using Xunit;
using CaptureState = SyncCast.Models.CaptureState;

namespace SyncCast.Tests;

public class SystemAudioCaptureTests
{
    private readonly ILogService _log = new LogService();

    [Fact]
    public async Task StartAndStop_Lifecycle_TransitionsStateCorrectly()
    {
        var captureService = new MockSystemAudioCaptureService();
        var sourceEndpoint = new AudioEndpointInfo
        {
            Id = "device-loopback-1",
            FriendlyName = "Realtek Speakers",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        Assert.Equal(CaptureState.Stopped, captureService.State);
        Assert.False(captureService.IsCapturing);

        bool started = await captureService.StartCaptureAsync(sourceEndpoint);
        Assert.True(started);
        Assert.Equal(CaptureState.Running, captureService.State);
        Assert.True(captureService.IsCapturing);

        await captureService.StopCaptureAsync();
        Assert.Equal(CaptureState.Stopped, captureService.State);
        Assert.False(captureService.IsCapturing);
    }

    [Fact]
    public async Task SystemAudioSharing_SkipsCaptureSource_AsNativeSource_ToPreventFeedback()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var createdStreams = new Dictionary<string, MockEndpointStream>();

        var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (endpoint, _) =>
            {
                var stream = new MockEndpointStream(endpoint);
                createdStreams[endpoint.Id] = stream;
                return stream;
            });

        var sourceEndpoint = new AudioEndpointInfo
        {
            Id = "source-speakers",
            FriendlyName = "Main Speakers",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var headphones = new AudioEndpointInfo
        {
            Id = "dest-headphones",
            FriendlyName = "Bluetooth Headphones",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        };

        var soundbar = new AudioEndpointInfo
        {
            Id = "dest-soundbar",
            FriendlyName = "Bluetooth Soundbar",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        };

        // User selected the capture source (Main Speakers) AND two Bluetooth devices
        var destinations = new List<AudioEndpointInfo> { sourceEndpoint, headphones, soundbar };

        var result = await engine.StartSystemAudioSharingAsync(sourceEndpoint, destinations);

        Assert.True(engine.IsRunning);
        Assert.Equal(AudioEngineMode.SystemAudio, engine.ActiveMode);
        Assert.Equal(3, result.AttemptedCount);
        // Only 2 are mirrored streams; source is marked native
        Assert.Equal(2, result.SuccessfullyPlayingCount);

        // Verify Source Endpoint stream state: marked as NativeSourcePlaying, NOT regular playing
        Assert.True(createdStreams.ContainsKey("source-speakers"));
        var sourceStream = createdStreams["source-speakers"];
        Assert.True(sourceStream.IsNativeSource);
        Assert.Equal(StreamPlaybackState.NativeSourcePlaying, sourceStream.State);

        // Verify Bluetooth outputs are regular Playing
        var hpStream = createdStreams["dest-headphones"];
        Assert.False(hpStream.IsNativeSource);
        Assert.Equal(StreamPlaybackState.Playing, hpStream.State);

        var sbStream = createdStreams["dest-soundbar"];
        Assert.False(sbStream.IsNativeSource);
        Assert.Equal(StreamPlaybackState.Playing, sbStream.State);

        // Simulate incoming audio captured from WASAPI loopback
        byte[] dummyAudio = new byte[1024];
        mockCapture.RaiseDataAvailable(dummyAudio, dummyAudio.Length);

        // Non-source destinations should receive audio chunks
        Assert.Single(hpStream.ReceivedAudioChunks);
        Assert.Single(sbStream.ReceivedAudioChunks);
        // Source must NEVER receive loopback audio back to prevent feedback loop
        Assert.Empty(sourceStream.ReceivedAudioChunks);

        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
        Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
    }

    [Fact]
    public async Task SystemAudioSharing_IsolatesIndividualStreamFailures_WithoutHaltingCapture()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var createdStreams = new Dictionary<string, MockEndpointStream>();

        var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (endpoint, _) =>
            {
                bool shouldFail = endpoint.Id == "dest-broken-bt";
                var stream = new MockEndpointStream(endpoint, shouldFail);
                createdStreams[endpoint.Id] = stream;
                return stream;
            });

        var sourceEndpoint = new AudioEndpointInfo
        {
            Id = "source-speakers",
            FriendlyName = "Main Speakers",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var goodEndpoint = new AudioEndpointInfo
        {
            Id = "dest-good-bt",
            FriendlyName = "Good Bluetooth Headphones",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        };

        var brokenEndpoint = new AudioEndpointInfo
        {
            Id = "dest-broken-bt",
            FriendlyName = "Disconnecting Bluetooth Earbud",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        };

        var destinations = new List<AudioEndpointInfo> { goodEndpoint, brokenEndpoint };

        var result = await engine.StartSystemAudioSharingAsync(sourceEndpoint, destinations);

        Assert.True(engine.IsRunning);
        Assert.Equal(1, result.SuccessfullyPlayingCount);
        Assert.Equal(1, result.FailedCount);

        Assert.Equal(StreamPlaybackState.Playing, createdStreams["dest-good-bt"].State);
        Assert.Equal(StreamPlaybackState.Failed, createdStreams["dest-broken-bt"].State);
        Assert.NotNull(createdStreams["dest-broken-bt"].ErrorMessage);

        // Delivering data should safely go to good stream without crashing on broken stream
        byte[] chunk = new byte[512];
        mockCapture.RaiseDataAvailable(chunk, chunk.Length);

        Assert.Single(createdStreams["dest-good-bt"].ReceivedAudioChunks);

        await engine.StopPlaybackAsync();
    }

    [Fact]
    public async Task ModeSwitching_StopsActiveMode_BeforeStartingAnother()
    {
        var mockCapture = new MockSystemAudioCaptureService();
        var engine = new MultiOutputAudioEngine(
            _log,
            mockCapture,
            null,
            (endpoint, _) => new MockEndpointStream(endpoint));

        var ep1 = new AudioEndpointInfo { Id = "ep1", FriendlyName = "Device 1", State = EndpointActiveState.Active };
        var ep2 = new AudioEndpointInfo { Id = "ep2", FriendlyName = "Device 2", State = EndpointActiveState.Active };

        // 1. Start Test Tone
        await engine.StartPlaybackAsync(new[] { ep1, ep2 });
        Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);
        Assert.True(engine.IsRunning);

        // 2. Start System Audio without explicit stop: engine automatically tears down Mode 1 and starts Mode 2
        await engine.StartSystemAudioSharingAsync(ep1, new[] { ep2 });
        Assert.Equal(AudioEngineMode.SystemAudio, engine.ActiveMode);
        Assert.True(engine.IsRunning);

        // 3. Switch back to Test Tone
        await engine.StartPlaybackAsync(new[] { ep1, ep2 });
        Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);
        Assert.True(engine.IsRunning);

        await engine.StopPlaybackAsync();
        Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void BoundedBuffer_DiscardOnOverflow_PreventsUnboundedLatency()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        // Bounded buffer of 100ms: 48000 samples/sec * 2 channels * 4 bytes/sample * 0.1s = 38,400 bytes
        int bufferBytes = (int)(format.AverageBytesPerSecond * 0.1);

        var bufferedWaveProvider = new BufferedWaveProvider(format)
        {
            BufferLength = bufferBytes,
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        // Write 500ms of audio (5x buffer capacity)
        byte[] oversizedData = new byte[bufferBytes * 5];
        new Random(42).NextBytes(oversizedData);

        // AddSamples should discard on overflow without throwing OutOfMemoryException or growing buffer
        bufferedWaveProvider.AddSamples(oversizedData, 0, oversizedData.Length);

        // BufferedBytes must not exceed BufferLength
        Assert.True(bufferedWaveProvider.BufferedBytes <= bufferedWaveProvider.BufferLength);
    }

    [Fact]
    public async Task MainViewModel_SystemAudioFeedbackAdvisories_AndDestinationCounts()
    {
        var discovery = new MockDiscoveryService();
        var selectionManager = new DeviceSelectionManager(5);
        var diag = new DiagnosticsService();
        var log = new LogService();
        var engine = new MockAudioEngine();

        var vm = new MainViewModel(discovery, selectionManager, diag, log, engine);

        var speaker = new AudioEndpointInfo
        {
            Id = "spk-1",
            FriendlyName = "Realtek High Definition Audio",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var btEarphones = new AudioEndpointInfo
        {
            Id = "bt-1",
            FriendlyName = "Sony WH-1000XM4",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        };

        discovery.MockEndpoints = new List<AudioEndpointInfo> { speaker, btEarphones };
        await vm.RefreshDevicesAsync();

        // 1. Initially no capture source selected
        Assert.NotNull(vm.SelectedCaptureSource); // Defaults to first active
        Assert.Equal("spk-1", vm.SelectedCaptureSource.Id);

        // 2. Select only Bluetooth device as output (speaker is not selected)
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints.First(e => e.Id == "bt-1"));

        Assert.False(vm.IsSourceAlsoSelected);
        Assert.Equal(1, vm.EligibleMirroredDestinationCount);
        Assert.Contains("Windows apps will continue rendering audio", vm.SourceSelectionAdviceMessage);
        Assert.True(vm.CanStartSharing);

        // 3. Now also select the speaker as an output (both speaker and BT selected)
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints.First(e => e.Id == "spk-1"));

        Assert.True(vm.IsSourceAlsoSelected);
        Assert.Equal(1, vm.EligibleMirroredDestinationCount);
        Assert.Contains("play natively via Windows", vm.SourceSelectionAdviceMessage);
        Assert.True(vm.CanStartSharing);

        // 4. If ONLY the speaker is selected, eligible mirrored destinations count is 0
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints.First(e => e.Id == "bt-1")); // Deselect BT

        Assert.True(vm.IsSourceAlsoSelected);
        Assert.Equal(0, vm.EligibleMirroredDestinationCount);
        // Cannot start sharing if there are no OTHER endpoints to mirror to!
        Assert.False(vm.CanStartSharing);
    }
}
