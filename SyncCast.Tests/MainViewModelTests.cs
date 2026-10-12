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

public class MockSystemAudioCaptureService : ISystemAudioCaptureService
{
    public CaptureState State { get; set; } = CaptureState.Stopped;
    public WaveFormat? CaptureFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public bool IsCapturing => State == CaptureState.Running;
    public SystemAudioCaptureStatus Status => new()
    {
        State = State,
        CaptureFormatText = "48,000 Hz • Stereo • Float",
        HasReceivedAudioData = true
    };

    public event EventHandler<AudioDataChunkEventArgs>? DataAvailable;
    public event EventHandler<SystemAudioCaptureStatus>? StatusChanged;

    public Task<bool> StartCaptureAsync(AudioEndpointInfo sourceEndpoint, CancellationToken cancellationToken = default)
    {
        State = CaptureState.Running;
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(true);
    }

    public Task StopCaptureAsync()
    {
        State = CaptureState.Stopped;
        StatusChanged?.Invoke(this, Status);
        return Task.CompletedTask;
    }

    public void RaiseDataAvailable(byte[] buffer, int bytesRecorded)
    {
        DataAvailable?.Invoke(this, new AudioDataChunkEventArgs(buffer, bytesRecorded, CaptureFormat!));
    }

    public void Dispose() { }
}

public class MockDiscoveryService : IAudioEndpointDiscoveryService
{
    public List<AudioEndpointInfo> MockEndpoints { get; set; } = new();
    public bool ShouldThrow { get; set; }

    public Task<IReadOnlyList<AudioEndpointInfo>> DiscoverEndpointsAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new InvalidOperationException("Simulated Core Audio Enumeration Failure");
        }

        return Task.FromResult<IReadOnlyList<AudioEndpointInfo>>(MockEndpoints);
    }

    public EndpointActiveState GetCurrentDeviceState(string endpointId)
    {
        var match = MockEndpoints.FirstOrDefault(e => e.Id == endpointId);
        return match?.State ?? EndpointActiveState.Active;
    }
}

public class MockAudioEngine : IMultiOutputAudioEngine
{
    public bool IsRunning { get; set; }
    public AudioEngineMode ActiveMode { get; set; } = AudioEngineMode.Idle;
    public ISampleSource SampleSource { get; set; } = new ToneGeneratorSampleSource();
    public ISystemAudioCaptureService CaptureService { get; set; } = new MockSystemAudioCaptureService();
    public int ActivePlayingCount => MockStatuses.Count(s => s.State == StreamPlaybackState.Playing);
    public List<OutputStreamStatus> MockStatuses { get; set; } = new();

    public double MasterVolumePercent { get; set; } = 100.0;
    public bool IsMasterMuted { get; set; } = false;
    public bool AutoReconnectEnabled { get; set; } = true;
    public Dictionary<string, (double Vol, bool Muted)> DeviceVolumes { get; set; } = new();
    public string? ReferenceEndpointId { get; set; }
    public SharedAudioTimeline Timeline { get; } = new();
    public Dictionary<string, double> DeviceSyncDelays { get; set; } = new();

    public void SetReferenceEndpoint(string? endpointId)
    {
        ReferenceEndpointId = endpointId;
    }

    public void SetDeviceSyncDelay(string endpointId, double delayMs)
    {
        DeviceSyncDelays[endpointId] = Math.Clamp(delayMs, 0.0, 1000.0);
    }

    public void ResetAllSyncDelays()
    {
        DeviceSyncDelays.Clear();
    }

    public double GetDeviceSyncDelay(string endpointId)
    {
        return DeviceSyncDelays.TryGetValue(endpointId, out double d) ? d : 0.0;
    }

    public void SetMasterVolume(double masterVolumePercent, bool isMuted)
    {
        MasterVolumePercent = masterVolumePercent;
        IsMasterMuted = isMuted;
    }

    public void SetDeviceVolume(string endpointId, double volumePercent, bool isMuted)
    {
        DeviceVolumes[endpointId] = (volumePercent, isMuted);
    }

    public Task<bool> RetryEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<MultiOutputPlaybackResult> StartCalibrationPulseAsync(IReadOnlyList<AudioEndpointInfo> endpoints, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        ActiveMode = AudioEngineMode.CalibrationPulse;
        MockStatuses = endpoints.Select(e => new OutputStreamStatus
        {
            EndpointId = e.Id,
            EndpointName = e.FriendlyName,
            Transport = e.Transport,
            State = StreamPlaybackState.Playing,
            TargetFormat = "44,100 Hz • Stereo • Float",
            LatencyMs = 100
        }).ToList();

        PlaybackStateChanged?.Invoke(this, true);

        return Task.FromResult(new MultiOutputPlaybackResult
        {
            AttemptedCount = endpoints.Count,
            SuccessfullyPlayingCount = MockStatuses.Count,
            FailedCount = 0,
            StreamStatuses = MockStatuses
        });
    }

    public IReadOnlyList<OutputStreamStatus> CurrentStreamStatuses => MockStatuses;

