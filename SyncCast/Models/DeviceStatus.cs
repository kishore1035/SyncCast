namespace SyncCast.Models;

/// <summary>
/// Status states for device endpoints in the application.
/// </summary>
public enum DeviceStatus
{
    /// <summary>
    /// Device is active and candidate for output selection.
    /// </summary>
    Available,

    /// <summary>
    /// Device has been selected as one of the target outputs (up to 5).
    /// </summary>
    Selected,

    /// <summary>
    /// Device is disabled, unplugged, or not present according to Windows.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Playback has not yet been tested on this device.
    /// </summary>
    PlaybackNotTested
}

/// <summary>
/// Outcome of attempting to select or deselect a device.
/// </summary>
public enum SelectionResult
{
    Selected,
    Deselected,
    LimitReached,
    DeviceUnavailable,
    NotFound
}
