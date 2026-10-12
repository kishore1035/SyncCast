using System.Buffers;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Logging;

namespace SyncCast.Services.Audio.Engine;

public interface IEndpointAudioStream : IDisposable
{
    string EndpointId { get; }
    string EndpointName { get; }
    DeviceTransportType Transport { get; }
    StreamPlaybackState State { get; }
    OutputStreamStatus Status { get; }
    string? ErrorMessage { get; }
    bool IsNativeSource { get; }
    bool HasRenderedAudioData { get; }

    double DeviceVolumePercent { get; }
    bool IsMuted { get; }
    double EffectiveGainPercent { get; }

    // Chunk 5: Multi-Output Audio Synchronization
    double SyncDelayMs { get; }
    bool IsSyncReference { get; set; }

    event EventHandler<OutputStreamStatus>? StatusChanged;

    void SetVolume(double deviceVolumePercent, bool isMuted, double masterVolumePercent, bool isMasterMuted);
    void SetSyncDelay(double delayMs, bool snapImmediately = false);
    void UpdateTimelineMetrics(long lastBlockSeq, double driftMs);
    Task<bool> StartAsync(ISampleSource sampleSource, CancellationToken cancellationToken = default);
    Task<bool> StartWithLiveCaptureAsync(WaveFormat captureFormat, CancellationToken cancellationToken = default);
    void MarkAsNativeSource();
    void MarkAsDisconnected(string reason);
    void MarkAsRecovering();
    void EnqueueAudioData(byte[] buffer, int offset, int count);
    Task StopAsync();
}

public class EndpointAudioStream : IEndpointAudioStream
{
    private readonly AudioEndpointInfo _endpointInfo;
    private readonly ILogService _logService;
    private readonly object _stateLock = new();
    private readonly object _bufferLock = new();

    private WasapiOut? _wasapiOut;
    private MMDevice? _mmDevice;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private DelaySampleProvider? _delayProvider;
    private SmoothGainSampleProvider? _volumeProvider;

    private StreamPlaybackState _state = StreamPlaybackState.Stopped;
    private string? _errorMessage;
    private string _targetFormatText = "Pending Initialization";
    private int _latencyMs = 100;
    private bool _isNativeSource = false;
    private bool _hasRenderedAudioData = false;
    private bool _disposed = false;

    // Volume settings
    private double _deviceVolumePercent = 100.0;
    private bool _isMuted = false;
    private double _masterVolumePercent = 100.0;
    private bool _isMasterMuted = false;

    // Chunk 5: Sync delay and drift tracking
    private double _syncDelayMs = 0.0;
    private bool _isSyncReference = false;
    private double _estimatedDriftMs = 0.0;
    private long _lastBlockSequence = 0;

    // Health and latency diagnostics metrics
    private long _framesPlayed = 0;
    private long _audioDataWrittenBytes = 0;
    private long _bufferOverflowDropCount = 0;
    private DateTime? _lastAudioWriteTime;

    public string EndpointId => _endpointInfo.Id;
    public string EndpointName => _endpointInfo.FriendlyName;
    public DeviceTransportType Transport => _endpointInfo.Transport;
    public StreamPlaybackState State => _state;
    public string? ErrorMessage => _errorMessage;
    public bool IsNativeSource => _isNativeSource;
    public bool HasRenderedAudioData => _hasRenderedAudioData;

    public double DeviceVolumePercent => _deviceVolumePercent;
    public bool IsMuted => _isMuted;
    public double EffectiveGainPercent => CalculateEffectiveGain() * 100.0;

    public double SyncDelayMs => _syncDelayMs;
    public bool IsSyncReference
    {
        get => _isSyncReference;
        set
        {
            _isSyncReference = value;
            StatusChanged?.Invoke(this, Status);
        }
    }

    public event EventHandler<OutputStreamStatus>? StatusChanged;

