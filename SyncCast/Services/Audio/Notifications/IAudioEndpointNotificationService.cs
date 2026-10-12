using NAudio.CoreAudioApi;

namespace SyncCast.Services.Audio.Notifications;

public interface IAudioEndpointNotificationService : IDisposable
{
    event EventHandler<(string EndpointId, DeviceState NewState)>? EndpointStateChanged;
    event EventHandler<string>? EndpointAdded;
    event EventHandler<string>? EndpointRemoved;
    event EventHandler<string>? DefaultEndpointChanged;

    void StartListening();
    void StopListening();
}
