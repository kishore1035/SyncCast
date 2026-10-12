namespace SyncCast.Models;

/// <summary>
/// Hardware, runtime, and endpoint diagnostics.
/// Honest reporting: Endpoint discovery alone does not prove concurrent playback capability.
/// </summary>
public record DiagnosticInfo
{
    public required string WindowsVersion { get; init; }
    public required string DotNetVersion { get; init; }
    public int ActiveRenderEndpointsCount { get; init; }
    public int SelectedEndpointsCount { get; init; }
    public int LikelyBluetoothEndpointsCount { get; init; }
    public int TestedEndpointsCount { get; init; } = 0; // Stage 1 explicitly has 0 tested endpoints
    public required string BluetoothDisclaimer { get; init; }
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public record LogMessage
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public required string Message { get; init; }

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
}
