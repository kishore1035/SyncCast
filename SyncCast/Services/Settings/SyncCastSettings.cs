namespace SyncCast.Services.Settings;

public class DeviceVolumeSetting
{
    public string DeviceFriendlyName { get; set; } = string.Empty;
    public double VolumePercent { get; set; } = 100.0;
    public bool IsMuted { get; set; } = false;
    public double SyncDelayMs { get; set; } = 0.0;
}

public class SyncCastSettings
{
    public double MasterVolumePercent { get; set; } = 100.0;
    public bool IsMasterMuted { get; set; } = false;
    public bool AutoReconnectEnabled { get; set; } = true;
    public string? LastCaptureSourceId { get; set; }
    public string? ReferenceEndpointId { get; set; }
    public bool DriftMonitoringEnabled { get; set; } = true;
    public string ThemeMode { get; set; } = "Dark";
    public Dictionary<string, DeviceVolumeSetting> DeviceSettings { get; set; } = new();
}