    public event EventHandler<OutputStreamStatus>? StreamStatusChanged = delegate { };
    public event EventHandler<bool>? PlaybackStateChanged = delegate { };

    public Task<MultiOutputPlaybackResult> StartPlaybackAsync(IReadOnlyList<AudioEndpointInfo> endpoints, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        ActiveMode = AudioEngineMode.TestTone;
        MockStatuses = endpoints.Select(e => new OutputStreamStatus
        {
            EndpointId = e.Id,
            EndpointName = e.FriendlyName,
            Transport = e.Transport,
            State = StreamPlaybackState.Playing,
            TargetFormat = "48,000 Hz • Stereo • Float",
            LatencyMs = 100
        }).ToList();

        PlaybackStateChanged?.Invoke(this, true);

        return Task.FromResult(new MultiOutputPlaybackResult
        {
            AttemptedCount = endpoints.Count,
            SuccessfullyPlayingCount = MockStatuses.Count,
            FailedCount = 0,
            StreamStatuses = MockStatuses
        });
    }

    public Task<MultiOutputPlaybackResult> StartSystemAudioSharingAsync(AudioEndpointInfo sourceEndpoint, IReadOnlyList<AudioEndpointInfo> destinationEndpoints, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        ActiveMode = AudioEngineMode.SystemAudio;

        var statuses = new List<OutputStreamStatus>();
        int playingCount = 0;

        foreach (var dest in destinationEndpoints)
        {
            if (dest.Id == sourceEndpoint.Id)
            {
                statuses.Add(new OutputStreamStatus
                {
                    EndpointId = dest.Id,
                    EndpointName = dest.FriendlyName,
                    Transport = dest.Transport,
                    State = StreamPlaybackState.NativeSourcePlaying,
                    IsNativeSource = true,
                    TargetFormat = "48,000 Hz • Stereo • Float",
                    LatencyMs = 0
                });
            }
            else
            {
                statuses.Add(new OutputStreamStatus
                {
                    EndpointId = dest.Id,
                    EndpointName = dest.FriendlyName,
                    Transport = dest.Transport,
                    State = StreamPlaybackState.Playing,
                    HasRenderedAudioData = true,
                    TargetFormat = "48,000 Hz • Stereo • Float",
                    LatencyMs = 100
                });
                playingCount++;
            }
        }

        MockStatuses = statuses;
        PlaybackStateChanged?.Invoke(this, true);

        return Task.FromResult(new MultiOutputPlaybackResult
        {
            AttemptedCount = destinationEndpoints.Count,
            SuccessfullyPlayingCount = playingCount,
            FailedCount = 0,
            StreamStatuses = MockStatuses
        });
    }

    public Task StopPlaybackAsync()
    {
        IsRunning = false;
        ActiveMode = AudioEngineMode.Idle;
        MockStatuses = MockStatuses.Select(s => s with { State = StreamPlaybackState.Stopped }).ToList();
        PlaybackStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

public class MainViewModelTests
{
    private (MainViewModel Vm, MockDiscoveryService Discovery, IDeviceSelectionManager SelectionManager, MockAudioEngine Engine) CreateSut()
    {
        var discovery = new MockDiscoveryService();
        var selectionManager = new DeviceSelectionManager(5);
        var diag = new DiagnosticsService();
        var log = new LogService();
        var engine = new MockAudioEngine();

        var vm = new MainViewModel(discovery, selectionManager, diag, log, engine);
        return (vm, discovery, selectionManager, engine);
    }

    [Fact]
    public async Task RefreshDevices_PopulatesEndpointsAndMaintainsAccurateCounts()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Speakers", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired },
            new() { Id = "ep2", FriendlyName = "WH-1000XM4", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep3", FriendlyName = "Offline USB", State = EndpointActiveState.Unplugged, Transport = DeviceTransportType.Unknown }
        };

        await vm.RefreshDevicesAsync();

        Assert.Equal(3, vm.AvailableEndpoints.Count);
        Assert.True(vm.HasEndpoints);
        Assert.Equal(2, vm.Diagnostics.ActiveRenderEndpointsCount);
        Assert.Equal(1, vm.Diagnostics.LikelyBluetoothEndpointsCount);
        Assert.Equal(0, vm.Diagnostics.TestedEndpointsCount);
    }

    [Fact]
    public async Task DeviceSelection_EnforcesLimitAndShowsUserFeedback()
    {
        var (vm, discovery, _, _) = CreateSut();
        var endpoints = Enumerable.Range(1, 6).Select(i => new AudioEndpointInfo
        {
            Id = $"ep-{i}",
            FriendlyName = $"Device #{i}",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.Bluetooth
        }).ToList();

        discovery.MockEndpoints = endpoints;
        await vm.RefreshDevicesAsync();

        // Select first 5 devices
        for (int i = 0; i < 5; i++)
        {
            vm.OnDeviceToggleRequested(vm.AvailableEndpoints[i]);
        }

        Assert.Equal(5, vm.SelectedCount);
        Assert.True(vm.HasReachedMaxSelection);
        Assert.Equal("5/5 selected outputs", vm.SelectionCountText);
        Assert.False(vm.HasSelectionWarning);

        // Attempt to select 6th device
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[5]);

        Assert.Equal(5, vm.SelectedCount);
        Assert.True(vm.HasSelectionWarning);
        Assert.NotNull(vm.SelectionWarning);
        Assert.Contains("Maximum 5 output endpoints allowed", vm.SelectionWarning);
    }

