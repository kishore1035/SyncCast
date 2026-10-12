using System.Threading;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Audio.Notifications;
using SyncCast.Services.Devices;
using SyncCast.Services.Diagnostics;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using SyncCast.Services.UI;
using SyncCast.ViewModels;
using Xunit;
using Xunit.Abstractions;
using CaptureState = SyncCast.Models.CaptureState;

namespace SyncCast.Tests;

/// <summary>
/// Comprehensive End-to-End System Verification Tests for SyncCast.
/// Exercises real hardware devices, Core Audio APIs, WASAPI loopback capture,
/// multi-stream rendering, volume control, sync calibration, settings persistence,
/// and WPF ViewModel / View initialization.
/// </summary>
public class EndToEndSystemVerificationTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndSystemVerificationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task E2E_01_DeviceDiscovery_RealHardware()
    {
        var discoveryService = new AudioEndpointDiscoveryService();
        var allEndpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: true);
        var activeEndpoints = allEndpoints.Where(e => e.State == EndpointActiveState.Active).ToList();

        _output.WriteLine($"[E2E Discovery] Total discovered endpoints: {allEndpoints.Count} (Active: {activeEndpoints.Count})");

        Assert.NotEmpty(allEndpoints);
        foreach (var ep in allEndpoints)
        {
            _output.WriteLine($"  - [{ep.State}] '{ep.FriendlyName}' | Transport: {ep.Transport} | Format: {ep.FormatSummary} | ID: {ep.Id}");
            Assert.False(string.IsNullOrWhiteSpace(ep.Id));
            Assert.False(string.IsNullOrWhiteSpace(ep.FriendlyName));
            Assert.DoesNotContain("Sample Device", ep.FriendlyName);
            Assert.DoesNotContain("Fake Device", ep.FriendlyName);
        }

        Assert.True(activeEndpoints.Count >= 1, "Expected at least 1 active audio endpoint on this Windows system.");
    }

    [Fact]
    public async Task E2E_02_DeviceSelection_LimitAndPreservation()
    {
        var selectionManager = new DeviceSelectionManager();
        var discoveryService = new AudioEndpointDiscoveryService();
        var activeEndpoints = (await discoveryService.DiscoverEndpointsAsync(includeInactive: false)).ToList();

        _output.WriteLine($"[E2E Selection] Testing selection logic on {activeEndpoints.Count} real endpoint(s)...");

        // 1. Select endpoints up to max
        for (int i = 0; i < activeEndpoints.Count; i++)
        {
            var ep = activeEndpoints[i];
            var result = selectionManager.ToggleSelection(ep, out var msg);
            Assert.Equal(SelectionResult.Selected, result);
            Assert.True(selectionManager.IsSelected(ep.Id));
        }

        _output.WriteLine($"[E2E Selection] Selected {selectionManager.SelectedCount} device(s). Count matches: {selectionManager.SelectedCount}");
        Assert.Equal(activeEndpoints.Count, selectionManager.SelectedCount);

        // 2. Enforce 5-device limit behavior
        if (selectionManager.SelectedCount < 5)
        {
            var dummyList = new List<AudioEndpointInfo>();
            for (int i = selectionManager.SelectedCount; i < 6; i++)
            {
                dummyList.Add(new AudioEndpointInfo
                {
                    Id = $"synthetic-limit-test-{i}",
                    FriendlyName = $"Limit Test Device {i}",
                    State = EndpointActiveState.Active,
                    Transport = DeviceTransportType.Bluetooth
                });
            }

            foreach (var dummy in dummyList)
            {
                if (selectionManager.SelectedCount < 5)
                {
                    var res = selectionManager.ToggleSelection(dummy, out _);
                    Assert.Equal(SelectionResult.Selected, res);
                }
                else
                {
                    // 6th device should be rejected
                    var res = selectionManager.ToggleSelection(dummy, out var warning);
                    Assert.Equal(SelectionResult.LimitReached, res);
                    Assert.Contains("Limit reached", warning);
                    _output.WriteLine("[E2E Selection] Successfully verified 5-device strict limit enforcement.");
                    break;
                }
            }
        }

        // 3. Clear selections cleanly
        selectionManager.ClearSelection();
        Assert.Equal(0, selectionManager.SelectedCount);
        _output.WriteLine("[E2E Selection] Selections cleared successfully.");
    }

    [Fact]
    public async Task E2E_03_ToneGenerator_RealHardwareConcurrency()
    {
        var logService = new LogService();
        var discoveryService = new AudioEndpointDiscoveryService();
        var sampleSource = new ToneGeneratorSampleSource
        {
            Frequency = 440,
            Volume = 0.02f // Safe volume for automated test
        };

        using var engine = new MultiOutputAudioEngine(logService, sampleSource);

        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: false);
        var activeEndpoints = endpoints.Where(e => e.State == EndpointActiveState.Active).Take(5).ToList();

        if (activeEndpoints.Count == 0)
        {
            _output.WriteLine("[E2E Tone] No active endpoints available. Skipping real render test.");
            return;
        }

        _output.WriteLine($"[E2E Tone] Launching concurrent tone playback across {activeEndpoints.Count} hardware device(s)...");

        var result = await engine.StartPlaybackAsync(activeEndpoints);
        _output.WriteLine($"[E2E Tone] StartPlayback result: Attempted={result.AttemptedCount}, Playing={result.SuccessfullyPlayingCount}, Failed={result.FailedCount}");

        Assert.True(result.HasAnyPlaying, "At least one endpoint must enter playing state.");
        Assert.Equal(AudioEngineMode.TestTone, engine.ActiveMode);
        Assert.True(engine.IsRunning);

        // Run for 1000ms and verify stream telemetry
        await Task.Delay(1000);

        foreach (var status in engine.CurrentStreamStatuses)
        {
            _output.WriteLine($"  - Stream '{status.EndpointName}': State={status.State}, TargetFormat={status.TargetFormat}, BufferDelay={status.EstimatedBufferDelayMs:F1}ms");
            if (status.State == StreamPlaybackState.Playing)
            {
                Assert.True(status.HasRenderedAudioData, $"Playing stream '{status.EndpointName}' must report rendered audio data.");
            }
        }

        // Test dynamic master volume and mute
        _output.WriteLine("[E2E Tone] Adjusting master volume to 50% and muting...");
        engine.SetMasterVolume(50.0, isMuted: true);
        Assert.Equal(50.0, engine.MasterVolumePercent);
        Assert.True(engine.IsMasterMuted);

        await Task.Delay(300);

        engine.SetMasterVolume(100.0, isMuted: false);
        Assert.False(engine.IsMasterMuted);

        // Test per-device volume and mute
        var firstEndpoint = activeEndpoints[0];
        _output.WriteLine($"[E2E Tone] Adjusting device volume for '{firstEndpoint.FriendlyName}' to 30%...");
        engine.SetDeviceVolume(firstEndpoint.Id, 30.0, isMuted: true);
        await Task.Delay(300);
        engine.SetDeviceVolume(firstEndpoint.Id, 100.0, isMuted: false);

        // Clean stop
        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
        Assert.Equal(0, engine.ActivePlayingCount);
        _output.WriteLine("[E2E Tone] Playback stopped cleanly and resources disposed.");
    }

    [Fact]
    public async Task E2E_04_SystemAudioLoopbackCapture_RealHardware()
    {
        var logService = new LogService();
        var discoveryService = new AudioEndpointDiscoveryService();
        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: false);
        var activeEndpoints = endpoints.Where(e => e.State == EndpointActiveState.Active).ToList();

        if (activeEndpoints.Count == 0)
        {
            _output.WriteLine("[E2E Loopback] No active endpoints available. Skipping loopback test.");
            return;
        }

        var sourceEndpoint = activeEndpoints[0];
        _output.WriteLine($"[E2E Loopback] Testing loopback capture on source: '{sourceEndpoint.FriendlyName}' (ID: {sourceEndpoint.Id})");

        using var captureService = new SystemAudioCaptureService(logService);

        int chunksReceived = 0;
        long totalBytesCaptured = 0;
        captureService.DataAvailable += (s, e) =>
        {
            Interlocked.Increment(ref chunksReceived);
            Interlocked.Add(ref totalBytesCaptured, e.BytesRecorded);
        };

        bool started = await captureService.StartCaptureAsync(sourceEndpoint);
        _output.WriteLine($"[E2E Loopback] Capture start result: {started}, State: {captureService.State}, Format: {captureService.Status.CaptureFormatText}");

        Assert.True(started, "WASAPI Loopback capture failed to start on default active endpoint.");
        Assert.Equal(CaptureState.Running, captureService.State);
        Assert.True(captureService.IsCapturing);

        // Play a short tone into the same endpoint using WasapiOut so audio actually flows into Windows mixer
        var testToneSource = new ToneGeneratorSampleSource { Frequency = 440, Volume = 0.02f };
        using (var mmEnumerator = new MMDeviceEnumerator())
        {
            var mmDev = mmEnumerator.GetDevice(sourceEndpoint.Id);
            using var wasapiOut = new WasapiOut(mmDev, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            var waveProvider = AudioFormatAdapter.AdaptToTargetFormat(testToneSource.CreateStreamSampleProvider(), mmDev.AudioClient.MixFormat);
            wasapiOut.Init(waveProvider);
            wasapiOut.Play();

            // Wait 1200ms to allow loopback capture to record audio frames
            await Task.Delay(1200);

            wasapiOut.Stop();
        }

        await captureService.StopCaptureAsync();
        Assert.Equal(CaptureState.Stopped, captureService.State);
        Assert.False(captureService.IsCapturing);

        _output.WriteLine($"[E2E Loopback] Chunks received: {chunksReceived}, Total bytes captured: {totalBytesCaptured}");
        Assert.True(chunksReceived > 0, "Loopback capture should have received at least one audio chunk while audio was rendering.");
        Assert.True(totalBytesCaptured > 0, "Loopback capture should have recorded > 0 bytes.");
    }

    [Fact]
    public async Task E2E_05_SystemAudioSharing_ZeroEchoAndDistribution_RealHardware()
    {
        var logService = new LogService();
        var discoveryService = new AudioEndpointDiscoveryService();
        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: false);
        var activeEndpoints = endpoints.Where(e => e.State == EndpointActiveState.Active).ToList();

        if (activeEndpoints.Count < 2)
        {
            _output.WriteLine($"[E2E Sharing] Found {activeEndpoints.Count} active endpoints. Need at least 2 for distribution test. Skipping.");
            return;
        }

        var sourceEndpoint = activeEndpoints[0];
        // Select sourceEndpoint AND secondary active endpoints
        var destinations = activeEndpoints.Take(3).ToList();

        _output.WriteLine($"[E2E Sharing] Capture Source: '{sourceEndpoint.FriendlyName}'");
        _output.WriteLine($"[E2E Sharing] Selected Destinations: {string.Join(", ", destinations.Select(d => d.FriendlyName))}");

        using var engine = new MultiOutputAudioEngine(logService);

        var result = await engine.StartSystemAudioSharingAsync(sourceEndpoint, destinations);
        _output.WriteLine($"[E2E Sharing] StartSystemAudioSharing result: Attempted={result.AttemptedCount}, Playing={result.SuccessfullyPlayingCount}, Failed={result.FailedCount}");

        Assert.True(engine.IsRunning);
        Assert.Equal(AudioEngineMode.SystemAudio, engine.ActiveMode);

        // Verify Zero-Echo: source endpoint must be marked NativeSourcePlaying
        var sourceStream = engine.CurrentStreamStatuses.FirstOrDefault(s => string.Equals(s.EndpointId, sourceEndpoint.Id, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sourceStream);
        Assert.Equal(StreamPlaybackState.NativeSourcePlaying, sourceStream.State);
        _output.WriteLine($"[E2E Sharing] Verified Zero-Echo: '{sourceStream.EndpointName}' is NativeSourcePlaying.");

        // Verify secondary endpoints are in Playing state
        var mirroredStreams = engine.CurrentStreamStatuses.Where(s => !string.Equals(s.EndpointId, sourceEndpoint.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var mStream in mirroredStreams)
        {
            _output.WriteLine($"  - Mirrored destination '{mStream.EndpointName}': State={mStream.State}, Format={mStream.TargetFormat}");
            Assert.Equal(StreamPlaybackState.Playing, mStream.State);
        }

        // Generate audio on source endpoint for 1200ms so loopback captures and fans out
        var testToneSource = new ToneGeneratorSampleSource { Frequency = 528, Volume = 0.02f };
        using (var mmEnumerator = new MMDeviceEnumerator())
        {
            var mmDev = mmEnumerator.GetDevice(sourceEndpoint.Id);
            using var wasapiOut = new WasapiOut(mmDev, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            var waveProvider = AudioFormatAdapter.AdaptToTargetFormat(testToneSource.CreateStreamSampleProvider(), mmDev.AudioClient.MixFormat);
            wasapiOut.Init(waveProvider);
            wasapiOut.Play();

            await Task.Delay(1200);

            wasapiOut.Stop();
        }

        // Verify mirrored streams received and wrote audio bytes
        foreach (var mStream in engine.CurrentStreamStatuses.Where(s => s.State == StreamPlaybackState.Playing))
        {
            _output.WriteLine($"  - Stream '{mStream.EndpointName}': BytesWritten={mStream.BytesWritten}, Drops={mStream.BufferOverflowDropCount}");
            Assert.True(mStream.BytesWritten > 0, $"Mirrored destination '{mStream.EndpointName}' must have written audio bytes.");
        }

        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
        _output.WriteLine("[E2E Sharing] System audio sharing stopped cleanly.");
    }

    [Fact]
    public async Task E2E_06_GuidedCalibrationPulse_RealHardware()
    {
        var logService = new LogService();
        var discoveryService = new AudioEndpointDiscoveryService();
        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: false);
        var activeEndpoints = endpoints.Where(e => e.State == EndpointActiveState.Active).Take(3).ToList();

        if (activeEndpoints.Count == 0)
        {
            _output.WriteLine("[E2E Calibration] No active endpoints available. Skipping calibration test.");
            return;
        }

        using var engine = new MultiOutputAudioEngine(logService);

        _output.WriteLine($"[E2E Calibration] Starting calibration pulse across {activeEndpoints.Count} hardware endpoints...");

        var result = await engine.StartCalibrationPulseAsync(activeEndpoints);
        Assert.True(result.HasAnyPlaying);
        Assert.Equal(AudioEngineMode.CalibrationPulse, engine.ActiveMode);
        Assert.True(engine.IsRunning);

        // Test setting delay offsets
        var refEp = activeEndpoints[0];
        engine.SetReferenceEndpoint(refEp.Id);
        Assert.Equal(refEp.Id, engine.ReferenceEndpointId);

        if (activeEndpoints.Count > 1)
        {
            engine.SetDeviceSyncDelay(activeEndpoints[1].Id, 25.0);
            Assert.Equal(25.0, engine.GetDeviceSyncDelay(activeEndpoints[1].Id));
            _output.WriteLine($"[E2E Calibration] Set sync delay on '{activeEndpoints[1].FriendlyName}': 25.0 ms");
        }

        await Task.Delay(1000);

        // Check timeline drift status
        foreach (var ep in activeEndpoints)
        {
            var destStatus = engine.Timeline.GetDestinationStatus(ep.Id);
            if (destStatus != null)
            {
                _output.WriteLine($"  - Timeline '{ep.FriendlyName}': FramesEnqueued={destStatus.FramesEnqueued}, EstimatedDrift={destStatus.EstimatedDriftMs:F2}ms, DelayOffset={destStatus.ConfiguredDelayMs}ms");
            }
        }

        await engine.StopPlaybackAsync();
        Assert.False(engine.IsRunning);
        _output.WriteLine("[E2E Calibration] Calibration pulse stopped cleanly.");
    }

    [Fact]
    public async Task E2E_07_SettingsPersistence_Roundtrip()
    {
        var logService = new LogService();
        var settingsService = new SettingsService(logService);

        var originalSettings = await settingsService.LoadSettingsAsync();

        try
        {
            // Modify settings
            originalSettings.MasterVolumePercent = 77.5;
            originalSettings.IsMasterMuted = true;
            originalSettings.AutoReconnectEnabled = false;
            originalSettings.ThemeMode = "Dark";
            originalSettings.DeviceSettings["device-abc"] = new DeviceVolumeSetting
            {
                DeviceFriendlyName = "Test Bluetooth",
                VolumePercent = 82.0,
                IsMuted = false,
                SyncDelayMs = 35.0
            };

            await settingsService.SaveSettingsAsync(originalSettings);

            // Reload from disk to verify roundtrip
            var reloaded = await settingsService.LoadSettingsAsync();

            Assert.Equal(77.5, reloaded.MasterVolumePercent);
            Assert.True(reloaded.IsMasterMuted);
            Assert.False(reloaded.AutoReconnectEnabled);
            Assert.Equal("Dark", reloaded.ThemeMode);
            Assert.True(reloaded.DeviceSettings.ContainsKey("device-abc"));
            Assert.Equal(35.0, reloaded.DeviceSettings["device-abc"].SyncDelayMs);
            Assert.Equal(82.0, reloaded.DeviceSettings["device-abc"].VolumePercent);

            _output.WriteLine("[E2E Settings] Successfully verified JSON settings persistence and reload roundtrip.");
        }
        finally
        {
            // Reset to safe default settings
            await settingsService.SaveSettingsAsync(new SyncCastSettings());
        }
    }

    [Fact]
    public void E2E_08_WpfAppAndMainWindow_Instantiation_STA()
    {
        // WPF elements must be tested on an STA thread
        var thread = new Thread(() =>
        {
            var logService = new LogService();
            var settingsService = new SettingsService(logService);
            var discoveryService = new AudioEndpointDiscoveryService();
            var selectionManager = new DeviceSelectionManager();
            var notificationService = new AudioEndpointNotificationService(logService);
            var captureService = new SystemAudioCaptureService(logService);
            var engine = new MultiOutputAudioEngine(logService, captureService, sampleSource: null, streamFactory: null, notificationService: notificationService);
            var diagnosticsService = new DiagnosticsService();

            var viewModel = new MainViewModel(
                discoveryService,
                selectionManager,
                diagnosticsService,
                logService,
                engine,
                settingsService);

            _output.WriteLine($"[E2E WPF] MainViewModel initialized. CurrentPage={viewModel.CurrentPage}, MasterVolume={viewModel.MasterVolumePercent}");
            Assert.Equal(NavigationPage.Overview, viewModel.CurrentPage);
            Assert.NotNull(viewModel.AvailableEndpoints);
            Assert.NotNull(viewModel.ActiveStreams);
            Assert.NotNull(viewModel.StartAudioTestCommand);
            Assert.NotNull(viewModel.StartSystemAudioSharingCommand);

            // Test navigation tab switching
            viewModel.NavigateCommand.Execute(nameof(NavigationPage.Devices));
            Assert.Equal(NavigationPage.Devices, viewModel.CurrentPage);

            viewModel.NavigateCommand.Execute(nameof(NavigationPage.SyncCalibration));
            Assert.Equal(NavigationPage.SyncCalibration, viewModel.CurrentPage);

            viewModel.NavigateCommand.Execute(nameof(NavigationPage.Diagnostics));
            Assert.Equal(NavigationPage.Diagnostics, viewModel.CurrentPage);

            viewModel.NavigateCommand.Execute(nameof(NavigationPage.Settings));
            Assert.Equal(NavigationPage.Settings, viewModel.CurrentPage);

            viewModel.NavigateCommand.Execute(nameof(NavigationPage.About));
            Assert.Equal(NavigationPage.About, viewModel.CurrentPage);

            // Test theme switching
            ThemeService.ApplyTheme(AppThemeMode.Dark);
            Assert.True(ThemeService.IsDarkTheme);
            ThemeService.ApplyTheme(AppThemeMode.Light);
            Assert.False(ThemeService.IsDarkTheme);

            // Ensure Application resources and converters are available in test host
            if (Application.Current == null)
            {
                _ = new Application();
            }

            if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString?.Contains("Styles.xaml") == true))
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SyncCast;component/Resources/Styles.xaml", UriKind.Absolute)
                });
            }

            Application.Current.Resources["BoolToVis"] = new SyncCast.Converters.BooleanToVisibilityConverter();
            Application.Current.Resources["InvertBoolToVis"] = new SyncCast.Converters.BooleanToVisibilityConverter { Invert = true };
            Application.Current.Resources["InvertBool"] = new SyncCast.Converters.InvertBooleanConverter();
            Application.Current.Resources["TransportToColor"] = new SyncCast.Converters.TransportToColorConverter();
            Application.Current.Resources["StateToBrush"] = new SyncCast.Converters.StateToStatusBrushConverter();
            Application.Current.Resources["LogLevelToBrush"] = new SyncCast.Converters.LogLevelToBrushConverter();
            Application.Current.Resources["StreamStateToBrush"] = new SyncCast.Converters.StreamStateToBrushConverter();
            Application.Current.Resources["CaptureStateToBrush"] = new SyncCast.Converters.CaptureStateToBrushConverter();
            Application.Current.Resources["EqToVis"] = new SyncCast.Converters.EqualityToVisibilityConverter();
            Application.Current.Resources["EqToBool"] = new SyncCast.Converters.EqualityToBooleanConverter();
            Application.Current.Resources["TransportToIcon"] = new SyncCast.Converters.TransportToIconConverter();

            // Verify MainWindow instantiation
            var mainWindow = new MainWindow { DataContext = viewModel };
            Assert.NotNull(mainWindow);
            Assert.Same(viewModel, mainWindow.DataContext);

            _output.WriteLine("[E2E WPF] MainWindow and MainViewModel successfully instantiated without error.");

            // Cleanup
            mainWindow.Close();
            engine.Dispose();
            notificationService.Dispose();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);
    }
}