    public OutputStreamStatus Status
    {
        get
        {
            int occBytes = 0;
            int capBytes = 0;
            double occMs = 0.0;

            if (_bufferedWaveProvider != null)
            {
                occBytes = _bufferedWaveProvider.BufferedBytes;
                capBytes = _bufferedWaveProvider.BufferLength;
                int avgBytesPerSec = _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond;
                if (avgBytesPerSec > 0)
                {
                    occMs = (double)occBytes / avgBytesPerSec * 1000.0;
                }
            }

            return new OutputStreamStatus
            {
                EndpointId = EndpointId,
                EndpointName = EndpointName,
                Transport = Transport,
                State = _state,
                TargetFormat = _targetFormatText,
                LatencyMs = _latencyMs,
                FramesPlayed = _framesPlayed,
                IsNativeSource = _isNativeSource,
                HasRenderedAudioData = _hasRenderedAudioData,
                ErrorMessage = _errorMessage,
                LastUpdated = DateTime.Now,

                DeviceVolume = _deviceVolumePercent,
                IsMuted = _isMuted,
                EffectiveGainPercent = CalculateEffectiveGain() * 100.0,

                BytesWritten = _audioDataWrittenBytes,
                FramesProcessed = _framesPlayed,
                BufferOccupancyBytes = occBytes,
                BufferOccupancyMs = Math.Round(occMs, 1),
                BufferCapacityBytes = capBytes,
                BufferOverflowDropCount = _bufferOverflowDropCount,
                BufferUnderrunCount = 0,
                LastAudioWriteTime = _lastAudioWriteTime,
                EstimatedBufferDelayMs = Math.Round(occMs + _syncDelayMs, 1),
                AcousticLatencyText = "Unknown (Hardware/Bluetooth dependent)",

                SyncDelayMs = _syncDelayMs,
                IsSyncReference = _isSyncReference,
                EstimatedDriftMs = _estimatedDriftMs,
                LastBlockSequence = _lastBlockSequence
            };
        }
    }

    public void SetSyncDelay(double delayMs, bool snapImmediately = false)
    {
        lock (_stateLock)
        {
            _syncDelayMs = Math.Clamp(delayMs, DelaySampleProvider.MinDelayMs, DelaySampleProvider.MaxDelayMs);
            _delayProvider?.SetDelayMs(_syncDelayMs, snapImmediately);
        }
        StatusChanged?.Invoke(this, Status);
    }

    public void UpdateTimelineMetrics(long lastBlockSeq, double driftMs)
    {
        _lastBlockSequence = lastBlockSeq;
        _estimatedDriftMs = driftMs;
    }

    public EndpointAudioStream(AudioEndpointInfo endpointInfo, ILogService logService)
    {
        _endpointInfo = endpointInfo;
        _logService = logService;
    }

    private float CalculateEffectiveGain()
    {
        if (_isMasterMuted || _isMuted) return 0.0f;
        double masterFactor = Math.Clamp(_masterVolumePercent / 100.0, 0.0, 1.0);
        double deviceFactor = Math.Clamp(_deviceVolumePercent / 100.0, 0.0, 1.0);
        return (float)(masterFactor * deviceFactor);
    }

    public void SetVolume(double deviceVolumePercent, bool isMuted, double masterVolumePercent, bool isMasterMuted)
    {
        lock (_stateLock)
        {
            _deviceVolumePercent = Math.Clamp(deviceVolumePercent, 0.0, 100.0);
            _isMuted = isMuted;
            _masterVolumePercent = Math.Clamp(masterVolumePercent, 0.0, 100.0);
            _isMasterMuted = isMasterMuted;

            float gain = CalculateEffectiveGain();
            _volumeProvider?.SetTargetGain(gain);
        }

        StatusChanged?.Invoke(this, Status);
    }

    public void MarkAsNativeSource()
    {
        lock (_stateLock)
        {
            _isNativeSource = true;
            _targetFormatText = "Native Windows Playback (Bypassed to prevent feedback/duplicate audio)";
            SetState(StreamPlaybackState.NativeSourcePlaying);
        }
        _logService.LogInfo($"[Bypass] '{EndpointName}' is the active capture source. Audio renders natively; second loopback stream bypassed.");
    }

    public void MarkAsDisconnected(string reason)
    {
        lock (_stateLock)
        {
            _errorMessage = reason;
            CleanupWasapi();
            SetState(StreamPlaybackState.Disconnected);
        }
        _logService.LogWarning($"[Stream Disconnected] Output '{EndpointName}' disconnected: {reason}");
    }

