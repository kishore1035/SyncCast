namespace SyncCast.Models;

/// <summary>
/// Identifies the transport interface of an audio endpoint based on verifiable metadata.
/// If transport cannot be verified reliably, it must be marked as Unknown.
/// </summary>
public enum DeviceTransportType
{
    /// <summary>
    /// Device has verifiable indicators of Bluetooth transport (e.g. BTHENUM, Bluetooth A2DP, Hands-Free).
    /// </summary>
    Bluetooth,

    /// <summary>
    /// Internal PC audio, wired analog (3.5mm), USB DAC, or display audio (HDMI/DisplayPort).
    /// </summary>
    BuiltInOrWired,

    /// <summary>
    /// Transport interface could not be reliably determined from available Windows Core Audio properties.
    /// Honest reporting rather than guessing.
    /// </summary>
    Unknown
}
