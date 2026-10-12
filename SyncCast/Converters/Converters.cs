using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SyncCast.Models;

namespace SyncCast.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            bool b = v == Visibility.Visible;
            if (Invert) b = !b;
            return b;
        }
        return Binding.DoNothing;
    }
}

public class InvertBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class TransportToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush BluetoothBrush = new(Color.FromRgb(0, 229, 255));      // Electric Cyan #00E5FF
    private static readonly SolidColorBrush BuiltInBrush = new(Color.FromRgb(99, 102, 241));       // Indigo #6366F1
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromRgb(148, 163, 184));      // Slate #94A3B8

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DeviceTransportType transport)
        {
            return transport switch
            {
                DeviceTransportType.Bluetooth => BluetoothBrush,
                DeviceTransportType.BuiltInOrWired => BuiltInBrush,
                _ => UnknownBrush
            };
        }
        return UnknownBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class StateToStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(16, 185, 129));      // Emerald #10B981
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(245, 158, 11));     // Amber #F59E0B
    private static readonly SolidColorBrush DisabledBrush = new(Color.FromRgb(100, 116, 139));   // Slate #64748B

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is EndpointActiveState state)
        {
            return state switch
            {
                EndpointActiveState.Active => ActiveBrush,
                EndpointActiveState.Disabled => DisabledBrush,
                EndpointActiveState.Unplugged => WarningBrush,
                EndpointActiveState.NotPresent => DisabledBrush,
                _ => DisabledBrush
            };
        }
        return DisabledBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(148, 163, 184));     // Slate
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(52, 211, 153));   // Emerald
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(251, 191, 36));   // Amber
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(248, 113, 113));    // Crimson

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Success => SuccessBrush,
                LogLevel.Warning => WarningBrush,
                LogLevel.Error => ErrorBrush,
                _ => InfoBrush
            };
        }
        return InfoBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class StreamStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PlayingBrush = new(Color.FromRgb(16, 185, 129));      // Emerald #10B981
    private static readonly SolidColorBrush NativeSourceBrush = new(Color.FromRgb(0, 229, 255));   // Electric Cyan #00E5FF
    private static readonly SolidColorBrush InitializingBrush = new(Color.FromRgb(245, 158, 11)); // Amber #F59E0B
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(239, 68, 68));        // Crimson #EF4444
    private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromRgb(245, 158, 11)); // Amber #F59E0B
    private static readonly SolidColorBrush RecoveringBrush = new(Color.FromRgb(59, 130, 246));   // Blue #3B82F6
    private static readonly SolidColorBrush StoppedBrush = new(Color.FromRgb(100, 116, 139));     // Slate #64748B

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StreamPlaybackState state)
        {
            return state switch
            {
                StreamPlaybackState.NativeSourcePlaying => NativeSourceBrush,
                StreamPlaybackState.Playing => PlayingBrush,
                StreamPlaybackState.Initializing => InitializingBrush,
                StreamPlaybackState.Recovering => RecoveringBrush,
                StreamPlaybackState.Disconnected => DisconnectedBrush,
                StreamPlaybackState.Failed => FailedBrush,
                _ => StoppedBrush
            };
        }
        return StoppedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class CaptureStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush RunningBrush = new(Color.FromRgb(16, 185, 129));      // Emerald #10B981
    private static readonly SolidColorBrush StartingBrush = new(Color.FromRgb(245, 158, 11));     // Amber #F59E0B
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(239, 68, 68));        // Crimson #EF4444
    private static readonly SolidColorBrush StoppedBrush = new(Color.FromRgb(100, 116, 139));     // Slate #64748B

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CaptureState state)
        {
            return state switch
            {
                CaptureState.Running => RunningBrush,
                CaptureState.Starting => StartingBrush,
                CaptureState.Failed => FailedBrush,
                _ => StoppedBrush
            };
        }
        return StoppedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class EqualityToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool matches = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (Invert) matches = !matches;
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public class EqualityToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            if (targetType.IsEnum && Enum.TryParse(targetType, parameter.ToString(), true, out var result))
            {
                return result;
            }
            return parameter;
        }
        return Binding.DoNothing;
    }
}

public class TransportToIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (app == null) return Geometry.Empty;

        if (value is DeviceTransportType transport)
        {
            return transport switch
            {
                DeviceTransportType.Bluetooth => app.TryFindResource("IconBluetooth") as Geometry ?? Geometry.Empty,
                DeviceTransportType.BuiltInOrWired => app.TryFindResource("IconSpeaker") as Geometry ?? Geometry.Empty,
                _ => app.TryFindResource("IconDevices") as Geometry ?? Geometry.Empty
            };
        }
        return app.TryFindResource("IconDevices") as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
