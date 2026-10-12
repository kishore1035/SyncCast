using SyncCast.Models;
using SyncCast.Services.Audio;
using Xunit;

namespace SyncCast.Tests;

public class RealWindowsAudioDiscoveryTests
{
    [Fact]
    public async Task RealWindowsAudioDiscovery_EnumeratesRealEndpoints_WithoutCrashing()
    {
        var discoveryService = new AudioEndpointDiscoveryService();

        var endpoints = await discoveryService.DiscoverEndpointsAsync(includeInactive: true);

        // Verification: The service should execute against Windows Core Audio APIs without exceptions
        Assert.NotNull(endpoints);

        foreach (var endpoint in endpoints)
        {
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Id), "Endpoint ID must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(endpoint.FriendlyName), "Endpoint friendly name must not be empty");

            // Critical rule: "Never show fake sample devices in the real device list."
            Assert.DoesNotContain("Sample Device", endpoint.FriendlyName);
            Assert.DoesNotContain("Fake Device", endpoint.FriendlyName);
            Assert.DoesNotContain("Simulated", endpoint.FriendlyName);

            // Transport must be Bluetooth, BuiltInOrWired, or honest Unknown
            Assert.True(
                endpoint.Transport == DeviceTransportType.Bluetooth ||
                endpoint.Transport == DeviceTransportType.BuiltInOrWired ||
                endpoint.Transport == DeviceTransportType.Unknown);
        }
    }
}