    public void MarkAsRecovering()
    {
        lock (_stateLock)
        {
            SetState(StreamPlaybackState.Recovering);
        }
        _logService.LogInfo($"[Stream Recovery] Output '{EndpointName}' is attempting recovery / reconnection...");
    }

    public async Task<bool> StartAsync(ISampleSource sampleSource, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    _errorMessage = "Audio stream has been disposed.";
                    SetState(StreamPlaybackState.Failed);
                    return false;
                }

                if (_state == StreamPlaybackState.Playing)
                {
                    return true;
                }

                SetState(StreamPlaybackState.Initializing);
                _errorMessage = null;
                _isNativeSource = false;
                _hasRenderedAudioData = true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (device, mixFormat) = InitializeDevice();

                // Create independent sample provider with delay and smooth gain control
                var streamSampleProvider = sampleSource.CreateStreamSampleProvider();
                var delayProvider = new DelaySampleProvider(streamSampleProvider, _syncDelayMs);
                _delayProvider = delayProvider;

                var volumeProvider = new SmoothGainSampleProvider(delayProvider, CalculateEffectiveGain());
                _volumeProvider = volumeProvider;

                var adaptedWaveProvider = AudioFormatAdapter.AdaptToTargetFormat(volumeProvider, mixFormat);
                var countingWaveProvider = new CountingWaveProvider(adaptedWaveProvider, bytesRead =>
                {
                    Interlocked.Add(ref _audioDataWrittenBytes, bytesRead);
                    _lastAudioWriteTime = DateTime.UtcNow;
                    int bytesPerFrame = mixFormat.BlockAlign;
                    if (bytesPerFrame > 0)
                    {
                        Interlocked.Add(ref _framesPlayed, bytesRead / bytesPerFrame);
                    }
                });

                InitializeWasapi(device, countingWaveProvider);

                _wasapiOut!.Play();

                lock (_stateLock)
                {
                    SetState(StreamPlaybackState.Playing);
                }

