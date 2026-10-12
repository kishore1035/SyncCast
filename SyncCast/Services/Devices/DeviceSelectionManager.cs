using SyncCast.Models;

namespace SyncCast.Services.Devices;

public interface IDeviceSelectionManager
{
    int MaxSelectionLimit { get; }
    IReadOnlySet<string> SelectedDeviceIds { get; }
    int SelectedCount { get; }

    bool IsSelected(string endpointId);
    bool CanSelect(AudioEndpointInfo endpoint, out string reason);
    SelectionResult ToggleSelection(AudioEndpointInfo endpoint, out string message);
    void ClearSelection();
    void PrunePreservedSelection(IEnumerable<AudioEndpointInfo> currentEndpoints);
}

public class DeviceSelectionManager : IDeviceSelectionManager
{
    public const int DefaultMaxSelectionLimit = 5;
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);

    public int MaxSelectionLimit { get; }
    public IReadOnlySet<string> SelectedDeviceIds => _selectedIds;
    public int SelectedCount => _selectedIds.Count;

    public DeviceSelectionManager(int maxSelectionLimit = DefaultMaxSelectionLimit)
    {
        MaxSelectionLimit = maxSelectionLimit;
    }

    public bool IsSelected(string endpointId)
    {
        return _selectedIds.Contains(endpointId);
    }

    public bool CanSelect(AudioEndpointInfo endpoint, out string reason)
    {
        if (endpoint == null)
        {
            reason = "Invalid endpoint specification.";
            return false;
        }

        if (endpoint.State != EndpointActiveState.Active)
        {
            reason = $"Device '{endpoint.FriendlyName}' is unavailable (state: {endpoint.State}). Only active devices can be selected.";
            return false;
        }

        if (_selectedIds.Contains(endpoint.Id))
        {
            reason = "Device is already selected.";
            return true;
        }

        if (_selectedIds.Count >= MaxSelectionLimit)
        {
            reason = $"Maximum limit of {MaxSelectionLimit} candidate outputs reached. Deselect an existing device to add this one.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public SelectionResult ToggleSelection(AudioEndpointInfo endpoint, out string message)
    {
        if (endpoint == null)
        {
            message = "Endpoint reference is null.";
            return SelectionResult.NotFound;
        }

        if (_selectedIds.Contains(endpoint.Id))
        {
            _selectedIds.Remove(endpoint.Id);
            message = $"Deselected '{endpoint.FriendlyName}'. Selected outputs: {_selectedIds.Count}/{MaxSelectionLimit}.";
            return SelectionResult.Deselected;
        }

        if (endpoint.State != EndpointActiveState.Active)
        {
            message = $"Cannot select '{endpoint.FriendlyName}'. Windows reports the device as {endpoint.State}. In Windows Bluetooth settings, ensure it says 'Connected voice, music', then click Refresh Devices.";
            return SelectionResult.DeviceUnavailable;
        }

        if (_selectedIds.Count >= MaxSelectionLimit)
        {
            message = $"Limit reached: Cannot select '{endpoint.FriendlyName}'. Maximum {MaxSelectionLimit} output endpoints allowed simultaneously.";
            return SelectionResult.LimitReached;
        }

        _selectedIds.Add(endpoint.Id);
        message = $"Selected '{endpoint.FriendlyName}' ({_selectedIds.Count}/{MaxSelectionLimit}). Playback verification will occur in testing stage.";
        return SelectionResult.Selected;
    }

    public void ClearSelection()
    {
        _selectedIds.Clear();
    }

    public void PrunePreservedSelection(IEnumerable<AudioEndpointInfo> currentEndpoints)
    {
        var activeExistingIds = currentEndpoints
            .Where(e => e.State == EndpointActiveState.Active)
            .Select(e => e.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove IDs that are either no longer present or no longer active
        _selectedIds.RemoveWhere(id => !activeExistingIds.Contains(id));
    }
}
