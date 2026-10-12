using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncCast.Models;

namespace SyncCast.ViewModels;

public partial class AudioEndpointViewModel : ObservableObject
{
    private readonly Action<AudioEndpointViewModel>? _onToggleRequested;

    public AudioEndpointInfo Info { get; private set; }

    public void UpdateState(EndpointActiveState newState)
    {
        if (Info.State != newState)
        {
            Info = Info with { State = newState };
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsSelectable));
            OnPropertyChanged(nameof(CurrentStatus));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(FormatSummary));
        }
    }

    public string Id => Info.Id;
    public string FriendlyName => Info.FriendlyName;
    public string DeviceDescription => Info.DeviceDescription;
    public EndpointActiveState State => Info.State;
    public DeviceTransportType Transport => Info.Transport;
    public string TransportReason => Info.TransportReason;
    public bool IsDefaultEndpoint => Info.IsDefaultEndpoint;
    public string FormatSummary => Info.FormatSummary;
    public string FormatText => FormatSummary;
    public bool IsActive => Info.IsActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CurrentStatus))]
    private bool _isSelected;

    public bool IsSelectable => Info.IsActive;

    public DeviceStatus CurrentStatus
    {
        get
        {
            if (!Info.IsActive) return DeviceStatus.Unavailable;
            return IsSelected ? DeviceStatus.Selected : DeviceStatus.Available;
        }
    }

    public string StatusText
    {
        get
        {
            if (!Info.IsActive)
            {
                return Info.State switch
                {
                    EndpointActiveState.Disabled => "Disabled by Windows",
                    EndpointActiveState.NotPresent => "Not Present",
                    EndpointActiveState.Unplugged => "Cable Unplugged",
                    _ => "Unavailable"
                };
            }

            return IsSelected ? "Selected for Output" : "Available";
        }
    }

    /// <summary>
    /// Explicit separate status showing that audio playback hasn't been verified yet.
    /// </summary>
    public string PlaybackTestStatus => "Playback Not Tested";

    public string TransportBadgeText => Transport switch
    {
        DeviceTransportType.Bluetooth => "Bluetooth",
        DeviceTransportType.BuiltInOrWired => "Built-In / Wired",
        _ => "Unknown Transport"
    };

    public AudioEndpointViewModel(AudioEndpointInfo info, bool isSelected = false, Action<AudioEndpointViewModel>? onToggleRequested = null)
    {
        Info = info;
        _isSelected = isSelected;
        _onToggleRequested = onToggleRequested;
    }

    [RelayCommand]
    private void ToggleSelection()
    {
        _onToggleRequested?.Invoke(this);
    }
}
