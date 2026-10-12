using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Logging;
using CaptureState = SyncCast.Models.CaptureState;

namespace SyncCast.Services.Audio.Capture;

public class AudioDataChunkEventArgs : EventArgs
{
    public byte[] Buffer { get; }
    public int BytesRecorded { get; }
    public WaveFormat Format { get; }

    public AudioDataChunkEventArgs(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        Buffer = buffer;
        BytesRecorded = bytesRecorded;
        Format = format;
    }
}

public interface ISystemAudioCaptureService : IDisposable
{
    CaptureState State { get; }
    SystemAudioCaptureStatus Status { get; }
    WaveFormat? CaptureFormat { get; }
    bool IsCapturing { get; }

    event EventHandler<AudioDataChunkEventArgs>? DataAvailable;
    event EventHandler<SystemAudioCaptureStatus>? StatusChanged;

    Task<bool> StartCaptureAsync(AudioEndpointInfo sourceEndpoint, CancellationToken cancellationToken = default);
    Task StopCaptureAsync();
}

public class SystemAudioCaptureService : ISystemAudioCaptureService
{
    private readonly ILogService _logService;
    private readonly object _syncLock = new();

    private WasapiLoopbackCapture? _capture;
    private MMDevice? _sourceDevice;
    private AudioEndpointInfo? _currentSource;
    private CaptureState _state = CaptureState.Stopped;
    private string? _errorMessage;
    private long _bytesCaptured = 0;
    private bool _hasReceivedData = false;
    private WaveFormat? _captureFormat;
    private bool _disposed = false;

    public CaptureState State => _state;
    public WaveFormat? CaptureFormat => _captureFormat;
    public bool IsCapturing => _state == CaptureState.Running;

    public SystemAudioCaptureStatus Status => new()
    {
        State = _state,
        SourceEndpointId = _currentSource?.Id,
        SourceEndpointName = _currentSource?.FriendlyName,
        CaptureFormatText = _captureFormat != null ? $"{_captureFormat.SampleRate:N0} Hz • {_captureFormat.Channels} Ch • {_captureFormat.BitsPerSample}-bit ({_captureFormat.Encoding})" : "None",
        BytesCaptured = _bytesCaptured,
        HasReceivedAudioData = _hasReceivedData,
        ErrorMessage = _errorMessage,
        LastActivity = DateTime.Now
    };

    public event EventHandler<AudioDataChunkEventArgs>? DataAvailable;
    public event EventHandler<SystemAudioCaptureStatus>? StatusChanged;

    public SystemAudioCaptureService(ILogService logService)
    {
        _logService = logService;
    }

