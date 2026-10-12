using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SyncCast.Models;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Notifications;
using SyncCast.Services.Logging;

namespace SyncCast.Services.Audio.Engine;

public interface IMultiOutputAudioEngine : IDisposable
{
    bool IsRunning { get; }
    AudioEngineMode ActiveMode { get; }
    ISampleSource SampleSource { get; }
    ISystemAudioCaptureService CaptureService { get; }
    int ActivePlayingCount { get; }
    IReadOnlyList<OutputStreamStatus> CurrentStreamStatuses { get; }

    double MasterVolumePercent { get; }
    bool IsMasterMuted { get; }
    bool AutoReconnectEnabled { get; set; }

    event EventHandler<OutputStreamStatus>? StreamStatusChanged;
    event EventHandler<bool>? PlaybackStateChanged;

    string? ReferenceEndpointId { get; }
    SharedAudioTimeline Timeline { get; }

    void SetMasterVolume(double masterVolumePercent, bool isMuted);
    void SetDeviceVolume(string endpointId, double volumePercent, bool isMuted);
    void SetReferenceEndpoint(string? endpointId);
    void SetDeviceSyncDelay(string endpointId, double delayMs);
    void ResetAllSyncDelays();
    double GetDeviceSyncDelay(string endpointId);

    Task<bool> RetryEndpointAsync(string endpointId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mode 1: Test Tone Playback (Chunk 2)
    /// </summary>
    Task<MultiOutputPlaybackResult> StartPlaybackAsync(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mode 2: System Audio Loopback Capture & Multi-Output Mirroring (Chunk 3)
    /// Captures render mix from sourceEndpoint and mirrors to other selected endpoints.
    /// Bypasses sourceEndpoint from mirrored playback to prevent feedback and duplicate audio.
    /// </summary>
    Task<MultiOutputPlaybackResult> StartSystemAudioSharingAsync(
        AudioEndpointInfo sourceEndpoint,
        IReadOnlyList<AudioEndpointInfo> candidateOutputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mode 3: Guided Sync Calibration Pulse Playback (Chunk 5)
    /// Emits gentle, periodic Hann-windowed pulses for acoustic alignment.
    /// </summary>
    Task<MultiOutputPlaybackResult> StartCalibrationPulseAsync(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dynamically attaches a destination output endpoint to the currently running audio session.
    /// </summary>
    Task<OutputStreamStatus?> AttachDestinationAsync(
        AudioEndpointInfo destination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dynamically detaches and stops a destination output endpoint from the active audio session.
    /// </summary>
    Task DetachDestinationAsync(string endpointId);

    Task StopPlaybackAsync();
}

public class MultiOutputAudioEngine : IMultiOutputAudioEngine
{
    private readonly ILogService _logService;
    private readonly Func<AudioEndpointInfo, ILogService, IEndpointAudioStream> _streamFactory;
    private readonly IAudioEndpointNotificationService? _notificationService;
    private readonly ConcurrentDictionary<string, IEndpointAudioStream> _activeStreams = new();
    private readonly ConcurrentDictionary<string, (double Volume, bool IsMuted)> _deviceVolumes = new();
    private readonly ConcurrentDictionary<string, double> _deviceDelays = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconnectCts = new();
    private readonly object _engineLock = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private CancellationTokenSource? _sessionCts;
    private AudioEngineMode _activeMode = AudioEngineMode.Idle;
    private bool _disposed = false;

    // Master volume state (default 100%, unmuted)
    private double _masterVolumePercent = 100.0;
    private bool _isMasterMuted = false;
    private bool _autoReconnectEnabled = true;

    // Chunk 5: Sync reference and timeline
    private string? _referenceEndpointId;
    private SharedAudioTimeline _timeline = new();
    private ISampleSource? _calibrationPulseSource;

    // Track active session parameters for reconnection
    private AudioEndpointInfo? _currentCaptureSource;
    private List<AudioEndpointInfo> _currentCandidateEndpoints = new();

    // Reconnection backoff schedule: 1s, 2s, 4s, 8s, 15s
    private static readonly int[] ReconnectBackoffDelaysMs = { 1000, 2000, 4000, 8000, 15000 };

    public bool IsRunning => _activeMode != AudioEngineMode.Idle;
    public AudioEngineMode ActiveMode => _activeMode;
    public ISampleSource SampleSource { get; }
    public ISystemAudioCaptureService CaptureService { get; }

    public double MasterVolumePercent => _masterVolumePercent;
    public bool IsMasterMuted => _isMasterMuted;
    public bool AutoReconnectEnabled
    {
        get => _autoReconnectEnabled;
        set => _autoReconnectEnabled = value;
    }

    public string? ReferenceEndpointId => _referenceEndpointId;
    public SharedAudioTimeline Timeline => _timeline;

    public int ActivePlayingCount => _activeStreams.Values.Count(s => s.State == StreamPlaybackState.Playing && !s.IsNativeSource);

    public IReadOnlyList<OutputStreamStatus> CurrentStreamStatuses =>
        _activeStreams.Values.Select(s => s.Status).ToList();

    public event EventHandler<OutputStreamStatus>? StreamStatusChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

    public MultiOutputAudioEngine(
        ILogService logService,
        ISystemAudioCaptureService captureService,
        ISampleSource? sampleSource = null,
        Func<AudioEndpointInfo, ILogService, IEndpointAudioStream>? streamFactory = null,
        IAudioEndpointNotificationService? notificationService = null)
    {
        _logService = logService;
        CaptureService = captureService;
        SampleSource = sampleSource ?? new ToneGeneratorSampleSource();
        _streamFactory = streamFactory ?? ((info, log) => new EndpointAudioStream(info, log));
        _notificationService = notificationService;

        if (_notificationService != null)
        {
            _notificationService.EndpointStateChanged += OnEndpointStateChanged;
            _notificationService.EndpointRemoved += OnEndpointRemoved;
            _notificationService.StartListening();
        }
    }

    public MultiOutputAudioEngine(
        ILogService logService,
        ISampleSource? sampleSource = null,
        Func<AudioEndpointInfo, ILogService, IEndpointAudioStream>? streamFactory = null)
        : this(logService, new SystemAudioCaptureService(logService), sampleSource, streamFactory, null)
    {
    }

    public void SetMasterVolume(double masterVolumePercent, bool isMuted)
    {
        lock (_engineLock)
        {
            _masterVolumePercent = Math.Clamp(masterVolumePercent, 0.0, 100.0);
            _isMasterMuted = isMuted;

            foreach (var stream in _activeStreams.Values)
            {
                var (devVol, devMuted) = GetDeviceVolumeSetting(stream.EndpointId);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);
            }
        }

        _logService.LogInfo($"[Master Volume] Set to {_masterVolumePercent:F0}% (Muted: {_isMasterMuted}).");
    }

    public void SetDeviceVolume(string endpointId, double volumePercent, bool isMuted)
    {
        double clampedVol = Math.Clamp(volumePercent, 0.0, 100.0);
        _deviceVolumes[endpointId] = (clampedVol, isMuted);

        if (_activeStreams.TryGetValue(endpointId, out var stream))
        {
            stream.SetVolume(clampedVol, isMuted, _masterVolumePercent, _isMasterMuted);
        }

        _logService.LogInfo($"[Device Volume] Endpoint '{endpointId}' volume set to {clampedVol:F0}% (Muted: {isMuted}).");
    }

    private (double Volume, bool IsMuted) GetDeviceVolumeSetting(string endpointId)
    {
        if (_deviceVolumes.TryGetValue(endpointId, out var val))
        {
            return val;
        }
        return (100.0, false);
    }

    public void SetReferenceEndpoint(string? endpointId)
    {
        lock (_engineLock)
        {
            _referenceEndpointId = endpointId;
            _timeline.SetReferenceEndpoint(endpointId);

            foreach (var stream in _activeStreams.Values)
            {
                stream.IsSyncReference = string.Equals(stream.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase);
            }
        }
        _logService.LogInfo($"[Sync Reference] Reference endpoint set to: {endpointId ?? "None"}");
    }

    public void SetDeviceSyncDelay(string endpointId, double delayMs)
    {
        double clamped = Math.Clamp(delayMs, DelaySampleProvider.MinDelayMs, DelaySampleProvider.MaxDelayMs);
        _deviceDelays[endpointId] = clamped;
        _timeline.UpdateDestinationDelay(endpointId, clamped);

        if (_activeStreams.TryGetValue(endpointId, out var stream))
        {
            stream.SetSyncDelay(clamped);
        }
        _logService.LogInfo($"[Sync Delay] Endpoint '{endpointId}' delay set to {clamped:F1}ms");
    }

    public double GetDeviceSyncDelay(string endpointId)
    {
        if (_deviceDelays.TryGetValue(endpointId, out var delay))
        {
            return delay;
        }
        return 0.0;
    }

    public void ResetAllSyncDelays()
    {
        lock (_engineLock)
        {
            foreach (var key in _deviceDelays.Keys)
            {
                _deviceDelays[key] = 0.0;
            }
            foreach (var stream in _activeStreams.Values)
            {
                stream.SetSyncDelay(0.0);
                _timeline.UpdateDestinationDelay(stream.EndpointId, 0.0);
            }
        }
        _logService.LogInfo("[Sync Delay] All per-device delay compensation values reset to 0.0ms.");
    }

    public async Task<MultiOutputPlaybackResult> StartPlaybackAsync(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiOutputAudioEngine));
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await StopPlaybackInternalAsync();

            if (endpoints == null || endpoints.Count == 0)
            {
                _logService.LogWarning("[Audio Engine] No candidate endpoints provided for test tone playback.");
                return new MultiOutputPlaybackResult
                {
                    AttemptedCount = 0,
                    SuccessfullyPlayingCount = 0,
                    FailedCount = 0,
                    StreamStatuses = Array.Empty<OutputStreamStatus>()
                };
            }

            var candidateList = endpoints.Take(5).ToList();
            _currentCandidateEndpoints = candidateList;
            _currentCaptureSource = null;

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _sessionCts.Token;

            lock (_engineLock)
            {
                _activeMode = AudioEngineMode.TestTone;
                PlaybackStateChanged?.Invoke(this, true);
            }

            _logService.LogInfo($"[Audio Engine] Starting Mode 1: Test Tone across {candidateList.Count} endpoints...");

            _timeline = new SharedAudioTimeline(_referenceEndpointId);
            var resultsList = new List<OutputStreamStatus>();

            foreach (var endpoint in candidateList)
            {
                if (token.IsCancellationRequested) break;

                if (endpoint.State != EndpointActiveState.Active)
                {
                    _logService.LogWarning($"[Stream Skipped] Endpoint '{endpoint.FriendlyName}' is unavailable ({endpoint.State}).");
                    resultsList.Add(new OutputStreamStatus
                    {
                        EndpointId = endpoint.Id,
                        EndpointName = endpoint.FriendlyName,
                        Transport = endpoint.Transport,
                        State = StreamPlaybackState.Failed,
                        ErrorMessage = $"Endpoint state is {endpoint.State}."
                    });
                    continue;
                }

                var stream = _streamFactory(endpoint, _logService);
                var (devVol, devMuted) = GetDeviceVolumeSetting(endpoint.Id);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

                double devDelay = GetDeviceSyncDelay(endpoint.Id);
                stream.SetSyncDelay(devDelay, snapImmediately: true);
                stream.IsSyncReference = string.Equals(endpoint.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(endpoint.Id, devDelay);

                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[endpoint.Id] = stream;

                try
                {
                    bool started = await stream.StartAsync(SampleSource, token);
                    resultsList.Add(stream.Status);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"[Stream Launch Error] Failed opening tone stream for '{endpoint.FriendlyName}': {ex.Message}");
                    resultsList.Add(stream.Status);
                }
            }

            int playingCount = _activeStreams.Values.Count(s => s.State == StreamPlaybackState.Playing);
            int failedCount = resultsList.Count(s => s.State == StreamPlaybackState.Failed);

            return new MultiOutputPlaybackResult
            {
                AttemptedCount = candidateList.Count,
                SuccessfullyPlayingCount = playingCount,
                FailedCount = failedCount,
                StreamStatuses = resultsList
            };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<MultiOutputPlaybackResult> StartSystemAudioSharingAsync(
        AudioEndpointInfo sourceEndpoint,
        IReadOnlyList<AudioEndpointInfo> candidateOutputs,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiOutputAudioEngine));
        }

        if (sourceEndpoint == null)
        {
            throw new ArgumentNullException(nameof(sourceEndpoint), "System audio capture requires a valid source endpoint.");
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await StopPlaybackInternalAsync();

            if (sourceEndpoint.State != EndpointActiveState.Active)
            {
                _logService.LogError($"[System Audio Error] Selected capture source '{sourceEndpoint.FriendlyName}' is not active ({sourceEndpoint.State}).");
                return new MultiOutputPlaybackResult
                {
                    AttemptedCount = 0,
                    SuccessfullyPlayingCount = 0,
                    FailedCount = 1,
                    StreamStatuses = Array.Empty<OutputStreamStatus>()
                };
            }

            var candidateList = candidateOutputs?.Take(5).ToList() ?? new List<AudioEndpointInfo>();
            _currentCandidateEndpoints = candidateList;
            _currentCaptureSource = sourceEndpoint;

            bool isSourceAlsoOutput = candidateList.Any(o => string.Equals(o.Id, sourceEndpoint.Id, StringComparison.OrdinalIgnoreCase));
            var mirroredDestinations = candidateList
                .Where(o => !string.Equals(o.Id, sourceEndpoint.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _sessionCts.Token;

            _logService.LogInfo($"[System Audio Setup] Initializing WASAPI loopback capture on '{sourceEndpoint.FriendlyName}'...");
            bool captureStarted = await CaptureService.StartCaptureAsync(sourceEndpoint, token);
            if (!captureStarted)
            {
                _logService.LogError($"[System Audio Error] WASAPI Loopback capture failed to start on '{sourceEndpoint.FriendlyName}'.");
                return new MultiOutputPlaybackResult
                {
                    AttemptedCount = candidateList.Count,
                    SuccessfullyPlayingCount = 0,
                    FailedCount = candidateList.Count,
                    StreamStatuses = Array.Empty<OutputStreamStatus>()
                };
            }

            lock (_engineLock)
            {
                _activeMode = AudioEngineMode.SystemAudio;
                PlaybackStateChanged?.Invoke(this, true);
            }

            CaptureService.DataAvailable += OnCaptureDataAvailable;
            _timeline = new SharedAudioTimeline(_referenceEndpointId);

            var resultsList = new List<OutputStreamStatus>();

            if (isSourceAlsoOutput)
            {
                var nativeSource = candidateList.First(o => string.Equals(o.Id, sourceEndpoint.Id, StringComparison.OrdinalIgnoreCase));
                var nativeStream = _streamFactory(nativeSource, _logService);
                nativeStream.MarkAsNativeSource();
                nativeStream.SetSyncDelay(0.0, snapImmediately: true);
                nativeStream.IsSyncReference = string.Equals(nativeSource.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(nativeSource.Id, 0.0);

                _activeStreams[nativeSource.Id] = nativeStream;
                resultsList.Add(nativeStream.Status);
            }

            foreach (var destination in mirroredDestinations)
            {
                if (token.IsCancellationRequested) break;

                if (destination.State != EndpointActiveState.Active)
                {
                    _logService.LogWarning($"[Destination Skipped] '{destination.FriendlyName}' is unavailable ({destination.State}).");
                    resultsList.Add(new OutputStreamStatus
                    {
                        EndpointId = destination.Id,
                        EndpointName = destination.FriendlyName,
                        Transport = destination.Transport,
                        State = StreamPlaybackState.Failed,
                        ErrorMessage = $"Device state is {destination.State}."
                    });
                    continue;
                }

                var stream = _streamFactory(destination, _logService);
                var (devVol, devMuted) = GetDeviceVolumeSetting(destination.Id);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

                double devDelay = GetDeviceSyncDelay(destination.Id);
                stream.SetSyncDelay(devDelay, snapImmediately: true);
                stream.IsSyncReference = string.Equals(destination.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(destination.Id, devDelay);

                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[destination.Id] = stream;

                try
                {
                    var captureFormat = CaptureService.CaptureFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                    bool started = await stream.StartWithLiveCaptureAsync(captureFormat, token);
                    resultsList.Add(stream.Status);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"[Stream Launch Error] Failed opening live stream for '{destination.FriendlyName}': {ex.Message}");
                    resultsList.Add(stream.Status);
                }
            }

            int playingCount = _activeStreams.Values.Count(s => s.State == StreamPlaybackState.Playing && !s.IsNativeSource);
            int failedCount = resultsList.Count(s => s.State == StreamPlaybackState.Failed);

            _logService.LogSuccess($"[System Audio Active] Sharing active: {playingCount} output(s) active ({mirroredDestinations.Count} mirrored, source native: {(isSourceAlsoOutput ? "Yes" : "No")}).");

            return new MultiOutputPlaybackResult
            {
                AttemptedCount = candidateList.Count,
                SuccessfullyPlayingCount = playingCount,
                FailedCount = failedCount,
                StreamStatuses = resultsList
            };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private void OnCaptureDataAvailable(object? sender, AudioDataChunkEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _activeMode != AudioEngineMode.SystemAudio) return;

        int bytesPerFrame = CaptureService.CaptureFormat?.BlockAlign ?? 8;
        int frameCount = bytesPerFrame > 0 ? e.BytesRecorded / bytesPerFrame : 0;
        var (seq, pos) = _timeline.AdvanceSourceBlock(frameCount);
        int sampleRate = CaptureService.CaptureFormat?.SampleRate ?? 48000;

        foreach (var kvp in _activeStreams)
        {
            var stream = kvp.Value;
            if (!stream.IsNativeSource && stream.State == StreamPlaybackState.Playing)
            {
                stream.EnqueueAudioData(e.Buffer, 0, e.BytesRecorded);
                _timeline.RecordBlockEnqueued(stream.EndpointId, seq, frameCount, sampleRate);
                var destStatus = _timeline.GetDestinationStatus(stream.EndpointId);
                if (destStatus != null)
                {
                    stream.UpdateTimelineMetrics(seq, destStatus.EstimatedDriftMs);
                }
            }
        }
    }

    public async Task<MultiOutputPlaybackResult> StartCalibrationPulseAsync(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiOutputAudioEngine));
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await StopPlaybackInternalAsync();

            if (endpoints == null || endpoints.Count == 0)
            {
                _logService.LogWarning("[Calibration Pulse] No candidate endpoints provided for sync test.");
                return new MultiOutputPlaybackResult
                {
                    AttemptedCount = 0,
                    SuccessfullyPlayingCount = 0,
                    FailedCount = 0,
                    StreamStatuses = Array.Empty<OutputStreamStatus>()
                };
            }

            var candidateList = endpoints.Take(5).ToList();
            _currentCandidateEndpoints = candidateList;
            _currentCaptureSource = null;

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _sessionCts.Token;

            lock (_engineLock)
            {
                _activeMode = AudioEngineMode.CalibrationPulse;
                PlaybackStateChanged?.Invoke(this, true);
            }

            _logService.LogInfo($"[Calibration Pulse] Starting sync calibration pulse across {candidateList.Count} endpoints...");

            _calibrationPulseSource = new CalibrationPulseSampleSource();
            _timeline = new SharedAudioTimeline(_referenceEndpointId);

            var resultsList = new List<OutputStreamStatus>();

            foreach (var endpoint in candidateList)
            {
                if (token.IsCancellationRequested) break;

                if (endpoint.State != EndpointActiveState.Active)
                {
                    _logService.LogWarning($"[Stream Skipped] Endpoint '{endpoint.FriendlyName}' is unavailable ({endpoint.State}).");
                    resultsList.Add(new OutputStreamStatus
                    {
                        EndpointId = endpoint.Id,
                        EndpointName = endpoint.FriendlyName,
                        Transport = endpoint.Transport,
                        State = StreamPlaybackState.Failed,
                        ErrorMessage = $"Endpoint state is {endpoint.State}."
                    });
                    continue;
                }

                var stream = _streamFactory(endpoint, _logService);
                var (devVol, devMuted) = GetDeviceVolumeSetting(endpoint.Id);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

                double devDelay = GetDeviceSyncDelay(endpoint.Id);
                stream.SetSyncDelay(devDelay, snapImmediately: true);
                stream.IsSyncReference = string.Equals(endpoint.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(endpoint.Id, devDelay);

                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[endpoint.Id] = stream;

                try
                {
                    bool started = await stream.StartAsync(_calibrationPulseSource, token);
                    resultsList.Add(stream.Status);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"[Calibration Pulse Launch Error] Failed opening pulse stream for '{endpoint.FriendlyName}': {ex.Message}");
                    resultsList.Add(stream.Status);
                }
            }

            int playingCount = _activeStreams.Values.Count(s => s.State == StreamPlaybackState.Playing);
            int failedCount = resultsList.Count(s => s.State == StreamPlaybackState.Failed);

            return new MultiOutputPlaybackResult
            {
                AttemptedCount = candidateList.Count,
                SuccessfullyPlayingCount = playingCount,
                FailedCount = failedCount,
                StreamStatuses = resultsList
            };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<bool> RetryEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _disposed) return false;

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsRunning || _disposed) return false;

            var endpoint = _currentCandidateEndpoints.FirstOrDefault(e => e.Id == endpointId);
            if (endpoint == null)
            {
                _logService.LogWarning($"[Manual Retry] Endpoint '{endpointId}' is not among the active session candidates.");
                return false;
            }

            _logService.LogInfo($"[Manual Retry] Attempting to reconnect endpoint '{endpoint.FriendlyName}'...");

            if (!_activeStreams.TryGetValue(endpointId, out var stream))
            {
                stream = _streamFactory(endpoint, _logService);
                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[endpointId] = stream;
            }

            stream.MarkAsRecovering();
            var (devVol, devMuted) = GetDeviceVolumeSetting(endpointId);
            stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

            double devDelay = GetDeviceSyncDelay(endpointId);
            stream.SetSyncDelay(devDelay, snapImmediately: true);
            stream.IsSyncReference = string.Equals(endpointId, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);

            try
            {
                bool success = false;
                if (_activeMode == AudioEngineMode.SystemAudio)
                {
                    if (_currentCaptureSource != null && string.Equals(endpoint.Id, _currentCaptureSource.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        stream.MarkAsNativeSource();
                        success = true;
                    }
                    else
                    {
                        var captureFormat = CaptureService.CaptureFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                        success = await stream.StartWithLiveCaptureAsync(captureFormat, cancellationToken);
                    }
                }
                else if (_activeMode == AudioEngineMode.CalibrationPulse)
                {
                    success = await stream.StartAsync(_calibrationPulseSource ?? new CalibrationPulseSampleSource(), cancellationToken);
                }
                else if (_activeMode == AudioEngineMode.TestTone)
                {
                    success = await stream.StartAsync(SampleSource, cancellationToken);
                }

                if (success)
                {
                    _logService.LogSuccess($"[Manual Retry] Successfully reconnected and resumed playback for '{endpoint.FriendlyName}'.");
                    return true;
                }
                else
                {
                    _logService.LogWarning($"[Manual Retry] Could not restart output stream for '{endpoint.FriendlyName}'.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[Manual Retry] Reconnection error for '{endpoint.FriendlyName}': {ex.Message}");
                return false;
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<OutputStreamStatus?> AttachDestinationAsync(
        AudioEndpointInfo destination,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsRunning || destination == null) return null;

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (_activeStreams.TryGetValue(destination.Id, out var existing))
            {
                return existing.Status;
            }

            if (destination.State != EndpointActiveState.Active)
            {
                _logService.LogWarning($"[Destination Skipped] '{destination.FriendlyName}' is unavailable ({destination.State}).");
                return null;
            }

            if (_activeMode == AudioEngineMode.SystemAudio)
            {
                if (_currentCaptureSource != null && string.Equals(destination.Id, _currentCaptureSource.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var nativeStream = _streamFactory(destination, _logService);
                    nativeStream.MarkAsNativeSource();
                    nativeStream.SetSyncDelay(0.0, snapImmediately: true);
                    nativeStream.IsSyncReference = string.Equals(destination.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                    _timeline.RegisterDestination(destination.Id, 0.0);
                    _activeStreams[destination.Id] = nativeStream;
                    _logService.LogInfo($"[Dynamic Attach] '{destination.FriendlyName}' attached as native loopback source.");
                    return nativeStream.Status;
                }

                var stream = _streamFactory(destination, _logService);
                var (devVol, devMuted) = GetDeviceVolumeSetting(destination.Id);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

                double devDelay = GetDeviceSyncDelay(destination.Id);
                stream.SetSyncDelay(devDelay, snapImmediately: true);
                stream.IsSyncReference = string.Equals(destination.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(destination.Id, devDelay);

                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[destination.Id] = stream;

                var captureFormat = CaptureService.CaptureFormat ?? NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                bool started = await stream.StartWithLiveCaptureAsync(captureFormat, cancellationToken);
                _logService.LogSuccess($"[Dynamic Attach] Live stream started dynamically for '{destination.FriendlyName}'.");
                return stream.Status;
            }
            else if (_activeMode == AudioEngineMode.TestTone)
            {
                var stream = _streamFactory(destination, _logService);
                var (devVol, devMuted) = GetDeviceVolumeSetting(destination.Id);
                stream.SetVolume(devVol, devMuted, _masterVolumePercent, _isMasterMuted);

                double devDelay = GetDeviceSyncDelay(destination.Id);
                stream.SetSyncDelay(devDelay, snapImmediately: true);
                stream.IsSyncReference = string.Equals(destination.Id, _referenceEndpointId, StringComparison.OrdinalIgnoreCase);
                _timeline.RegisterDestination(destination.Id, devDelay);

                stream.StatusChanged += OnIndividualStreamStatusChanged;
                _activeStreams[destination.Id] = stream;

                await stream.StartAsync(SampleSource, cancellationToken);
                _logService.LogSuccess($"[Dynamic Attach] Tone stream started dynamically for '{destination.FriendlyName}'.");
                return stream.Status;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logService.LogError($"[Dynamic Attach Error] Failed attaching destination '{destination.FriendlyName}': {ex.Message}");
            return null;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task DetachDestinationAsync(string endpointId)
    {
        if (_disposed || string.IsNullOrEmpty(endpointId)) return;

        await _sessionLock.WaitAsync();
        try
        {
            if (_activeStreams.TryRemove(endpointId, out var stream))
            {
                stream.StatusChanged -= OnIndividualStreamStatusChanged;
                stream.Dispose();
                _timeline.UnregisterDestination(endpointId);
                _logService.LogInfo($"[Dynamic Detach] Detached stream for '{endpointId}'.");
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private void OnEndpointStateChanged(object? sender, (string EndpointId, DeviceState NewState) args)
    {
        if (args.NewState != DeviceState.Active)
        {
            HandleEndpointDisconnected(args.EndpointId, $"Device state changed to {args.NewState}");
        }
        else
        {
            HandleEndpointReconnected(args.EndpointId);
        }
    }

    private void OnEndpointRemoved(object? sender, string endpointId)
    {
        HandleEndpointDisconnected(endpointId, "Device unplugged or removed from Windows.");
    }

    public void NotifyEndpointDisconnected(string endpointId, string reason)
    {
        HandleEndpointDisconnected(endpointId, reason);
    }

    public void NotifyEndpointReconnected(string endpointId)
    {
        HandleEndpointReconnected(endpointId);
    }

    private void HandleEndpointDisconnected(string endpointId, string reason)
    {
        if (!_activeStreams.TryGetValue(endpointId, out var stream)) return;
        if (stream.State == StreamPlaybackState.Disconnected || stream.State == StreamPlaybackState.Recovering) return;

        stream.MarkAsDisconnected(reason);

        // Cancel any existing reconnect task for this endpoint
        if (_reconnectCts.TryRemove(endpointId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        if (_autoReconnectEnabled && IsRunning && !stream.IsNativeSource)
        {
            ScheduleAutoReconnect(endpointId);
        }
    }

    private void HandleEndpointReconnected(string endpointId)
    {
        if (!IsRunning || !_autoReconnectEnabled) return;
        if (!_activeStreams.TryGetValue(endpointId, out var stream)) return;

        if (stream.State == StreamPlaybackState.Disconnected || stream.State == StreamPlaybackState.Recovering || stream.State == StreamPlaybackState.Failed)
        {
            _logService.LogInfo($"[Device Reappeared] Endpoint '{stream.EndpointName}' is active again. Triggering recovery...");
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // Brief settle time
                await RetryEndpointAsync(endpointId);
            });
        }
    }

    private void ScheduleAutoReconnect(string endpointId)
    {
        var reconnectCts = new CancellationTokenSource();
        _reconnectCts[endpointId] = reconnectCts;
        var token = reconnectCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_activeStreams.TryGetValue(endpointId, out var stream)) return;
                string devName = stream.EndpointName;

                _logService.LogInfo($"[Auto-Reconnect] Starting controlled recovery backoff policy for '{devName}'...");

                for (int attempt = 0; attempt < ReconnectBackoffDelaysMs.Length; attempt++)
                {
                    int delayMs = ReconnectBackoffDelaysMs[attempt];

                    try
                    {
                        await Task.Delay(delayMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logService.LogInfo($"[Auto-Reconnect] Recovery cancelled for '{devName}'.");
                        return;
                    }

                    if (!IsRunning || token.IsCancellationRequested) return;

                    stream.MarkAsRecovering();
                    _logService.LogInfo($"[Auto-Reconnect] Attempt {attempt + 1}/{ReconnectBackoffDelaysMs.Length} for '{devName}' (delay: {delayMs}ms)...");

                    bool success = await RetryEndpointAsync(endpointId, token);
                    if (success)
                    {
                        _logService.LogSuccess($"[Auto-Reconnect] Recovery successful! '{devName}' re-joined live playback.");
                        return;
                    }
                }

                _logService.LogWarning($"[Auto-Reconnect] Exhausted {ReconnectBackoffDelaysMs.Length} recovery attempts for '{devName}'. Marking as Failed (manual Retry available).");
            }
            finally
            {
                if (_reconnectCts.TryRemove(endpointId, out var removedCts))
                {
                    removedCts.Dispose();
                }
                else
                {
                    reconnectCts.Dispose();
                }
            }
        }, token);
    }

    public async Task StopPlaybackAsync()
    {
        await _sessionLock.WaitAsync();
        try
        {
            await StopPlaybackInternalAsync();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task StopPlaybackInternalAsync()
    {
        _sessionCts?.Cancel();

        // Cancel and clear pending auto-reconnect operations
        foreach (var kvp in _reconnectCts)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _reconnectCts.Clear();

        CaptureService.DataAvailable -= OnCaptureDataAvailable;
        await CaptureService.StopCaptureAsync();

        var streamsToStop = _activeStreams.Values.ToList();
        _activeStreams.Clear();

        foreach (var stream in streamsToStop)
        {
            try
            {
                stream.StatusChanged -= OnIndividualStreamStatusChanged;
                await stream.StopAsync();
                stream.Dispose();
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"[Stream Teardown] Error stopping stream '{stream.EndpointName}': {ex.Message}");
            }
        }

        lock (_engineLock)
        {
            if (_activeMode != AudioEngineMode.Idle)
            {
                _activeMode = AudioEngineMode.Idle;
                _calibrationPulseSource = null;
                PlaybackStateChanged?.Invoke(this, false);
                _logService.LogInfo("[Audio Engine] Session stopped. All capture and render streams released.");
            }
        }

        _sessionCts?.Dispose();
        _sessionCts = null;
    }

    private void OnIndividualStreamStatusChanged(object? sender, OutputStreamStatus status)
    {
        if (status.State == StreamPlaybackState.Disconnected)
        {
            HandleEndpointDisconnected(status.EndpointId, status.ErrorMessage ?? "Playback disconnected.");
        }

        StreamStatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        lock (_engineLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _sessionCts?.Cancel();
                _sessionCts?.Dispose();
                _sessionCts = null;

                foreach (var kvp in _reconnectCts)
                {
                    kvp.Value.Cancel();
                    kvp.Value.Dispose();
                }
                _reconnectCts.Clear();

                if (_notificationService != null)
                {
                    _notificationService.EndpointStateChanged -= OnEndpointStateChanged;
                    _notificationService.EndpointRemoved -= OnEndpointRemoved;
                    _notificationService.Dispose();
                }

                CaptureService.DataAvailable -= OnCaptureDataAvailable;
                CaptureService.Dispose();

                foreach (var stream in _activeStreams.Values)
                {
                    try
                    {
                        stream.StatusChanged -= OnIndividualStreamStatusChanged;
                        stream.Dispose();
                    }
                    catch
                    {
                    }
                }
                _activeStreams.Clear();
                _activeMode = AudioEngineMode.Idle;
                _sessionLock.Dispose();
            }
        }
    }
}