                _logService.LogSuccess($"[Stream Online] Tone stream open for '{EndpointName}' [{_targetFormatText}].");
                return true;
            }
            catch (OperationCanceledException)
            {
                lock (_stateLock)
                {
                    CleanupWasapi();
                    SetState(StreamPlaybackState.Stopped);
                }
                return false;
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _errorMessage = ex.Message;
                    CleanupWasapi();
                    SetState(StreamPlaybackState.Failed);
                }

                _logService.LogError($"[Stream Error] Failed to open render stream for '{EndpointName}': {ex.Message}");
                return false;
            }
        }, cancellationToken);
    }

    public async Task<bool> StartWithLiveCaptureAsync(WaveFormat captureFormat, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    _errorMessage = "Audio stream has been disposed.";
                    SetState(StreamPlaybackState.Failed);
                    return false;
                }

                if (_isNativeSource)
                {
                    return true;
                }

                if (_state == StreamPlaybackState.Playing)
                {
                    return true;
                }

                SetState(StreamPlaybackState.Initializing);
                _errorMessage = null;
                _framesPlayed = 0;
                _audioDataWrittenBytes = 0;
                _bufferOverflowDropCount = 0;
                _hasRenderedAudioData = false;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (device, destinationMixFormat) = InitializeDevice();

                // 1. Create bounded circular buffer for live captured audio (250ms capped buffer)
                int boundedBytes = (int)(captureFormat.AverageBytesPerSecond * 0.25);
                if (boundedBytes < 4096) boundedBytes = 4096;

                var bufferedWaveProvider = new BufferedWaveProvider(captureFormat)
                {
                    BufferLength = boundedBytes,
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };
                _bufferedWaveProvider = bufferedWaveProvider;

                // 2. Wrap bounded buffer in ISampleProvider bridge
                var capturedSampleProvider = CreateSampleProviderBridge(bufferedWaveProvider, captureFormat);

                // 3. Wrap with DelaySampleProvider for bounded per-device software delay compensation
                var delayProvider = new DelaySampleProvider(capturedSampleProvider, _syncDelayMs);
                _delayProvider = delayProvider;

                // 4. Wrap with SmoothGainSampleProvider for click-free volume adjustment
                var volumeProvider = new SmoothGainSampleProvider(delayProvider, CalculateEffectiveGain());
                _volumeProvider = volumeProvider;

                // 5. Resample and adapt format to destination endpoint's native format
                var adaptedWaveProvider = AudioFormatAdapter.AdaptToTargetFormat(volumeProvider, destinationMixFormat);

                // 5. Initialize WASAPI client in Shared Mode
                InitializeWasapi(device, adaptedWaveProvider);

                _wasapiOut!.Play();

                lock (_stateLock)
                {
                    SetState(StreamPlaybackState.Playing);
                }

                _logService.LogSuccess($"[Live Stream Online] Mirrored stream online for '{EndpointName}' [{_targetFormatText}] via bounded buffer.");
                return true;
            }
            catch (OperationCanceledException)
            {
                lock (_stateLock)
                {
                    CleanupWasapi();
                    SetState(StreamPlaybackState.Stopped);
                }
                return false;
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _errorMessage = ex.Message;
                    CleanupWasapi();
                    SetState(StreamPlaybackState.Failed);
                }

                _logService.LogError($"[Live Stream Error] Failed to open live capture stream for '{EndpointName}': {ex.Message}");
                return false;
            }
        }, cancellationToken);
    }

    public void EnqueueAudioData(byte[] buffer, int offset, int count)
    {
        if (_isNativeSource || _disposed || count <= 0) return;
        var bwp = _bufferedWaveProvider;
        if (bwp == null || _state != StreamPlaybackState.Playing) return;

        lock (_bufferLock)
        {
            if (_bufferedWaveProvider == null || _disposed) return;

            int currentOccupancy = _bufferedWaveProvider.BufferedBytes;
            int capacity = _bufferedWaveProvider.BufferLength;

            // Track buffer drop/overflow count
            if (currentOccupancy + count > capacity)
            {
                Interlocked.Increment(ref _bufferOverflowDropCount);
            }

            _bufferedWaveProvider.AddSamples(buffer, offset, count);
            _hasRenderedAudioData = true;
            Interlocked.Add(ref _audioDataWrittenBytes, count);
            _lastAudioWriteTime = DateTime.UtcNow;

            int bytesPerFrame = _bufferedWaveProvider.WaveFormat.BlockAlign;
            if (bytesPerFrame > 0)
            {
                Interlocked.Add(ref _framesPlayed, count / bytesPerFrame);
            }
        }
    }

    private (MMDevice Device, WaveFormat MixFormat) InitializeDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(_endpointInfo.Id);

        if (device == null || device.State != DeviceState.Active)
        {
            throw new InvalidOperationException($"Endpoint '{EndpointName}' is no longer active in Windows (State: {device?.State.ToString() ?? "Not Found"}).");
        }

        _mmDevice = device;
        var mixFormat = device.AudioClient.MixFormat;

        _targetFormatText = $"{mixFormat.SampleRate:N0} Hz • {mixFormat.Channels} Ch • {mixFormat.BitsPerSample}-bit ({mixFormat.Encoding})";
        return (device, mixFormat);
    }

    private void InitializeWasapi(MMDevice device, IWaveProvider waveProvider)
    {
        var wasapiOut = new WasapiOut(
            device,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 100);

        wasapiOut.PlaybackStopped += OnWasapiPlaybackStopped;

        try
        {
            wasapiOut.Init(waveProvider);
        }
        catch (Exception ex)
        {
            wasapiOut.Dispose();
            throw new InvalidOperationException($"WASAPI Init failed for '{EndpointName}' with format [{_targetFormatText}]: {ex.Message}", ex);
        }

        _wasapiOut = wasapiOut;
    }

    private static ISampleProvider CreateSampleProviderBridge(BufferedWaveProvider buffered, WaveFormat format)
    {
        if (format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"))
            {
                return new ExtensibleFloatSampleProvider(buffered, format);
            }

            if (extensible.SubFormat == new Guid("00000001-0000-0010-8000-00AA00389B71") && format.BitsPerSample == 16)
            {
                return new Pcm16BitToSampleProvider(buffered);
            }
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            return new Pcm16BitToSampleProvider(buffered);
        }

        return buffered.ToSampleProvider();
    }

    private class ExtensibleFloatSampleProvider : ISampleProvider
    {
        private readonly BufferedWaveProvider _source;
        public WaveFormat WaveFormat { get; }

        public ExtensibleFloatSampleProvider(BufferedWaveProvider source, WaveFormat originalFormat)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(originalFormat.SampleRate, originalFormat.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int bytesNeeded = count * 4;
            byte[] byteBuf = ArrayPool<byte>.Shared.Rent(bytesNeeded);
            try
            {
                int bytesRead = _source.Read(byteBuf, 0, bytesNeeded);
                int validBytes = bytesRead - (bytesRead % 4);
                int floatsRead = validBytes / 4;
                if (validBytes > 0)
                {
                    Buffer.BlockCopy(byteBuf, 0, buffer, offset * 4, validBytes);
                }
                return floatsRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteBuf);
            }
        }
    }

    private class Pcm16BitToSampleProvider : ISampleProvider
    {
        private readonly BufferedWaveProvider _source;
        public WaveFormat WaveFormat { get; }

        public Pcm16BitToSampleProvider(BufferedWaveProvider source)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int bytesNeeded = count * 2;
            byte[] byteBuf = ArrayPool<byte>.Shared.Rent(bytesNeeded);
            try
            {
                int bytesRead = _source.Read(byteBuf, 0, bytesNeeded);
                int validBytes = bytesRead - (bytesRead % 2);
                int samplesRead = validBytes / 2;
                for (int i = 0; i < samplesRead; i++)
                {
                    short val = (short)(byteBuf[i * 2] | (byteBuf[i * 2 + 1] << 8));
                    buffer[offset + i] = val / 32768.0f;
                }
                return samplesRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(byteBuf);
            }
        }
    }

    public async Task StopAsync()
    {
        await Task.Run(() =>
        {
            lock (_stateLock)
            {
                CleanupWasapi();
                SetState(StreamPlaybackState.Stopped);
            }
            _logService.LogInfo($"[Stream Stopped] Render stream stopped for '{EndpointName}'.");
        });
    }

    private void OnWasapiPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_stateLock)
        {
            if (_state == StreamPlaybackState.Playing)
            {
                if (e.Exception != null)
                {
                    _errorMessage = e.Exception.Message;
                    SetState(StreamPlaybackState.Disconnected);
                    _logService.LogWarning($"[Playback Stopped] Stream for '{EndpointName}' stopped unexpectedly: {e.Exception.Message}");
                }
                else
                {
                    SetState(StreamPlaybackState.Stopped);
                }
            }
        }
    }

    private void CleanupWasapi()
    {
        if (_wasapiOut != null)
        {
            try
            {
                _wasapiOut.PlaybackStopped -= OnWasapiPlaybackStopped;
                if (_wasapiOut.PlaybackState == PlaybackState.Playing)
                {
                    _wasapiOut.Stop();
                }
                _wasapiOut.Dispose();
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"[Teardown] Error during WasapiOut disposal for '{EndpointName}': {ex.Message}");
            }
            finally
            {
                _wasapiOut = null;
            }
        }

        if (_mmDevice != null)
        {
            try { _mmDevice.Dispose(); } catch { }
            _mmDevice = null;
        }

        _bufferedWaveProvider?.ClearBuffer();
        _bufferedWaveProvider = null;
        _delayProvider = null;
        _volumeProvider = null;
    }

    private void SetState(StreamPlaybackState newState)
    {
        _state = newState;
        StatusChanged?.Invoke(this, Status);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                CleanupWasapi();
                _state = StreamPlaybackState.Stopped;
            }
        }
    }

    private class CountingWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly Action<int> _onBytesRead;

        public CountingWaveProvider(IWaveProvider source, Action<int> onBytesRead)
        {
            _source = source;
            _onBytesRead = onBytesRead;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytes = _source.Read(buffer, offset, count);
            if (bytes > 0)
            {
                _onBytesRead(bytes);
            }
            return bytes;
        }
    }
}
