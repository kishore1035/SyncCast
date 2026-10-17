using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncCast.Models;
using SyncCast.Services.Audio;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Devices;
using SyncCast.Services.Diagnostics;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using SyncCast.Services.UI;

namespace SyncCast.ViewModels;

public enum NavigationPage
{
    Overview,
    Devices,
    SyncCalibration,
    Diagnostics,
    Settings,
    About
}

public partial class MainViewModel : ObservableObject
{
    private readonly IAudioEndpointDiscoveryService _discoveryService;
    private readonly IDeviceSelectionManager _selectionManager;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly ILogService _logService;
    private readonly IMultiOutputAudioEngine _audioEngine;
    private readonly ISettingsService _settingsService;
    private readonly SynchronizationContext? _syncContext;
    private readonly System.Timers.Timer _diagnosticUpdateTimer;
    private readonly System.Timers.Timer _settingsDebounceTimer;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusNotice = "Ready";

    [ObservableProperty]
    private string? _selectionWarning;

    [ObservableProperty]
    private bool _hasSelectionWarning;

    [ObservableProperty]
    private bool _hasEndpoints;

    [ObservableProperty]
    private DiagnosticInfo _diagnostics;

    // --- Audio Engine State Properties ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAudio))]
    [NotifyPropertyChangedFor(nameof(CanStopAudio))]
    [NotifyPropertyChangedFor(nameof(CanStartSharing))]
    [NotifyPropertyChangedFor(nameof(CanStopSharing))]
    [NotifyPropertyChangedFor(nameof(HasActiveStreams))]
    [NotifyPropertyChangedFor(nameof(CanShowStartSharingPrompt))]
    [NotifyPropertyChangedFor(nameof(AudioDevicesStatusTitle))]
    [NotifyPropertyChangedFor(nameof(AudioDevicesStatusDescription))]
    private bool _isAudioPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAudio))]
    [NotifyPropertyChangedFor(nameof(CanStartSharing))]
    private bool _isStartingPlayback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartAudio))]
    [NotifyPropertyChangedFor(nameof(CanStopAudio))]
    [NotifyPropertyChangedFor(nameof(CanStartSharing))]
    [NotifyPropertyChangedFor(nameof(CanStopSharing))]
    [NotifyPropertyChangedFor(nameof(HasActiveStreams))]
    [NotifyPropertyChangedFor(nameof(CanShowStartSharingPrompt))]
    [NotifyPropertyChangedFor(nameof(AudioDevicesStatusTitle))]
    [NotifyPropertyChangedFor(nameof(AudioDevicesStatusDescription))]
    private bool _isSharingAudio;

    [ObservableProperty]
    private double _toneFrequency = 440.0;

    [ObservableProperty]
    private int _masterVolumePercent = 10; // Safe default for test tone (10% / -20 dBFS)

    // --- Chunk 4: Global Master Volume (0–100%) & Mute ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterVolumeText))]
    private double _masterVolume = 100.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterVolumeText))]
    [NotifyPropertyChangedFor(nameof(MasterMuteButtonText))]
    private bool _isMasterMuted = false;

    private double _previousMasterVolume = 100.0;

    public string MasterVolumeText => IsMasterMuted ? "MUTED" : $"{MasterVolume:F0}%";
    public string MasterMuteButtonText => IsMasterMuted ? "Unmute Master" : "Mute Master";

    // --- Chunk 4: Auto-Reconnect Preference ---
    [ObservableProperty]
    private bool _autoReconnect = true;

    // --- Windows 11 Fluent Navigation & Appearance ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewPage))]
    [NotifyPropertyChangedFor(nameof(IsDevicesPage))]
    [NotifyPropertyChangedFor(nameof(IsSyncCalibrationPage))]
    [NotifyPropertyChangedFor(nameof(IsDiagnosticsPage))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage))]
    [NotifyPropertyChangedFor(nameof(IsAboutPage))]
    private NavigationPage _currentPage = NavigationPage.Overview;

    public bool IsOverviewPage => CurrentPage == NavigationPage.Overview;
    public bool IsDevicesPage => CurrentPage == NavigationPage.Devices;
    public bool IsSyncCalibrationPage => CurrentPage == NavigationPage.SyncCalibration;
    public bool IsDiagnosticsPage => CurrentPage == NavigationPage.Diagnostics;
    public bool IsSettingsPage => CurrentPage == NavigationPage.Settings;
    public bool IsAboutPage => CurrentPage == NavigationPage.About;

    [ObservableProperty]
    private string _themeMode = "Dark";

    [RelayCommand]
    public void Navigate(string pageName)
    {
        if (Enum.TryParse<NavigationPage>(pageName, true, out var page))
        {
            CurrentPage = page;
        }
    }

    [RelayCommand]
    public void SetTheme(string modeName)
    {
        ThemeMode = modeName;
        if (Enum.TryParse<AppThemeMode>(modeName, true, out var mode))
        {
            ThemeService.ApplyTheme(mode, Application.Current?.MainWindow);
            DebounceSaveSettings();
        }
    }

    [ObservableProperty]
    private string _concurrencyNotice = "Select candidate outputs to start playback or system audio sharing.";

    // --- Chunk 3: System Audio Capture Properties ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceSelectionAdviceMessage))]
    [NotifyPropertyChangedFor(nameof(IsSourceAlsoSelected))]
    [NotifyPropertyChangedFor(nameof(EligibleMirroredDestinationCount))]
    [NotifyPropertyChangedFor(nameof(CanStartSharing))]
    private AudioEndpointViewModel? _selectedCaptureSource;

    [ObservableProperty]
    private SystemAudioCaptureStatus _captureStatus = new();

    public ObservableCollection<AudioEndpointViewModel> AvailableEndpoints { get; } = new();
    public ObservableCollection<AudioEndpointViewModel> AvailableCaptureSources { get; } = new();
    public ObservableCollection<AudioEndpointViewModel> SelectedEndpoints { get; } = new();
    public ObservableCollection<OutputStreamViewModel> ActiveStreams { get; } = new();
    public ObservableCollection<LogMessage> DiagnosticLogs => _logService.Logs;

    public string AppName => "SyncCast";
    public string Subtitle => "Multi-Device Audio";
    public string AppVersion
    {
        get
        {
            var ver = typeof(MainViewModel).Assembly.GetName().Version;
            return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.0";
        }
    }
    public string WindowsVersionInfo => RuntimeInformation.OSDescription;
    public string DotNetVersionInfo => RuntimeInformation.FrameworkDescription;

    public int SelectedCount => _selectionManager.SelectedCount;
    public int MaxSelectionLimit => _selectionManager.MaxSelectionLimit;
    public string SelectionCountText => $"{SelectedCount}/{MaxSelectionLimit} selected outputs";
    public bool HasReachedMaxSelection => SelectedCount >= MaxSelectionLimit;

    // --- Chunk 5: Multi-Output Audio Synchronization & Guided Wizard ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReferenceEndpointName))]
    private string? _referenceEndpointId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartCalibration))]
    [NotifyPropertyChangedFor(nameof(CanStopCalibration))]
    [NotifyPropertyChangedFor(nameof(CanStartAudio))]
    [NotifyPropertyChangedFor(nameof(CanStartSharing))]
    private bool _isCalibrationRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep0Active))]
    [NotifyPropertyChangedFor(nameof(IsStep1Active))]
    [NotifyPropertyChangedFor(nameof(IsStep2Active))]
    [NotifyPropertyChangedFor(nameof(IsStep3Active))]
    private int _calibrationStepIndex = 0;

    public bool IsStep0Active => CalibrationStepIndex == 0;
    public bool IsStep1Active => CalibrationStepIndex == 1;
    public bool IsStep2Active => CalibrationStepIndex == 2;
    public bool IsStep3Active => CalibrationStepIndex == 3;

    public string ReferenceEndpointName
    {
        get
        {
            if (string.IsNullOrEmpty(ReferenceEndpointId)) return "None (Auto / First Selected)";
            var match = AvailableEndpoints.FirstOrDefault(e => e.Id == ReferenceEndpointId);
            return match?.FriendlyName ?? ReferenceEndpointId;
        }
    }

    // Feedback and Duplication Prevention Flags
    public bool IsSourceAlsoSelected =>
        SelectedCaptureSource != null && SelectedEndpoints.Any(e => e.Id == SelectedCaptureSource.Id);

    public int EligibleMirroredDestinationCount =>
        SelectedCaptureSource != null
            ? SelectedEndpoints.Count(e => e.Id != SelectedCaptureSource.Id)
            : SelectedEndpoints.Count;

    public string SourceSelectionAdviceMessage
    {
        get
        {
            if (SelectedCaptureSource == null)
            {
                return "Select an active Windows playback device as the loopback capture source.";
            }

            if (IsSourceAlsoSelected)
            {
                return $"Capture Source ('{SelectedCaptureSource.FriendlyName}') is also in your selected outputs. To prevent feedback and echo, it will play natively via Windows, while audio is mirrored to the other {EligibleMirroredDestinationCount} selected output(s).";
            }

            return $"Advisory: Windows apps will continue rendering audio to '{SelectedCaptureSource.FriendlyName}' natively, in addition to your {EligibleMirroredDestinationCount} mirrored destination(s). Software delay compensates mirrored outputs only.";
        }
    }

    // Command Eligibility
    public bool CanStartAudio => !IsAudioPlaying && !IsSharingAudio && !IsCalibrationRunning && !IsStartingPlayback && SelectedCount > 0;
    public bool CanStopAudio => IsAudioPlaying && !IsSharingAudio && !IsCalibrationRunning;

    public bool CanStartSharing => !IsSharingAudio && !IsAudioPlaying && !IsCalibrationRunning && !IsStartingPlayback && SelectedCaptureSource != null && EligibleMirroredDestinationCount > 0;
    public bool CanStopSharing => IsSharingAudio;

    public bool CanStartCalibration => !IsAudioPlaying && !IsSharingAudio && !IsCalibrationRunning && !IsStartingPlayback && SelectedCount >= 2;
    public bool CanStopCalibration => IsCalibrationRunning;

    public bool HasActiveStreams => ActiveStreams.Count > 0;
    public bool CanShowStartSharingPrompt => !IsSharingAudio && !IsAudioPlaying && CanStartSharing;

    public string AudioDevicesStatusTitle
    {
        get
        {
            if (IsSharingAudio) return "Live Multi-Device Audio Sharing Active";
            if (IsAudioPlaying) return "Test Tone Audio Generator Active";
            if (SelectedCount >= 2) return $"{SelectedCount} Devices Selected (Engine Idle)";
            if (SelectedCount == 1) return "1 Device Selected (Engine Idle)";
            return "No Devices Selected";
        }
    }

    public string AudioDevicesStatusDescription
    {
        get
        {
            if (IsSharingAudio)
            {
                return $"Mirroring system audio from '{SelectedCaptureSource?.FriendlyName ?? "Default Device"}' to {EligibleMirroredDestinationCount} destination(s) concurrently.";
            }
            if (IsAudioPlaying)
            {
                return "Rendering test tone across all selected destinations.";
            }
            if (SelectedCount >= 2)
            {
                return $"Click 'Start Sharing' to duplicate your Windows audio from '{SelectedCaptureSource?.FriendlyName ?? "Default Device"}' across all selected devices.";
            }
            if (SelectedCount == 1)
            {
                return "Select at least one more device to enable multi-device audio sharing.";
            }
            return "Toggle the switch next to any connected headphones or speakers to add them to your multi-device setup.";
        }
    }

    public bool HasMultipleBluetoothSelected =>
        SelectedEndpoints.Count(e => e.Transport == DeviceTransportType.Bluetooth) >= 2;

    public string MultipleBluetoothWarningMessage =>
        "Hardware Notice: Your PC has a single Bluetooth radio adapter. Windows Bluetooth (A2DP) can only actively stream audio to one Bluetooth headset at a time. When a second Bluetooth headset connects, Windows hands off the active audio stream to the newest device and puts the other into standby. To play audio on two headphones simultaneously, pair one via Bluetooth and plug the other into the 3.5mm headphone jack, a USB port, or a secondary USB Bluetooth dongle.";

    public int HealthyStreamsCount => ActiveStreams.Count(s => s.IsPlaying);

    public string SyncStatusText
    {
        get
        {
            if (ActiveStreams.Count == 0) return "Idle (No Active Streams)";
            bool anyDelay = ActiveStreams.Any(s => s.SyncDelayMs > 0);
            if (anyDelay) return "Software Delay Configured (Acoustic Unverified)";
            return "Zero Software Offset (Baseline Timeline)";
        }
    }

    public string SyncEngineNote =>
        "Software buffer compensation adjusts relative rendering offset across endpoints. Bluetooth transport latency, hardware buffers, and acoustic acoustic propagation depend on individual physical devices.";

    public MainViewModel(
        IAudioEndpointDiscoveryService discoveryService,
        IDeviceSelectionManager selectionManager,
        IDiagnosticsService diagnosticsService,
        ILogService logService,
        IMultiOutputAudioEngine audioEngine,
        ISettingsService? settingsService = null)
    {
        _discoveryService = discoveryService;
        _selectionManager = selectionManager;
        _diagnosticsService = diagnosticsService;
        _logService = logService;
        _audioEngine = audioEngine;
        _settingsService = settingsService ?? new SettingsService(logService);
        _syncContext = SynchronizationContext.Current;

        _diagnostics = _diagnosticsService.GetDiagnostics(Array.Empty<AudioEndpointInfo>(), 0);

        _audioEngine.StreamStatusChanged += OnEngineStreamStatusChanged;
        _audioEngine.PlaybackStateChanged += OnEnginePlaybackStateChanged;
        _audioEngine.CaptureService.StatusChanged += OnCaptureStatusChanged;

        // Throttled UI diagnostic update timer (updates every 250ms without flooding UI callbacks)
        _diagnosticUpdateTimer = new System.Timers.Timer(250) { AutoReset = true };
        _diagnosticUpdateTimer.Elapsed += OnDiagnosticTimerElapsed;
        _diagnosticUpdateTimer.Start();

        // Debounced settings save timer (500ms delay after user changes slider)
        _settingsDebounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _settingsDebounceTimer.Elapsed += async (_, _) => await SaveSettingsAsync();

        _logService.LogInfo("SyncCast initialized. Stage 4 Volume, Diagnostics & Reconnection loaded.");
        _logService.LogInfo($"Host: {WindowsVersionInfo} | Runtime: {DotNetVersionInfo}");

        _ = LoadSettingsAsync();
    }

    partial void OnMasterVolumeChanged(double value)
    {
        _audioEngine.SetMasterVolume(value, IsMasterMuted);
        DebounceSaveSettings();
    }

    partial void OnAutoReconnectChanged(bool value)
    {
        _audioEngine.AutoReconnectEnabled = value;
        _logService.LogInfo($"[Settings] Auto-reconnect is now {(value ? "Enabled" : "Disabled")}.");
        DebounceSaveSettings();
    }

    [RelayCommand]
    public void ToggleMasterMute()
    {
        IsMasterMuted = !IsMasterMuted;
        if (IsMasterMuted)
        {
            _previousMasterVolume = MasterVolume;
        }
        else if (MasterVolume <= 0.0)
        {
            MasterVolume = _previousMasterVolume > 0.0 ? _previousMasterVolume : 100.0;
        }

        _audioEngine.SetMasterVolume(MasterVolume, IsMasterMuted);
        DebounceSaveSettings();
    }

    private void OnDeviceVolumeChanged(string endpointId, double volume, bool isMuted)
    {
        _audioEngine.SetDeviceVolume(endpointId, volume, isMuted);
        DebounceSaveSettings();
    }

    private void OnDeviceDelayChanged(string endpointId, double delayMs)
    {
        _audioEngine.SetDeviceSyncDelay(endpointId, delayMs);
        DebounceSaveSettings();
    }

    [RelayCommand]
    public void SetReferenceEndpoint(string? endpointId)
    {
        ReferenceEndpointId = endpointId;
        _audioEngine.SetReferenceEndpoint(endpointId);
        foreach (var s in ActiveStreams)
        {
            s.IsSyncReference = string.Equals(s.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase);
        }
        OnPropertyChanged(nameof(ReferenceEndpointName));
        DebounceSaveSettings();
        _logService.LogInfo($"[Sync Reference] Set timing reference to: {endpointId ?? "None"}");
    }

    [RelayCommand]
    public void ResetAllDelays()
    {
        _audioEngine.ResetAllSyncDelays();
        foreach (var s in ActiveStreams)
        {
            s.SyncDelayMs = 0.0;
        }
        DebounceSaveSettings();
        _logService.LogInfo("[Sync Calibration] Reset all output delays to 0 ms.");
    }

    [RelayCommand]
    public void NextCalibrationStep()
    {
        if (CalibrationStepIndex < 3)
        {
            CalibrationStepIndex++;
        }
    }

    [RelayCommand]
    public void PrevCalibrationStep()
    {
        if (CalibrationStepIndex > 0)
        {
            CalibrationStepIndex--;
        }
    }

    [RelayCommand]
    public void GoToCalibrationStep(string stepStr)
    {
        if (int.TryParse(stepStr, out int step) && step >= 0 && step <= 3)
        {
            CalibrationStepIndex = step;
        }
    }

    [RelayCommand]
    public async Task StartCalibrationPulseAsync()
    {
        if (SelectedCount < 2)
        {
            SelectionWarning = "Please select at least 2 output endpoints to run delay calibration.";
            HasSelectionWarning = true;
            return;
        }

        if (IsAudioPlaying || IsSharingAudio)
        {
            await _audioEngine.StopPlaybackAsync();
            IsAudioPlaying = false;
            IsSharingAudio = false;
        }

        IsStartingPlayback = true;
        StatusNotice = "Starting gentle acoustic calibration pulse...";
        DismissWarning();

        try
        {
            var candidateInfos = SelectedEndpoints.Select(vm => vm.Info).ToList();
            var result = await _audioEngine.StartCalibrationPulseAsync(candidateInfos);

            ActiveStreams.Clear();
            foreach (var status in result.StreamStatuses)
            {
                ActiveStreams.Add(new OutputStreamViewModel(
                    status,
                    OnDeviceVolumeChanged,
                    OnDeviceDelayChanged,
                    SetReferenceEndpoint,
                    OnRetryEndpointRequested));
            }

            IsCalibrationRunning = result.HasAnyPlaying;
            IsAudioPlaying = false;
            IsSharingAudio = false;

            if (result.HasAnyPlaying)
            {
                StatusNotice = $"Calibration pulse running across {result.SuccessfullyPlayingCount} endpoint(s).";
            }
            else
            {
                StatusNotice = "Could not start calibration pulse on selected devices.";
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Calibration Error] {ex.Message}");
            SelectionWarning = $"Failed starting calibration pulse: {ex.Message}";
            HasSelectionWarning = true;
            IsCalibrationRunning = false;
        }
        finally
        {
            IsStartingPlayback = false;
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
            OnPropertyChanged(nameof(CanStartCalibration));
            OnPropertyChanged(nameof(CanStopCalibration));
        }
    }

    [RelayCommand]
    public async Task StopCalibrationPulseAsync()
    {
        StatusNotice = "Stopping calibration pulse...";
        try
        {
            await _audioEngine.StopPlaybackAsync();
            foreach (var streamVm in ActiveStreams)
            {
                streamVm.State = StreamPlaybackState.Stopped;
            }
            IsCalibrationRunning = false;
            StatusNotice = "Calibration pulse stopped.";
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Calibration Stop Error] {ex.Message}");
        }
        finally
        {
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
            OnPropertyChanged(nameof(CanStartCalibration));
            OnPropertyChanged(nameof(CanStopCalibration));
        }
    }

    private async Task OnRetryEndpointRequested(string endpointId)
    {
        _logService.LogInfo($"[Manual Retry Requested] User initiated recovery for endpoint '{endpointId}'.");
        await _audioEngine.RetryEndpointAsync(endpointId);
    }

    private void DebounceSaveSettings()
    {
        _settingsDebounceTimer.Stop();
        _settingsDebounceTimer.Start();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            if (settings != null)
            {
                MasterVolume = settings.MasterVolumePercent;
                IsMasterMuted = settings.IsMasterMuted;
                AutoReconnect = settings.AutoReconnectEnabled;
                ReferenceEndpointId = settings.ReferenceEndpointId;
                _audioEngine.SetReferenceEndpoint(settings.ReferenceEndpointId);

                if (!string.IsNullOrEmpty(settings.ThemeMode))
                {
                    ThemeMode = settings.ThemeMode;
                    if (Enum.TryParse<AppThemeMode>(ThemeMode, true, out var mode))
                    {
                        ThemeService.ApplyTheme(mode, Application.Current?.MainWindow);
                    }
                }

                _audioEngine.SetMasterVolume(MasterVolume, IsMasterMuted);
                _audioEngine.AutoReconnectEnabled = AutoReconnect;

                foreach (var (epId, devSetting) in settings.DeviceSettings)
                {
                    _audioEngine.SetDeviceVolume(epId, devSetting.VolumePercent, devSetting.IsMuted);
                    _audioEngine.SetDeviceSyncDelay(epId, devSetting.SyncDelayMs);
                }
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"[Settings] Could not load stored preferences: {ex.Message}");
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var existingSettings = await _settingsService.LoadSettingsAsync();
            var dict = existingSettings?.DeviceSettings ?? new Dictionary<string, DeviceVolumeSetting>();

            foreach (var s in ActiveStreams)
            {
                dict[s.EndpointId] = new DeviceVolumeSetting
                {
                    DeviceFriendlyName = s.EndpointName,
                    VolumePercent = s.DeviceVolume,
                    IsMuted = s.IsMuted,
                    SyncDelayMs = s.SyncDelayMs
                };
            }

            var settings = new SyncCastSettings
            {
                MasterVolumePercent = MasterVolume,
                IsMasterMuted = IsMasterMuted,
                AutoReconnectEnabled = AutoReconnect,
                LastCaptureSourceId = SelectedCaptureSource?.Id,
                ReferenceEndpointId = ReferenceEndpointId,
                ThemeMode = ThemeMode,
                DeviceSettings = dict
            };

            await _settingsService.SaveSettingsAsync(settings);
        }
        catch
        {
        }
    }

    private void OnDiagnosticTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!IsSharingAudio && !IsAudioPlaying) return;

        void UpdateMetrics()
        {
            if (IsSharingAudio)
            {
                CaptureStatus = _audioEngine.CaptureService.Status;
            }

            foreach (var status in _audioEngine.CurrentStreamStatuses)
            {
                var match = ActiveStreams.FirstOrDefault(s => s.EndpointId == status.EndpointId);
                match?.Update(status);
            }
        }

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => UpdateMetrics(), null);
        }
        else
        {
            UpdateMetrics();
        }
    }

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        StatusNotice = "Scanning Windows Core Audio endpoints...";
        _logService.LogInfo("Initiating Windows audio endpoint discovery...");
        DismissWarning();

        try
        {
            var discovered = await _discoveryService.DiscoverEndpointsAsync(includeInactive: true);

            _selectionManager.PrunePreservedSelection(discovered);

            string? previousSourceId = SelectedCaptureSource?.Id;

            AvailableEndpoints.Clear();
            AvailableCaptureSources.Clear();
            SelectedEndpoints.Clear();

            int activeCount = 0;
            int btCount = 0;
            AudioEndpointViewModel? defaultCandidate = null;

            foreach (var endpoint in discovered)
            {
                bool isSelected = _selectionManager.IsSelected(endpoint.Id);
                var vm = new AudioEndpointViewModel(endpoint, isSelected, OnDeviceToggleRequested);
                AvailableEndpoints.Add(vm);

                if (isSelected)
                {
                    SelectedEndpoints.Add(vm);
                }

                if (endpoint.IsActive)
                {
                    activeCount++;
                    AvailableCaptureSources.Add(vm);

                    if (endpoint.IsDefaultEndpoint)
                    {
                        defaultCandidate = vm;
                    }

                    if (endpoint.Transport == DeviceTransportType.Bluetooth)
                    {
                        btCount++;
                    }
                }
            }

            if (previousSourceId != null)
            {
                SelectedCaptureSource = AvailableCaptureSources.FirstOrDefault(s => s.Id == previousSourceId)
                                        ?? defaultCandidate
                                        ?? AvailableCaptureSources.FirstOrDefault();
            }
            else
            {
                SelectedCaptureSource = defaultCandidate ?? AvailableCaptureSources.FirstOrDefault();
            }

            HasEndpoints = AvailableEndpoints.Count > 0;
            StatusNotice = $"Found {discovered.Count} endpoint(s) ({activeCount} active, {btCount} Bluetooth).";
            _logService.LogSuccess($"Discovery complete: {activeCount} active endpoints found. Selected capture source: '{SelectedCaptureSource?.FriendlyName ?? "None"}'.");

            UpdateDiagnostics(discovered, testedCount: _audioEngine.ActivePlayingCount);
            UpdateSelectionCounts();

            if (!string.IsNullOrEmpty(ReferenceEndpointId) && !discovered.Any(e => e.Id == ReferenceEndpointId && e.State == EndpointActiveState.Active))
            {
                var replacement = SelectedEndpoints.FirstOrDefault() ?? AvailableEndpoints.FirstOrDefault(e => e.IsActive);
                _logService.LogWarning($"[Sync Reference] Previous reference device is no longer active. Switching reference to '{replacement?.FriendlyName ?? "None"}'.");
                SetReferenceEndpoint(replacement?.Id);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Endpoint discovery failed: {ex.Message}");
            StatusNotice = "Endpoint discovery encountered an error.";
            SelectionWarning = $"Failed to scan audio endpoints: {ex.Message}";
            HasSelectionWarning = true;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public void RefreshCaptureSources()
    {
        _ = RefreshDevicesAsync();
    }

    public void OnDeviceToggleRequested(AudioEndpointViewModel targetEndpoint)
    {
        DismissWarning();

        // If currently marked inactive, check live Windows state in case it just connected
        if (targetEndpoint.Info.State != EndpointActiveState.Active)
        {
            var liveState = _discoveryService.GetCurrentDeviceState(targetEndpoint.Id);
            if (liveState == EndpointActiveState.Active)
            {
                targetEndpoint.UpdateState(EndpointActiveState.Active);
            }
        }

        bool wasSelected = targetEndpoint.IsSelected;
        var result = _selectionManager.ToggleSelection(targetEndpoint.Info, out string message);
        bool isNowSelected = result == SelectionResult.Selected;

        targetEndpoint.IsSelected = isNowSelected;

        if (isNowSelected && !wasSelected)
        {
            if (!SelectedEndpoints.Any(e => e.Id == targetEndpoint.Id))
            {
                SelectedEndpoints.Add(targetEndpoint);
            }
            _logService.LogInfo($"Selected endpoint: '{targetEndpoint.FriendlyName}' [{targetEndpoint.Transport}]");

            if (_audioEngine.IsRunning)
            {
                _ = Task.Run(async () =>
                {
                    var status = await _audioEngine.AttachDestinationAsync(targetEndpoint.Info);
                    if (status != null)
                    {
                        void AddStreamAction()
                        {
                            if (!ActiveStreams.Any(s => s.EndpointId == targetEndpoint.Id))
                            {
                                ActiveStreams.Add(new OutputStreamViewModel(
                                    status,
                                    OnDeviceVolumeChanged,
                                    OnDeviceDelayChanged,
                                    SetReferenceEndpoint,
                                    OnRetryEndpointRequested));
                            }
                            OnPropertyChanged(nameof(HasActiveStreams));
                        }

                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.CheckAccess())
                        {
                            await dispatcher.InvokeAsync(AddStreamAction);
                        }
                        else
                        {
                            AddStreamAction();
                        }
                    }
                });
            }
        }
        else if (!isNowSelected && wasSelected)
        {
            var match = SelectedEndpoints.FirstOrDefault(e => e.Id == targetEndpoint.Id);
            if (match != null)
            {
                SelectedEndpoints.Remove(match);
            }
            _logService.LogInfo($"Deselected endpoint: '{targetEndpoint.FriendlyName}'");

            if (_audioEngine.IsRunning)
            {
                _ = Task.Run(async () =>
                {
                    await _audioEngine.DetachDestinationAsync(targetEndpoint.Id);

                    void RemoveStreamAction()
                    {
                        var streamMatch = ActiveStreams.FirstOrDefault(s => s.EndpointId == targetEndpoint.Id);
                        if (streamMatch != null)
                        {
                            ActiveStreams.Remove(streamMatch);
                        }
                        OnPropertyChanged(nameof(HasActiveStreams));
                    }

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        await dispatcher.InvokeAsync(RemoveStreamAction);
                    }
                    else
                    {
                        RemoveStreamAction();
                    }
                });
            }
        }

        if (result == SelectionResult.LimitReached || result == SelectionResult.DeviceUnavailable || result == SelectionResult.NotFound)
        {
            SelectionWarning = message;
            HasSelectionWarning = true;
            _logService.LogWarning($"Selection constraint: {message}");
        }

        UpdateSelectionCounts();
    }

    private void UpdateSelectionCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionCountText));
        OnPropertyChanged(nameof(HasReachedMaxSelection));
        OnPropertyChanged(nameof(IsSourceAlsoSelected));
        OnPropertyChanged(nameof(EligibleMirroredDestinationCount));
        OnPropertyChanged(nameof(SourceSelectionAdviceMessage));
        OnPropertyChanged(nameof(CanStartAudio));
        OnPropertyChanged(nameof(CanStartSharing));
        OnPropertyChanged(nameof(HasActiveStreams));
        OnPropertyChanged(nameof(CanShowStartSharingPrompt));
        OnPropertyChanged(nameof(AudioDevicesStatusTitle));
        OnPropertyChanged(nameof(AudioDevicesStatusDescription));
        OnPropertyChanged(nameof(HasMultipleBluetoothSelected));
        OnPropertyChanged(nameof(MultipleBluetoothWarningMessage));

        UpdateDiagnostics(AvailableEndpoints.Select(vm => vm.Info).ToList(), testedCount: _audioEngine.ActivePlayingCount);
    }

    // --- Mode 1: Test Tone Playback Commands (Chunk 2) ---

    [RelayCommand]
    public async Task StartAudioTestAsync()
    {
        if (SelectedEndpoints.Count == 0)
        {
            SelectionWarning = "Please select at least 1 audio output endpoint before starting the test tone.";
            HasSelectionWarning = true;
            return;
        }

        if (IsSharingAudio)
        {
            await StopSystemAudioSharingAsync();
        }

        IsStartingPlayback = true;
        StatusNotice = $"Starting test tone on {SelectedEndpoints.Count} candidate endpoint(s)...";
        DismissWarning();

        _audioEngine.SampleSource.Frequency = ToneFrequency;
        _audioEngine.SampleSource.Volume = Math.Clamp(MasterVolumePercent / 100.0f, 0.01f, 0.5f);

        var candidateInfos = SelectedEndpoints.Select(vm => vm.Info).ToList();

        try
        {
            var result = await _audioEngine.StartPlaybackAsync(candidateInfos);

            ActiveStreams.Clear();
            foreach (var status in result.StreamStatuses)
            {
                ActiveStreams.Add(new OutputStreamViewModel(
                    status,
                    OnDeviceVolumeChanged,
                    OnDeviceDelayChanged,
                    SetReferenceEndpoint,
                    OnRetryEndpointRequested));
            }

            IsAudioPlaying = result.HasAnyPlaying;
            IsSharingAudio = false;

            if (result.SuccessfullyPlayingCount == result.AttemptedCount && result.AttemptedCount > 0)
            {
                ConcurrencyNotice = $"All {result.SuccessfullyPlayingCount} selected audio stream(s) opened successfully and are rendering test tone.";
                StatusNotice = $"Playing test tone across {result.SuccessfullyPlayingCount} endpoint(s).";
            }
            else if (result.HasAnyPlaying)
            {
                ConcurrencyNotice = $"Partial concurrency: {result.SuccessfullyPlayingCount} stream(s) succeeded, {result.FailedCount} stream(s) failed.";
                StatusNotice = $"Partial playback: {result.SuccessfullyPlayingCount}/{result.AttemptedCount} streams active.";
            }
            else
            {
                ConcurrencyNotice = "None of the selected audio endpoints could be initialized with WASAPI shared mode.";
                StatusNotice = "Playback test failed to start on all selected outputs.";
            }

            UpdateDiagnostics(
                AvailableEndpoints.Select(vm => vm.Info).ToList(),
                testedCount: result.SuccessfullyPlayingCount,
                customDisclaimer: ConcurrencyNotice);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Engine Error] Failed to start audio playback session: {ex.Message}");
            SelectionWarning = $"Failed to start audio playback: {ex.Message}";
            HasSelectionWarning = true;
            IsAudioPlaying = false;
        }
        finally
        {
            IsStartingPlayback = false;
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }
    }

    [RelayCommand]
    public async Task StopAudioTestAsync()
    {
        StatusNotice = "Stopping audio streams...";
        try
        {
            await _audioEngine.StopPlaybackAsync();

            foreach (var streamVm in ActiveStreams)
            {
                streamVm.State = StreamPlaybackState.Stopped;
            }

            IsAudioPlaying = false;
            IsSharingAudio = false;
            StatusNotice = "Audio playback test stopped.";
            ConcurrencyNotice = "Playback stopped. All WASAPI render streams released.";

            UpdateDiagnostics(
                AvailableEndpoints.Select(vm => vm.Info).ToList(),
                testedCount: 0);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Engine Stop Error] Error stopping audio session: {ex.Message}");
        }
        finally
        {
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }
    }

    // --- Mode 2: System Audio Sharing Commands (Chunk 3) ---

    [RelayCommand]
    public async Task StartSystemAudioSharingAsync()
    {
        if (SelectedCaptureSource == null)
        {
            SelectionWarning = "Please select an active Windows audio render endpoint as your capture source.";
            HasSelectionWarning = true;
            return;
        }

        if (EligibleMirroredDestinationCount == 0)
        {
            SelectionWarning = "Select at least 1 other output endpoint to mirror audio to. (The capture source cannot be the only selected endpoint).";
            HasSelectionWarning = true;
            return;
        }

        if (IsAudioPlaying)
        {
            await StopAudioTestAsync();
        }

        IsStartingPlayback = true;
        StatusNotice = $"Initializing system audio capture from '{SelectedCaptureSource.FriendlyName}'...";
        DismissWarning();

        try
        {
            var destinationInfos = SelectedEndpoints.Select(vm => vm.Info).ToList();
            var result = await _audioEngine.StartSystemAudioSharingAsync(SelectedCaptureSource.Info, destinationInfos);

            ActiveStreams.Clear();
            foreach (var status in result.StreamStatuses)
            {
                ActiveStreams.Add(new OutputStreamViewModel(
                    status,
                    OnDeviceVolumeChanged,
                    OnDeviceDelayChanged,
                    SetReferenceEndpoint,
                    OnRetryEndpointRequested));
            }

            IsSharingAudio = _audioEngine.CaptureService.IsCapturing;
            IsAudioPlaying = false;
            CaptureStatus = _audioEngine.CaptureService.Status;

            int mirroredSuccessCount = result.StreamStatuses.Count(s => !s.IsNativeSource && s.IsPlaying);

            if (mirroredSuccessCount > 0)
            {
                ConcurrencyNotice = $"System audio active: Capturing from '{SelectedCaptureSource.FriendlyName}'. Mirroring to {mirroredSuccessCount} destination(s). Source endpoint {(IsSourceAlsoSelected ? "playing natively" : "operating via default Windows routing")}.";
                StatusNotice = $"Sharing live system audio to {mirroredSuccessCount} output(s).";
            }
            else
            {
                ConcurrencyNotice = "System audio sharing could not open mirrored render streams.";
                StatusNotice = "Failed to open destination streams.";
            }

            UpdateDiagnostics(
                AvailableEndpoints.Select(vm => vm.Info).ToList(),
                testedCount: result.SuccessfullyPlayingCount,
                customDisclaimer: ConcurrencyNotice);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[System Audio Share Error] {ex.Message}");
            SelectionWarning = $"System audio sharing failed: {ex.Message}";
            HasSelectionWarning = true;
            IsSharingAudio = false;
        }
        finally
        {
            IsStartingPlayback = false;
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }
    }

    [RelayCommand]
    public async Task StopSystemAudioSharingAsync()
    {
        StatusNotice = "Stopping system audio sharing...";
        try
        {
            await _audioEngine.StopPlaybackAsync();

            foreach (var streamVm in ActiveStreams)
            {
                streamVm.State = StreamPlaybackState.Stopped;
            }

            IsSharingAudio = false;
            IsAudioPlaying = false;
            CaptureStatus = _audioEngine.CaptureService.Status;
            StatusNotice = "System audio sharing stopped.";
            ConcurrencyNotice = "System audio sharing stopped. WASAPI loopback and output streams released.";

            UpdateDiagnostics(
                AvailableEndpoints.Select(vm => vm.Info).ToList(),
                testedCount: 0);
        }
        catch (Exception ex)
        {
            _logService.LogError($"[System Audio Stop Error] Error stopping system audio: {ex.Message}");
        }
        finally
        {
            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }
    }

    private void OnCaptureStatusChanged(object? sender, SystemAudioCaptureStatus status)
    {
        void UpdateUi()
        {
            CaptureStatus = status;
            IsSharingAudio = status.IsActive;
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => UpdateUi(), null);
        }
        else
        {
            UpdateUi();
        }
    }

    private void OnEngineStreamStatusChanged(object? sender, OutputStreamStatus status)
    {
        void UpdateUi()
        {
            var match = ActiveStreams.FirstOrDefault(s => s.EndpointId == status.EndpointId);
            if (match != null)
            {
                match.Update(status);
            }
            else
            {
                ActiveStreams.Add(new OutputStreamViewModel(
                    status,
                    OnDeviceVolumeChanged,
                    OnDeviceDelayChanged,
                    SetReferenceEndpoint,
                    OnRetryEndpointRequested));
            }

            int playingCount = _audioEngine.ActivePlayingCount;
            if (_audioEngine.ActiveMode == AudioEngineMode.SystemAudio)
            {
                IsSharingAudio = playingCount > 0;
            }
            else if (_audioEngine.ActiveMode == AudioEngineMode.TestTone)
            {
                IsAudioPlaying = playingCount > 0;
            }

            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => UpdateUi(), null);
        }
        else
        {
            UpdateUi();
        }
    }

    private void OnEnginePlaybackStateChanged(object? sender, bool isPlaying)
    {
        void UpdateUi()
        {
            if (_audioEngine.ActiveMode == AudioEngineMode.SystemAudio)
            {
                IsSharingAudio = isPlaying;
                IsAudioPlaying = false;
            }
            else if (_audioEngine.ActiveMode == AudioEngineMode.TestTone)
            {
                IsAudioPlaying = isPlaying;
                IsSharingAudio = false;
            }
            else
            {
                IsAudioPlaying = false;
                IsSharingAudio = false;
            }

            OnPropertyChanged(nameof(CanStartAudio));
            OnPropertyChanged(nameof(CanStopAudio));
            OnPropertyChanged(nameof(CanStartSharing));
            OnPropertyChanged(nameof(CanStopSharing));
        }

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => UpdateUi(), null);
        }
        else
        {
            UpdateUi();
        }
    }

    private void UpdateDiagnostics(IReadOnlyList<AudioEndpointInfo> endpoints, int testedCount = 0, string? customDisclaimer = null)
    {
        Diagnostics = _diagnosticsService.GetDiagnostics(
            endpoints,
            _selectionManager.SelectedCount,
            testedCount,
            customDisclaimer);
    }

    [RelayCommand]
    public void DismissWarning()
    {
        SelectionWarning = null;
        HasSelectionWarning = false;
    }

    [RelayCommand]
    public void ClearLogs()
    {
        _logService.Clear();
    }

    [RelayCommand]
    public void CopyDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== SyncCast System & Audio Diagnostics ===");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {WindowsVersionInfo}");
        sb.AppendLine($"Runtime: {DotNetVersionInfo}");
        sb.AppendLine($"App Version: {AppVersion}");
        sb.AppendLine($"Engine Mode: {_audioEngine.ActiveMode}");
        sb.AppendLine($"Master Volume: {MasterVolume:F0}% (Muted: {IsMasterMuted})");
        sb.AppendLine($"Auto-Reconnect: {AutoReconnect}");
        sb.AppendLine($"Reference Endpoint: {ReferenceEndpointName}");
        sb.AppendLine();
        sb.AppendLine("--- Capture Service Status ---");
        sb.AppendLine($"Capture State: {CaptureStatus.State}");
        sb.AppendLine($"Capture Source: {CaptureStatus.SourceEndpointName ?? "None"}");
        sb.AppendLine($"Capture Format: {CaptureStatus.CaptureFormatText}");
        sb.AppendLine($"Bytes Captured: {CaptureStatus.BytesCaptured:N0}");
        sb.AppendLine();
        sb.AppendLine("--- Active Output Streams ---");
        if (ActiveStreams.Count == 0)
        {
            sb.AppendLine("No active output streams.");
        }
        else
        {
            foreach (var stream in ActiveStreams)
            {
                sb.AppendLine($"• {stream.EndpointName} [{stream.Transport}]");
                sb.AppendLine($"    State: {stream.State} | Format: {stream.TargetFormat}");
                sb.AppendLine($"    Volume: {stream.DeviceVolume:F0}% (Muted: {stream.IsMuted}) | Effective Gain: {stream.EffectiveGainPercent:F0}%");
                sb.AppendLine($"    Sync Delay: {stream.SyncDelayMs:F1}ms | Buffer Occupancy: {stream.BufferOccupancyMs:F1}ms | Overflows: {stream.BufferOverflowDropCount}");
                sb.AppendLine($"    Frames: {stream.FramesProcessed:N0} | Drift: {stream.EstimatedDriftMs:F1}ms | Sync Ref: {stream.IsSyncReference}");
                if (!string.IsNullOrEmpty(stream.ErrorMessage))
                {
                    sb.AppendLine($"    Error: {stream.ErrorMessage}");
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine("--- Recent Diagnostic Logs (Last 50) ---");
        var recentLogs = DiagnosticLogs.TakeLast(50);
        foreach (var log in recentLogs)
        {
            sb.AppendLine($"[{log.Timestamp:HH:mm:ss.fff}] [{log.Level}] {log.Message}");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            StatusNotice = "Diagnostics copied to clipboard.";
            _logService.LogInfo("Diagnostics summary copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"Failed to copy diagnostics to clipboard: {ex.Message}");
        }
    }
}
