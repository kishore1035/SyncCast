using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncCast.Models;

namespace SyncCast.ViewModels;

public partial class OutputStreamViewModel : ObservableObject
{
    private readonly Action<string, double, bool>? _onVolumeChanged;
    private readonly Action<string, double>? _onDelayChanged;
    private readonly Action<string>? _onSetAsReference;
    private readonly Func<string, Task>? _onRetryRequested;

    [ObservableProperty]
    private string _endpointId;

    [ObservableProperty]
    private string _endpointName;

    [ObservableProperty]
    private DeviceTransportType _transport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(StateBadgeText))]
    private StreamPlaybackState _state;

    [ObservableProperty]
    private string _targetFormat;

    [ObservableProperty]
    private int _latencyMs;

    [ObservableProperty]
    private bool _isNativeSource;

    [ObservableProperty]
    private bool _hasRenderedAudioData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string? _errorMessage;

    // Per-Device Volume & Mute (0–100%)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _deviceVolume = 100.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteButtonText))]
    private bool _isMuted = false;

    [ObservableProperty]
    private double _effectiveGainPercent = 100.0;

    // Chunk 5: Per-Device Synchronization & Drift
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncDelayText))]
    private double _syncDelayMs = 0.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReferenceBadgeText))]
    [NotifyPropertyChangedFor(nameof(IsSecondaryOutput))]
    private bool _isSyncReference = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriftText))]
    private double _estimatedDriftMs = 0.0;

    [ObservableProperty]
    private long _lastBlockSequence = 0;

    // Audio Health & Latency Metrics
    [ObservableProperty]
    private double _bufferOccupancyMs = 0.0;

    [ObservableProperty]
    private int _bufferOccupancyBytes = 0;

    [ObservableProperty]
    private int _bufferCapacityBytes = 0;

    [ObservableProperty]
    private long _bufferOverflowDropCount = 0;

    [ObservableProperty]
    private long _framesProcessed = 0;

    [ObservableProperty]
    private long _bytesWritten = 0;

    [ObservableProperty]
    private double _estimatedBufferDelayMs = 0.0;

    [ObservableProperty]
    private string _acousticLatencyText = "Unknown (Hardware/Bluetooth dependent)";

    [ObservableProperty]
    private string _lastAudioWriteText = "Never";

    public bool IsPlaying => State == StreamPlaybackState.Playing || State == StreamPlaybackState.NativeSourcePlaying;
    public bool HasError => State == StreamPlaybackState.Failed || State == StreamPlaybackState.Disconnected;
    public bool CanRetry => State == StreamPlaybackState.Failed || State == StreamPlaybackState.Disconnected;

    public string VolumeText => IsMuted ? "MUTED" : $"{DeviceVolume:F0}%";
    public string MuteButtonText => IsMuted ? "Unmute" : "Mute";

    public string SyncDelayText => $"{SyncDelayMs:F0} ms";
    public string ReferenceBadgeText => IsSyncReference ? "Timing Reference" : "Secondary Output";
    public bool IsSecondaryOutput => !IsSyncReference;
    public string DriftText => $"{EstimatedDriftMs:+0.0;-0.0;0.0} ms";

    public string StateBadgeText => State switch
    {
        StreamPlaybackState.NativeSourcePlaying => "Native Source (Windows)",
        StreamPlaybackState.Playing => "Playing",
        StreamPlaybackState.Initializing => "Initializing...",
        StreamPlaybackState.Disconnected => "Disconnected",
        StreamPlaybackState.Recovering => "Recovering...",
        StreamPlaybackState.Failed => "Failed",
        _ => "Stopped"
    };

    public OutputStreamViewModel(
        OutputStreamStatus status,
        Action<string, double, bool>? onVolumeChanged = null,
        Action<string, double>? onDelayChanged = null,
        Action<string>? onSetAsReference = null,
        Func<string, Task>? onRetryRequested = null)
    {
        _endpointId = status.EndpointId;
        _endpointName = status.EndpointName;
        _transport = status.Transport;
        _state = status.State;
        _targetFormat = status.TargetFormat;
        _latencyMs = status.LatencyMs;
        _isNativeSource = status.IsNativeSource;
        _hasRenderedAudioData = status.HasRenderedAudioData;
        _errorMessage = status.ErrorMessage;

        _deviceVolume = status.DeviceVolume;
        _isMuted = status.IsMuted;
        _effectiveGainPercent = status.EffectiveGainPercent;

        _syncDelayMs = status.SyncDelayMs;
        _isSyncReference = status.IsSyncReference;
        _estimatedDriftMs = status.EstimatedDriftMs;
        _lastBlockSequence = status.LastBlockSequence;

        _onVolumeChanged = onVolumeChanged;
        _onDelayChanged = onDelayChanged;
        _onSetAsReference = onSetAsReference;
        _onRetryRequested = onRetryRequested;

        UpdateHealthMetrics(status);
    }

    partial void OnDeviceVolumeChanged(double value)
    {
        _onVolumeChanged?.Invoke(EndpointId, value, IsMuted);
    }

    partial void OnSyncDelayMsChanged(double value)
    {
        _onDelayChanged?.Invoke(EndpointId, value);
    }

    [RelayCommand]
    private void NudgeDelay(string deltaStr)
    {
        if (double.TryParse(deltaStr, out double delta))
        {
            SyncDelayMs = Math.Clamp(SyncDelayMs + delta, 0.0, 1000.0);
        }
    }

    [RelayCommand]
    private void ResetDelay()
    {
        SyncDelayMs = 0.0;
    }

    [RelayCommand]
    private void SetAsReference()
    {
        _onSetAsReference?.Invoke(EndpointId);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _onVolumeChanged?.Invoke(EndpointId, DeviceVolume, IsMuted);
    }

    [RelayCommand]
    private async Task Retry()
    {
        if (_onRetryRequested != null)
        {
            await _onRetryRequested(EndpointId);
        }
    }

    public void Update(OutputStreamStatus status)
    {
        EndpointName = status.EndpointName;
        Transport = status.Transport;
        State = status.State;
        TargetFormat = status.TargetFormat;
        LatencyMs = status.LatencyMs;
        IsNativeSource = status.IsNativeSource;
        HasRenderedAudioData = status.HasRenderedAudioData;
        ErrorMessage = status.ErrorMessage;

        EffectiveGainPercent = status.EffectiveGainPercent;
        SyncDelayMs = status.SyncDelayMs;
        IsSyncReference = status.IsSyncReference;
        EstimatedDriftMs = status.EstimatedDriftMs;
        LastBlockSequence = status.LastBlockSequence;

        UpdateHealthMetrics(status);
    }

    private void UpdateHealthMetrics(OutputStreamStatus status)
    {
        BufferOccupancyMs = status.BufferOccupancyMs;
        BufferOccupancyBytes = status.BufferOccupancyBytes;
        BufferCapacityBytes = status.BufferCapacityBytes;
        BufferOverflowDropCount = status.BufferOverflowDropCount;
        FramesProcessed = status.FramesProcessed;
        BytesWritten = status.BytesWritten;
        EstimatedBufferDelayMs = status.EstimatedBufferDelayMs;
        AcousticLatencyText = status.AcousticLatencyText;

        if (status.LastAudioWriteTime.HasValue)
        {
            LastAudioWriteText = status.LastAudioWriteTime.Value.ToLocalTime().ToString("HH:mm:ss.fff");
        }
    }
}
