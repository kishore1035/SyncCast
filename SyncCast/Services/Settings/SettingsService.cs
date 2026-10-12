using System.IO;
using System.Text.Json;
using SyncCast.Services.Logging;

namespace SyncCast.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogService _logService;
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(ILogService logService, string? customFilePath = null)
    {
        _logService = logService;

        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "SyncCast");
            _settingsFilePath = Path.Combine(appDir, "settings.json");
        }
    }

    public async Task<SyncCastSettings> LoadSettingsAsync()
    {
        return await Task.Run(() =>
        {
            lock (_fileLock)
            {
                try
                {
                    if (!File.Exists(_settingsFilePath))
                    {
                        _logService.LogInfo("[Settings] No existing settings file found. Using default preferences.");
                        return new SyncCastSettings();
                    }

                    string json = File.ReadAllText(_settingsFilePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return new SyncCastSettings();
                    }

                    var loaded = JsonSerializer.Deserialize<SyncCastSettings>(json, JsonOptions);
                    if (loaded == null)
                    {
                        _logService.LogWarning("[Settings] Deserialization produced null settings. Falling back to defaults.");
                        return new SyncCastSettings();
                    }

                    // Sanitize values
                    loaded.MasterVolumePercent = Math.Clamp(loaded.MasterVolumePercent, 0.0, 100.0);
                    if (string.IsNullOrWhiteSpace(loaded.ReferenceEndpointId))
                    {
                        loaded.ReferenceEndpointId = null;
                    }
                    loaded.DeviceSettings ??= new();
                    foreach (var dev in loaded.DeviceSettings.Values)
                    {
                        dev.VolumePercent = Math.Clamp(dev.VolumePercent, 0.0, 100.0);
                        dev.SyncDelayMs = Math.Clamp(dev.SyncDelayMs, 0.0, 1000.0);
                    }

                    _logService.LogInfo($"[Settings] Preferences loaded successfully from '{_settingsFilePath}'.");
                    return loaded;
                }
                catch (Exception ex)
                {
                    _logService.LogWarning($"[Settings] Failed to load settings ({ex.Message}). Safe defaults applied.");
                    return new SyncCastSettings();
                }
            }
        });
    }

    public async Task SaveSettingsAsync(SyncCastSettings settings)
    {
        if (settings == null) return;

        await Task.Run(() =>
        {
            lock (_fileLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(_settingsFilePath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string tempFile = _settingsFilePath + ".tmp";
                    string json = JsonSerializer.Serialize(settings, JsonOptions);
                    File.WriteAllText(tempFile, json);

                    if (File.Exists(_settingsFilePath))
                    {
                        File.Replace(tempFile, _settingsFilePath, null);
                    }
                    else
                    {
                        File.Move(tempFile, _settingsFilePath);
                    }

                    _logService.LogInfo("[Settings] Preferences saved cleanly to local storage.");
                }
                catch (Exception ex)
                {
                    _logService.LogWarning($"[Settings] Could not save settings: {ex.Message}");
                }
            }
        });
    }
}
