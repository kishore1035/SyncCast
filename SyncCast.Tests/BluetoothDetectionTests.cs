using SyncCast.Models;
using SyncCast.Services.Audio;
using Xunit;

namespace SyncCast.Tests;

public class BluetoothDetectionTests
{
    [Theory]
    [InlineData("Headphones (WH-1000XM4 Audio)", "Bluetooth A2DP Device", @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}")]
    [InlineData("Headset (Hands-Free AG Audio)", "Bluetooth Handsfree", @"BTHHFENUM\BthHfAudio")]
    [InlineData("AirPods Pro - Find My", "AirPods Stereo", "")]
    [InlineData("Galaxy Buds2 Pro", "Samsung Wireless Audio", "")]
    [InlineData("Soundcore Life Q30 (Stereo)", "Bluetooth Audio Device", "")]
    [InlineData("Jabra Elite 75t", "Wireless Earbuds", "")]
    public void ClassifyTransport_IdentifiesBluetoothFromMetadata(string friendlyName, string desc, string pnp)
    {
        var (transport, reason) = AudioEndpointDiscoveryService.ClassifyTransport(friendlyName, desc, pnp, string.Empty);

        Assert.Equal(DeviceTransportType.Bluetooth, transport);
        Assert.Contains("Bluetooth", reason);
    }

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)", "Realtek High Definition Audio", @"HDAUDIO\FUNC_01&VEN_10EC")]
    [InlineData("Headphones (Realtek High Definition Audio)", "Realtek Audio", "")]
    [InlineData("Digital Audio (HDMI) (Intel(R) Display Audio)", "Intel Display Audio", @"HDAUDIO\FUNC_01&VEN_8086")]
    [InlineData("Speakers (USB Audio Device)", "Generic USB Audio", @"USB\VID_046D&PID_0825")]
    [InlineData("LG UltraWide (NVIDIA High Definition Audio)", "NVIDIA High Definition Audio", "")]
    public void ClassifyTransport_IdentifiesBuiltInOrWiredFromMetadata(string friendlyName, string desc, string pnp)
    {
        var (transport, reason) = AudioEndpointDiscoveryService.ClassifyTransport(friendlyName, desc, pnp, string.Empty);

        Assert.Equal(DeviceTransportType.BuiltInOrWired, transport);
        Assert.Contains("Built-In / Wired", reason);
    }

    [Theory]
    [InlineData("Generic Audio Output #1", "Generic Endpoint", "")]
    [InlineData("Virtual Audio Stream", "Virtual Driver", "")]
    [InlineData("Unknown Endpoint", "", "")]
    public void ClassifyTransport_LabelsAmbiguousDevicesAsUnknown_InsteadOfGuessing(string friendlyName, string desc, string pnp)
    {
        var (transport, reason) = AudioEndpointDiscoveryService.ClassifyTransport(friendlyName, desc, pnp, string.Empty);

        // Explicit requirement: If transport cannot be determined reliably, label it Unknown instead of guessing.
        Assert.Equal(DeviceTransportType.Unknown, transport);
        Assert.Contains("could not be determined", reason);
    }
}
