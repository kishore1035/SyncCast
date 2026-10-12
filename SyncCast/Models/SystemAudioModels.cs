namespace SyncCast.Models;

/// <summary>
/// Status of the WASAPI loopback system audio capture engine.
/// </summary>
public enum CaptureState
{
    Stopped,
    Starting,
    Running,
    Failed
}

/// <summary>
/// Active operational mode of the audio output engine.
/// Modes are mutually exclusive.
/// </summary>
public enum AudioEngineMode
{
    Idle,
    TestTone,
    SystemAudio,
    CalibrationPulse
}

/// <summary>
/// Diagnostic snapshot of the system-audio capture session.
/// </summary>
public record SystemAudioCaptureStatus
{
    public CaptureState State { get; init; } = CaptureState.Stopped;
    public string? SourceEndpointId { get; init; }
    public string? SourceEndpointName { get; init; }
    public string? CaptureFormatText { get; init; }
    public long BytesCaptured { get; init; }
    public long FramesCaptured { get; init; }
    public bool HasReceivedAudioData { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime LastActivity { get; init; } = DateTime.Now;

    // Aggregated Health Metrics
    public double EstimatedCaptureToOutputDelayMs { get; init; }
    public int TotalActiveDestinations { get; init; }
    public int TotalHealthyDestinations { get; init; }
    public long TotalDroppedFrames { get; init; }

    public bool IsActive => State == CaptureState.Running;
}
