namespace SyncCast.Models;

/// <summary>
/// Lifecycle state of an individual audio render stream.
/// </summary>
public enum StreamPlaybackState
{
    Stopped,
    Initializing,
    Playing,
    NativeSourcePlaying, // Bypassed mirrored stream; playing natively via Windows
    Disconnected,        // Device disconnected or unplugged while stream was active
    Recovering,          // Background recovery / reconnect in progress with backoff
    Failed
}

/// <summary>
/// Status and health diagnostics for a single endpoint's active render stream.
/// </summary>
public record OutputStreamStatus
{
    public required string EndpointId { get; init; }
    public required string EndpointName { get; init; }
    public DeviceTransportType Transport { get; init; }
    public StreamPlaybackState State { get; init; } = StreamPlaybackState.Stopped;
    public string TargetFormat { get; init; } = "Format Pending";
    public int LatencyMs { get; init; } = 100;
    public long FramesPlayed { get; init; }
    public bool IsNativeSource { get; init; }
    public bool HasRenderedAudioData { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime LastUpdated { get; init; } = DateTime.Now;

    // Per-device volume controls (0–100%)
    public double DeviceVolume { get; init; } = 100.0;
    public bool IsMuted { get; init; } = false;
    public double EffectiveGainPercent { get; init; } = 100.0;

    // Audio Health & Latency Diagnostics
    public long BytesWritten { get; init; }
    public long FramesProcessed { get; init; }
    public int BufferOccupancyBytes { get; init; }
    public double BufferOccupancyMs { get; init; }
    public int BufferCapacityBytes { get; init; }
    public long BufferOverflowDropCount { get; init; }
    public long BufferUnderrunCount { get; init; }
    public DateTime? LastAudioWriteTime { get; init; }
    public double EstimatedBufferDelayMs { get; init; }
    public string AcousticLatencyText { get; init; } = "Unknown (Hardware/Bluetooth dependent)";

    // Chunk 5: Multi-Output Audio Synchronization & Drift
    public double SyncDelayMs { get; init; } = 0.0;
    public bool IsSyncReference { get; init; } = false;
    public double EstimatedDriftMs { get; init; } = 0.0;
    public long LastBlockSequence { get; init; } = 0;

    public bool IsPlaying => State == StreamPlaybackState.Playing || State == StreamPlaybackState.NativeSourcePlaying;
    public bool HasFailed => State == StreamPlaybackState.Failed || State == StreamPlaybackState.Disconnected;
    public bool IsRecovering => State == StreamPlaybackState.Recovering;
}

/// <summary>
/// Summary result of attempting to start multi-output playback across candidate endpoints.
/// Never counts selected endpoints as successful playback.
/// </summary>
public record MultiOutputPlaybackResult
{
    public int AttemptedCount { get; init; }
    public int SuccessfullyPlayingCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<OutputStreamStatus> StreamStatuses { get; init; } = Array.Empty<OutputStreamStatus>();

    public bool HasAnyPlaying => SuccessfullyPlayingCount > 0;
    public bool AllSucceeded => AttemptedCount > 0 && SuccessfullyPlayingCount == AttemptedCount;
}