    [Fact]
    public async Task DeviceSelection_PreservesSelectedEndpointsAcrossRefresh()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep2", FriendlyName = "Speakers B", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        await vm.RefreshDevicesAsync();
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[0]); // Select ep1
        Assert.Equal(1, vm.SelectedCount);

        // Re-run refresh (manual refresh)
        await vm.RefreshDevicesAsync();

        Assert.Equal(1, vm.SelectedCount);
        Assert.True(vm.AvailableEndpoints[0].IsSelected);
        Assert.Single(vm.SelectedEndpoints);
        Assert.Equal("ep1", vm.SelectedEndpoints[0].Id);
    }

    [Fact]
    public async Task RefreshDevices_HandlesErrorsGracefullyWithoutCrashing()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.ShouldThrow = true;

        await vm.RefreshDevicesAsync();

        Assert.False(vm.IsScanning);
        Assert.True(vm.HasSelectionWarning);
        Assert.Contains("Failed to scan audio endpoints", vm.SelectionWarning);
    }

    [Fact]
    public async Task StartAudioTest_WarnsWhenNoEndpointsSelected()
    {
        var (vm, _, _, _) = CreateSut();

        await vm.StartAudioTestAsync();

        Assert.False(vm.IsAudioPlaying);
        Assert.True(vm.HasSelectionWarning);
        Assert.Contains("select at least 1 audio output", vm.SelectionWarning);
    }

    [Fact]
    public async Task StartAudioTest_StartsSelectedStreamsAndUpdatesTestedDiagnostics()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep2", FriendlyName = "Speakers B", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        await vm.RefreshDevicesAsync();
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[0]);
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[1]);

        await vm.StartAudioTestAsync();

        Assert.True(vm.IsAudioPlaying);
        Assert.Equal(2, vm.ActiveStreams.Count);
        Assert.Equal(2, vm.Diagnostics.TestedEndpointsCount); // Verified count updated!

        await vm.StopAudioTestAsync();
        Assert.False(vm.IsAudioPlaying);
    }

    [Fact]
    public async Task SetReferenceEndpoint_UpdatesReferenceDeviceAndStreamFlags()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep2", FriendlyName = "Speakers B", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        await vm.RefreshDevicesAsync();
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[0]);
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[1]);

        await vm.StartAudioTestAsync();
        Assert.Equal(2, vm.ActiveStreams.Count);

        vm.SetReferenceEndpoint("ep2");

        Assert.Equal("ep2", vm.ReferenceEndpointId);
        Assert.Equal("Speakers B", vm.ReferenceEndpointName);
        Assert.False(vm.ActiveStreams[0].IsSyncReference);
        Assert.True(vm.ActiveStreams[1].IsSyncReference);

        await vm.StopAudioTestAsync();
    }

    [Fact]
    public async Task ResetAllDelays_SetsAllActiveStreamsDelayToZero()
    {
        var (vm, discovery, _, _) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep2", FriendlyName = "Speakers B", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        await vm.RefreshDevicesAsync();
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[0]);
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[1]);

        await vm.StartAudioTestAsync();
        vm.ActiveStreams[0].SyncDelayMs = 45.0;
        vm.ActiveStreams[1].SyncDelayMs = 90.0;

        vm.ResetAllDelays();

        Assert.Equal(0.0, vm.ActiveStreams[0].SyncDelayMs);
        Assert.Equal(0.0, vm.ActiveStreams[1].SyncDelayMs);

        await vm.StopAudioTestAsync();
    }

    [Fact]
    public async Task CalibrationPulse_StartsAndStopsCleanly_WithTwoSelectedEndpoints()
    {
        var (vm, discovery, _, engine) = CreateSut();
        discovery.MockEndpoints = new List<AudioEndpointInfo>
        {
            new() { Id = "ep1", FriendlyName = "Headphones A", State = EndpointActiveState.Active, Transport = DeviceTransportType.Bluetooth },
            new() { Id = "ep2", FriendlyName = "Speakers B", State = EndpointActiveState.Active, Transport = DeviceTransportType.BuiltInOrWired }
        };

        await vm.RefreshDevicesAsync();
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[0]);
        vm.OnDeviceToggleRequested(vm.AvailableEndpoints[1]);

        Assert.True(vm.CanStartCalibration);

        await vm.StartCalibrationPulseAsync();

        Assert.True(vm.IsCalibrationRunning);
        Assert.Equal(AudioEngineMode.CalibrationPulse, engine.ActiveMode);
        Assert.Equal(2, vm.ActiveStreams.Count);

        await vm.StopCalibrationPulseAsync();

        Assert.False(vm.IsCalibrationRunning);
        Assert.Equal(AudioEngineMode.Idle, engine.ActiveMode);
    }
}
