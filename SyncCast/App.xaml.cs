using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SyncCast.Services.Audio;
using SyncCast.Services.Audio.Capture;
using SyncCast.Services.Audio.Engine;
using SyncCast.Services.Audio.Notifications;
using SyncCast.Services.Devices;
using SyncCast.Services.Diagnostics;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using SyncCast.ViewModels;

namespace SyncCast;

/// <summary>
/// Interaction logic for App.xaml
/// Configures dependency injection and starts main application window.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"SyncCast encountered an unexpected error:\n\n{args.Exception.Message}\n\n{args.Exception.InnerException?.Message}", "SyncCast Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAudioEndpointNotificationService, AudioEndpointNotificationService>();
        services.AddSingleton<IDeviceSelectionManager, DeviceSelectionManager>();
        services.AddSingleton<IAudioEndpointDiscoveryService, AudioEndpointDiscoveryService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<ISystemAudioCaptureService, SystemAudioCaptureService>();
        services.AddSingleton<IMultiOutputAudioEngine, MultiOutputAudioEngine>();

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            var viewModel = Services.GetRequiredService<MainViewModel>();
            mainWindow.DataContext = viewModel;
            MainWindow = mainWindow;
            mainWindow.Show();

            // Perform initial device discovery on launch
            await viewModel.RefreshDevicesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize SyncCast window:\n\n{ex.Message}\n\n{ex.InnerException?.Message}", "SyncCast Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            var engine = Services.GetService<IMultiOutputAudioEngine>();
            if (engine != null)
            {
                await engine.StopPlaybackAsync();
                engine.Dispose();
            }
        }
        catch
        {
        }

        base.OnExit(e);
    }
}
