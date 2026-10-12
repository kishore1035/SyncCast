using System.Collections.Concurrent;
using System.Diagnostics;

namespace SyncCast.Services.Audio.Engine;

/// <summary>
/// Per-destination timeline telemetry tracking frame delivery, sequence ordering, and drift.
/// </summary>
public record DestinationTimelineStatus
{
    public required string EndpointId { get; init; }
    public long FramesEnqueued { get; init; }
    public long LastBlockSequence { get; init; }
    public long DroppedFrames { get; init; }
    public double EstimatedDriftMs { get; init; }
    public DateTime? LastWriteTime { get; init; }
    public double ConfiguredDelayMs { get; init; }
    public bool IsReference { get; init; }
}

/// <summary>
/// Central shared audio timeline providing monotonic clock tracking, block sequence numbering,
/// and per-destination timing and drift metrics across the multi-output pipeline.
/// </summary>
public class SharedAudioTimeline
{
    private readonly long _epochTimestamp;
    private long _totalSourceFrames = 0;
    private long _currentSequenceNumber = 0;
    private string? _referenceEndpointId;

    private readonly ConcurrentDictionary<string, DestinationTracker> _destinations = new();

    public long EpochTimestamp => _epochTimestamp;
    public long TotalSourceFrames => Interlocked.Read(ref _totalSourceFrames);
    public long CurrentSequenceNumber => Interlocked.Read(ref _currentSequenceNumber);
    public string? ReferenceEndpointId => _referenceEndpointId;

    public SharedAudioTimeline(string? referenceEndpointId = null)
    {
        _epochTimestamp = Stopwatch.GetTimestamp();
        _referenceEndpointId = referenceEndpointId;
    }

    public void SetReferenceEndpoint(string? endpointId)
    {
        _referenceEndpointId = endpointId;
    }

    /// <summary>
    /// Gets elapsed session duration using the high-resolution monotonic stopwatch clock.
    /// </summary>
    public TimeSpan ElapsedSessionTime
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            double seconds = (double)(now - _epochTimestamp) / Stopwatch.Frequency;
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }
    }

    /// <summary>
    /// Registers or updates a destination output stream.
    /// </summary>
    public void RegisterDestination(string endpointId, double configuredDelayMs = 0.0)
    {
        _destinations.AddOrUpdate(
            endpointId,
            id => new DestinationTracker(id) { ConfiguredDelayMs = configuredDelayMs },
            (id, existing) =>
            {
                existing.ConfiguredDelayMs = configuredDelayMs;
                return existing;
            });
    }

    public void UnregisterDestination(string endpointId)
    {
        _destinations.TryRemove(endpointId, out _);
    }

    public void UpdateDestinationDelay(string endpointId, double delayMs)
    {
        if (_destinations.TryGetValue(endpointId, out var tracker))
        {
            tracker.ConfiguredDelayMs = delayMs;
        }
    }

    /// <summary>
    /// Allocates the next monotonic block sequence number and advances source frames.
    /// </summary>
    public (long SequenceNumber, long FramePosition) AdvanceSourceBlock(int frameCount)
    {
        long seq = Interlocked.Increment(ref _currentSequenceNumber);
        long pos = Interlocked.Add(ref _totalSourceFrames, frameCount);
        return (seq, pos);
    }

    /// <summary>
    /// Records audio block delivery to an individual destination.
    /// </summary>
    public void RecordBlockEnqueued(string endpointId, long sequenceNumber, int frameCount, int sampleRate = 48000)
    {
        if (_destinations.TryGetValue(endpointId, out var tracker))
        {
            tracker.LastBlockSequence = sequenceNumber;
            long total = Interlocked.Add(ref tracker.FramesEnqueued, frameCount);
            tracker.LastWriteTime = DateTime.UtcNow;

            // Compute software drift relative to source frames
            long sourceFrames = Interlocked.Read(ref _totalSourceFrames);
            long frameDelta = sourceFrames - total;
            if (sampleRate > 0)
            {
                tracker.EstimatedDriftMs = Math.Round(((double)frameDelta / sampleRate) * 1000.0, 1);
            }
        }
    }

    public void RecordDroppedFrames(string endpointId, int droppedFrameCount)
    {
        if (_destinations.TryGetValue(endpointId, out var tracker))
        {
            Interlocked.Add(ref tracker.DroppedFrames, droppedFrameCount);
        }
    }

    public DestinationTimelineStatus? GetDestinationStatus(string endpointId)
    {
        if (_destinations.TryGetValue(endpointId, out var tracker))
        {
            return new DestinationTimelineStatus
            {
                EndpointId = endpointId,
                FramesEnqueued = Interlocked.Read(ref tracker.FramesEnqueued),
                LastBlockSequence = tracker.LastBlockSequence,
                DroppedFrames = Interlocked.Read(ref tracker.DroppedFrames),
                EstimatedDriftMs = tracker.EstimatedDriftMs,
                LastWriteTime = tracker.LastWriteTime,
                ConfiguredDelayMs = tracker.ConfiguredDelayMs,
                IsReference = string.Equals(endpointId, _referenceEndpointId, StringComparison.OrdinalIgnoreCase)
            };
        }
        return null;
    }

    public IReadOnlyList<DestinationTimelineStatus> GetAllDestinationStatuses()
    {
        return _destinations.Values.Select(tracker => new DestinationTimelineStatus
        {
            EndpointId = tracker.EndpointId,
            FramesEnqueued = Interlocked.Read(ref tracker.FramesEnqueued),
            LastBlockSequence = tracker.LastBlockSequence,
            DroppedFrames = Interlocked.Read(ref tracker.DroppedFrames),
            EstimatedDriftMs = tracker.EstimatedDriftMs,
            LastWriteTime = tracker.LastWriteTime,
            ConfiguredDelayMs = tracker.ConfiguredDelayMs,
            IsReference = string.Equals(tracker.EndpointId, _referenceEndpointId, StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    private class DestinationTracker
    {
        public string EndpointId { get; }
        public long FramesEnqueued = 0;
        public long LastBlockSequence = 0;
        public long DroppedFrames = 0;
        public double EstimatedDriftMs = 0.0;
        public DateTime? LastWriteTime;
        public double ConfiguredDelayMs = 0.0;

        public DestinationTracker(string endpointId)
        {
            EndpointId = endpointId;
        }
    }
}
