namespace SyncCast.Models;

/// <summary>
/// Operational state of the Windows audio render endpoint as reported by MMDevice API.
/// </summary>
public enum EndpointActiveState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
    Unknown
}
