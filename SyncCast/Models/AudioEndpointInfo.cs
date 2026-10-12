namespace SyncCast.Models;

/// <summary>
/// Immutable representation of an audio render endpoint enumerated from Windows Core Audio APIs.
/// </summary>
public record AudioEndpointInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public string DeviceDescription { get; init; } = string.Empty;
    public EndpointActiveState State { get; init; }
    public DeviceTransportType Transport { get; init; }
    public string TransportReason { get; init; } = string.Empty;
    public bool IsDefaultEndpoint { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public int? BitsPerSample { get; init; }
    public string? PnpDeviceId { get; init; }

    public bool IsActive => State == EndpointActiveState.Active;

    public string FormatSummary
    {
        get
        {
            if (SampleRate.HasValue && Channels.HasValue)
            {
                var chText = Channels.Value switch
                {
                    1 => "Mono",
                    2 => "Stereo (2 ch)",
                    6 => "5.1 Surround (6 ch)",
                    8 => "7.1 Surround (8 ch)",
                    _ => $"{Channels.Value} ch"
                };

                var bitText = BitsPerSample.HasValue ? $", {BitsPerSample.Value}-bit" : string.Empty;
                return $"{SampleRate.Value:N0} Hz • {chText}{bitText}";
            }

            return IsActive ? "Standard Core Audio Format" : "Offline / Unplugged";
        }
    }
}
