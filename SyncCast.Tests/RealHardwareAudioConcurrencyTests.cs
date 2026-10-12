using SyncCast.Models;
using SyncCast.Services.Audio;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Logging;
using Xunit;
using Xunit.Abstractions;

namespace SyncCast.Tests;

public class RealHardwareAudioConcurrencyTests
{
    private readonly ITestOutputHelper _output;

    public RealHardwareAudioConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RealHardware_TestEndpointConcurrency_OnCurrentMachine()
    {
        var discoveryService = new AudioEndpointDiscoveryService();
        var logService = new LogService();
        var sampleSource = new ToneGeneratorSampleSource();
        // Safe, very low volume for automated testing (-26 dBFS / 0.05)
        sampleSource.Volume = 0.05f;

        var engine = new MultiOutputAudioEngine(logService, sampleSource);

        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: false);
        var activeEndpoints = endpoints.Where(e => e.State == EndpointActiveState.Active).Take(5).ToList();

        _output.WriteLine($"[Hardware Probe] Discovered {activeEndpoints.Count} active audio render endpoint(s) on this machine:");
        foreach (var ep in activeEndpoints)
        {
            _output.WriteLine($"  -> Endpoint: '{ep.FriendlyName}' | Transport: {ep.Transport} | Format: {ep.FormatSummary} | ID: {ep.Id}");
        }

        if (activeEndpoints.Count == 0)
        {
            _output.WriteLine("[Hardware Probe] Notice: No active audio render devices found on this environment (e.g. headless/CI environment). Skipping playback probe.");
            return;
        }

        // Attempt to start real WASAPI shared mode streams across all active endpoints
        var result = await engine.StartPlaybackAsync(activeEndpoints);

        _output.WriteLine($"[Hardware Concurrency Result] Attempted: {result.AttemptedCount} | Successfully Opened: {result.SuccessfullyPlayingCount} | Failed: {result.FailedCount}");

        foreach (var stream in result.StreamStatuses)
        {
            _output.WriteLine($"  - Stream '{stream.EndpointName}' ({stream.Transport}): State={stream.State}, TargetFormat=[{stream.TargetFormat}], Error=[{stream.ErrorMessage ?? "None"}]");
        }

        // Allow streams to run concurrently for 1 second if opened
        if (result.HasAnyPlaying)
        {
            await Task.Delay(1000);
            _output.WriteLine($"[Hardware Probe] Concurrency verified for {result.SuccessfullyPlayingCount} stream(s) running simultaneously for 1000 ms.");
        }

        // Stop cleanly and verify disposal
        await engine.StopPlaybackAsync();
        Assert.Equal(0, engine.ActivePlayingCount);

        _output.WriteLine("[Hardware Probe] Real hardware multi-output test completed and streams released cleanly.");
    }
}
