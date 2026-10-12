using SyncCast.Models;
using SyncCast.Services.Devices;
using Xunit;

namespace SyncCast.Tests;

public class DeviceSelectionManagerTests
{
    private AudioEndpointInfo CreateEndpoint(string id, string name, EndpointActiveState state = EndpointActiveState.Active)
    {
        return new AudioEndpointInfo
        {
            Id = id,
            FriendlyName = name,
            State = state,
            Transport = DeviceTransportType.Bluetooth
        };
    }

    [Fact]
    public void SelectionManager_AllowsSelectingUpToFiveDevices()
    {
        var manager = new DeviceSelectionManager(5);

        for (int i = 1; i <= 5; i++)
        {
            var ep = CreateEndpoint($"dev-{i}", $"Device {i}");
            var result = manager.ToggleSelection(ep, out string msg);

            Assert.Equal(SelectionResult.Selected, result);
            Assert.True(manager.IsSelected(ep.Id));
            Assert.Contains($"{i}/5", msg);
        }

        Assert.Equal(5, manager.SelectedCount);
    }

    [Fact]
    public void SelectionManager_StrictlyRejectsSixthDevice()
    {
        var manager = new DeviceSelectionManager(5);

        for (int i = 1; i <= 5; i++)
        {
            var ep = CreateEndpoint($"dev-{i}", $"Device {i}");
            manager.ToggleSelection(ep, out _);
        }

        var sixthDevice = CreateEndpoint("dev-6", "Device 6");
        var result = manager.ToggleSelection(sixthDevice, out string message);

        Assert.Equal(SelectionResult.LimitReached, result);
        Assert.False(manager.IsSelected("dev-6"));
        Assert.Equal(5, manager.SelectedCount);
        Assert.Contains("Maximum 5 output endpoints allowed", message);
    }

    [Fact]
    public void SelectionManager_RejectsUnavailableEndpoints()
    {
        var manager = new DeviceSelectionManager(5);
        var disabledEndpoint = CreateEndpoint("dev-disabled", "Disabled Headset", EndpointActiveState.Disabled);
        var unpluggedEndpoint = CreateEndpoint("dev-unplugged", "Unplugged Aux", EndpointActiveState.Unplugged);

        var res1 = manager.ToggleSelection(disabledEndpoint, out string msg1);
        var res2 = manager.ToggleSelection(unpluggedEndpoint, out string msg2);

        Assert.Equal(SelectionResult.DeviceUnavailable, res1);
        Assert.Equal(SelectionResult.DeviceUnavailable, res2);
        Assert.Equal(0, manager.SelectedCount);
        Assert.Contains("Disabled", msg1);
        Assert.Contains("Unplugged", msg2);
    }

    [Fact]
    public void SelectionManager_TogglingSelectedDeviceDeselectsIt()
    {
        var manager = new DeviceSelectionManager(5);
        var ep = CreateEndpoint("dev-1", "Bluetooth Headphones");

        manager.ToggleSelection(ep, out _);
        Assert.True(manager.IsSelected(ep.Id));
        Assert.Equal(1, manager.SelectedCount);

        var result = manager.ToggleSelection(ep, out string msg);
        Assert.Equal(SelectionResult.Deselected, result);
        Assert.False(manager.IsSelected(ep.Id));
        Assert.Equal(0, manager.SelectedCount);
        Assert.Contains("Deselected", msg);
    }

    [Fact]
    public void SelectionManager_ClearSelection_RemovesAllSelections()
    {
        var manager = new DeviceSelectionManager(5);
        manager.ToggleSelection(CreateEndpoint("d1", "Dev 1"), out _);
        manager.ToggleSelection(CreateEndpoint("d2", "Dev 2"), out _);

        Assert.Equal(2, manager.SelectedCount);
        manager.ClearSelection();
        Assert.Equal(0, manager.SelectedCount);
    }

    [Fact]
    public void SelectionManager_PrunePreservedSelection_PreservesActiveAndRemovesInactive()
    {
        var manager = new DeviceSelectionManager(5);
        var ep1 = CreateEndpoint("d1", "Dev 1", EndpointActiveState.Active);
        var ep2 = CreateEndpoint("d2", "Dev 2", EndpointActiveState.Active);
        var ep3 = CreateEndpoint("d3", "Dev 3", EndpointActiveState.Active);

        manager.ToggleSelection(ep1, out _);
        manager.ToggleSelection(ep2, out _);
        manager.ToggleSelection(ep3, out _);

        // Simulated refresh: ep1 is still active, ep2 became disabled, ep3 disappeared
        var currentEndpoints = new[]
        {
            CreateEndpoint("d1", "Dev 1", EndpointActiveState.Active),
            CreateEndpoint("d2", "Dev 2", EndpointActiveState.Disabled),
            CreateEndpoint("d4", "Dev 4", EndpointActiveState.Active)
        };

        manager.PrunePreservedSelection(currentEndpoints);

        Assert.True(manager.IsSelected("d1"));
        Assert.False(manager.IsSelected("d2")); // pruned because state is Disabled
        Assert.False(manager.IsSelected("d3")); // pruned because no longer present
        Assert.Equal(1, manager.SelectedCount);
    }
}
