using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SyncCast.Services.Logging;

namespace SyncCast.Services.Audio.Notifications;

public class AudioEndpointNotificationService : IAudioEndpointNotificationService, IMMNotificationClient
{
    private readonly ILogService _logService;
    private readonly object _syncLock = new();
    private MMDeviceEnumerator? _deviceEnumerator;
    private bool _isListening = false;
    private bool _disposed = false;

    public event EventHandler<(string EndpointId, DeviceState NewState)>? EndpointStateChanged;
    public event EventHandler<string>? EndpointAdded;
    public event EventHandler<string>? EndpointRemoved;
    public event EventHandler<string>? DefaultEndpointChanged;

    public AudioEndpointNotificationService(ILogService logService)
    {
        _logService = logService;
    }

    public void StartListening()
    {
        lock (_syncLock)
        {
            if (_isListening || _disposed) return;

            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _deviceEnumerator.RegisterEndpointNotificationCallback(this);
                _isListening = true;
                _logService.LogInfo("[Device Notifications] Registered Windows Core Audio endpoint notification listener.");
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"[Device Notifications] Could not register Core Audio notification callback: {ex.Message}");
            }
        }
    }

    public void StopListening()
    {
        lock (_syncLock)
        {
            if (!_isListening) return;

            try
            {
                if (_deviceEnumerator != null)
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
                    _deviceEnumerator.Dispose();
                    _deviceEnumerator = null;
                }
            }
            catch
            {
                // Unregistration best-effort during shutdown
            }
            finally
            {
                _isListening = false;
            }
        }
    }

    // IMMNotificationClient Implementation
    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _logService.LogInfo($"[Device State] Endpoint '{deviceId}' state changed to {newState}.");
        EndpointStateChanged?.Invoke(this, (deviceId, newState));
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        _logService.LogInfo($"[Device Added] Endpoint '{pwstrDeviceId}' was connected.");
        EndpointAdded?.Invoke(this, pwstrDeviceId);
    }

    public void OnDeviceRemoved(string deviceId)
    {
        _logService.LogInfo($"[Device Removed] Endpoint '{deviceId}' was disconnected or unplugged.");
        EndpointRemoved?.Invoke(this, deviceId);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            _logService.LogInfo($"[Default Device Changed] Default multimedia render device is now '{defaultDeviceId}'.");
            DefaultEndpointChanged?.Invoke(this, defaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property change (e.g. friendly name or format update)
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                StopListening();
            }
        }
    }
}
