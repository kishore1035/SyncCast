using SyncCast.Models;
using SyncCast.ViewModels;
using Xunit;

namespace SyncCast.Tests;

public class AudioEndpointViewModelTests
{
    [Fact]
    public void EndpointViewModel_DisplaysPlaybackNotTestedStatus_Explicitly()
    {
        var endpoint = new AudioEndpointInfo
        {
            Id = "test-1",
            FriendlyName = "Studio Monitor",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired
        };

        var vm = new AudioEndpointViewModel(endpoint);

        // Required: Show separate statuses for Available, Selected, and Playback Not Tested
        Assert.Equal("Playback Not Tested", vm.PlaybackTestStatus);
        Assert.Equal("Available", vm.StatusText);
        Assert.Equal(DeviceStatus.Available, vm.CurrentStatus);

        vm.IsSelected = true;
        Assert.Equal("Selected for Output", vm.StatusText);
        Assert.Equal(DeviceStatus.Selected, vm.CurrentStatus);
    }

    [Fact]
    public void EndpointViewModel_FormatSummary_FormatsAudioSampleDetails()
    {
        var endpoint = new AudioEndpointInfo
        {
            Id = "test-2",
            FriendlyName = "Hi-Fi DAC",
            State = EndpointActiveState.Active,
            Transport = DeviceTransportType.BuiltInOrWired,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 24
        };

        var vm = new AudioEndpointViewModel(endpoint);

        Assert.Contains("48,000 Hz", vm.FormatSummary);
        Assert.Contains("Stereo (2 ch)", vm.FormatSummary);
        Assert.Contains("24-bit", vm.FormatSummary);
    }

    [Fact]
    public void EndpointViewModel_UnavailableEndpoints_AreNotSelectable()
    {
        var disabledEndpoint = new AudioEndpointInfo
        {
            Id = "test-disabled",
            FriendlyName = "Disconnected Bluetooth Earphones",
            State = EndpointActiveState.Unplugged,
            Transport = DeviceTransportType.Bluetooth
        };

        var vm = new AudioEndpointViewModel(disabledEndpoint);

        Assert.False(vm.IsSelectable);
        Assert.Equal("Cable Unplugged", vm.StatusText);
        Assert.Equal(DeviceStatus.Unavailable, vm.CurrentStatus);
    }
}