    public async Task<bool> StartCaptureAsync(AudioEndpointInfo sourceEndpoint, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            lock (_syncLock)
            {
                if (_disposed)
                {
                    _errorMessage = "Capture service is disposed.";
                    SetState(CaptureState.Failed);
                    return false;
                }

                // Prevent duplicate capture sessions
                if (_state == CaptureState.Running)
                {
                    _logService.LogWarning("[Capture] A system audio capture session is already active.");
                    return true;
                }

                SetState(CaptureState.Starting);
                _errorMessage = null;
                _bytesCaptured = 0;
                _hasReceivedData = false;
                _currentSource = sourceEndpoint;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sourceEndpoint == null)
                {
                    throw new ArgumentNullException(nameof(sourceEndpoint), "Source endpoint cannot be null.");
                }

                if (sourceEndpoint.State != EndpointActiveState.Active)
                {
                    throw new InvalidOperationException($"Source endpoint '{sourceEndpoint.FriendlyName}' is not in Active state ({sourceEndpoint.State}).");
                }

                // 1. Resolve MMDevice for the requested render endpoint
                using var enumerator = new MMDeviceEnumerator();
                MMDevice device;
                try
                {
                    device = enumerator.GetDevice(sourceEndpoint.Id);
                }
                catch (COMException ex)
                {
                    throw new InvalidOperationException($"Cannot open capture endpoint '{sourceEndpoint.FriendlyName}' (COM Error 0x{ex.ErrorCode:X8}): {ex.Message}", ex);
                }

                if (device.State != DeviceState.Active)
                {
                    device.Dispose();
                    throw new InvalidOperationException($"Endpoint '{sourceEndpoint.FriendlyName}' is not currently active in Windows.");
                }

                _sourceDevice = device;

                // 2. Initialize WASAPI Loopback Capture targeting this specific render endpoint
                var loopback = new WasapiLoopbackCapture(device);
                _captureFormat = loopback.WaveFormat;
                _logService.LogInfo($"[Capture Init] Loopback format for '{sourceEndpoint.FriendlyName}': {_captureFormat.SampleRate} Hz, {_captureFormat.Channels} Ch, {_captureFormat.BitsPerSample}-bit ({_captureFormat.Encoding}).");

                loopback.DataAvailable += OnDataAvailable;
                loopback.RecordingStopped += OnRecordingStopped;

                // 3. Begin loopback recording
                loopback.StartRecording();
                _capture = loopback;

                lock (_syncLock)
                {
                    SetState(CaptureState.Running);
                }

                _logService.LogSuccess($"[Capture Online] System audio loopback capture active on '{sourceEndpoint.FriendlyName}'.");
                return true;
            }
            catch (OperationCanceledException)
            {
                lock (_syncLock)
                {
                    CleanupCapture();
                    SetState(CaptureState.Stopped);
                }
                return false;
            }
            catch (Exception ex)
            {
                lock (_syncLock)
                {
                    _errorMessage = ex.Message;
                    CleanupCapture();
                    SetState(CaptureState.Failed);
                }

                _logService.LogError($"[Capture Error] Failed to start loopback capture on '{sourceEndpoint.FriendlyName}': {ex.Message}");
                return false;
            }
        }, cancellationToken);
    }

    public async Task StopCaptureAsync()
    {
        await Task.Run(() =>
        {
            lock (_syncLock)
            {
                if (_state == CaptureState.Stopped) return;

                CleanupCapture();
                SetState(CaptureState.Stopped);
            }

            _logService.LogInfo("[Capture Stopped] System audio loopback capture released.");
        });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _captureFormat == null) return;

        Interlocked.Add(ref _bytesCaptured, e.BytesRecorded);

        if (!_hasReceivedData)
        {
            _hasReceivedData = true;
            _logService.LogSuccess($"[Capture Streaming] First audio data buffer received from '{_currentSource?.FriendlyName}' ({e.BytesRecorded} bytes).");
            StatusChanged?.Invoke(this, Status);
        }

        // Fire audio chunk event to fan-out subscribers
        DataAvailable?.Invoke(this, new AudioDataChunkEventArgs(e.Buffer, e.BytesRecorded, _captureFormat));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncLock)
        {
            if (_state == CaptureState.Running)
            {
                if (e.Exception != null)
                {
                    _errorMessage = $"Capture source terminated unexpectedly: {e.Exception.Message}";
                    SetState(CaptureState.Failed);
                    _logService.LogError($"[Capture Disconnected] Loopback capture failed: {e.Exception.Message}");
                }
                else
                {
                    SetState(CaptureState.Stopped);
                }
            }
        }
    }

    private void CleanupCapture()
    {
        if (_capture != null)
        {
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch
            {
            }
            finally
            {
                _capture = null;
            }
        }

        if (_sourceDevice != null)
        {
            try
            {
                _sourceDevice.Dispose();
            }
            catch
            {
            }
            finally
            {
                _sourceDevice = null;
            }
        }
    }

    private void SetState(CaptureState newState)
    {
        _state = newState;
        StatusChanged?.Invoke(this, Status);
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                CleanupCapture();
                _state = CaptureState.Stopped;
            }
        }
    }
}
