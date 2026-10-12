namespace SyncCast.Services.Settings;

public interface ISettingsService
{
    Task<SyncCastSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(SyncCastSettings settings);
}
