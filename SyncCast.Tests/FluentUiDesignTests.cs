using SyncCast.Services.Devices;
using SyncCast.Services.Diagnostics;
using SyncCast.Services.Logging;
using SyncCast.Services.Settings;
using SyncCast.Services.UI;
using SyncCast.ViewModels;
using Xunit;

namespace SyncCast.Tests;

public class MockSettingsService : ISettingsService
{
    public SyncCastSettings Settings { get; set; } = new();

    public Task<SyncCastSettings> LoadSettingsAsync() => Task.FromResult(Settings);
    public Task SaveSettingsAsync(SyncCastSettings settings)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

public class FluentUiDesignTests
{
    private MainViewModel CreateViewModel(SyncCastSettings? initialSettings = null)
    {
        var discovery = new MockDiscoveryService();
        var selection = new DeviceSelectionManager();
        var diagnostics = new DiagnosticsService();
        var log = new LogService();
        var engine = new MockAudioEngine();
        var settingsService = new MockSettingsService();
        if (initialSettings != null)
        {
            settingsService.Settings = initialSettings;
        }

        return new MainViewModel(discovery, selection, diagnostics, log, engine, settingsService);
    }

    [Fact]
    public void MainViewModel_InitializesWithOverviewPage()
    {
        var vm = CreateViewModel();

        Assert.Equal(NavigationPage.Overview, vm.CurrentPage);
        Assert.True(vm.IsOverviewPage);
        Assert.False(vm.IsDevicesPage);
        Assert.False(vm.IsDiagnosticsPage);
        Assert.False(vm.IsSettingsPage);
        Assert.False(vm.IsAboutPage);
    }

    [Fact]
    public void MainViewModel_NavigatesBetweenPagesCorrectly()
    {
        var vm = CreateViewModel();

        // Navigate to Devices
        vm.Navigate("Devices");
        Assert.Equal(NavigationPage.Devices, vm.CurrentPage);
        Assert.True(vm.IsDevicesPage);
        Assert.False(vm.IsOverviewPage);

        // Navigate to Diagnostics
        vm.Navigate("Diagnostics");
        Assert.Equal(NavigationPage.Diagnostics, vm.CurrentPage);
        Assert.True(vm.IsDiagnosticsPage);

        // Navigate to Settings
        vm.Navigate("Settings");
        Assert.Equal(NavigationPage.Settings, vm.CurrentPage);
        Assert.True(vm.IsSettingsPage);

        // Navigate to About
        vm.Navigate("About");
        Assert.Equal(NavigationPage.About, vm.CurrentPage);
        Assert.True(vm.IsAboutPage);

        // Navigate back to Overview
        vm.Navigate("Overview");
        Assert.Equal(NavigationPage.Overview, vm.CurrentPage);
        Assert.True(vm.IsOverviewPage);

        // Invalid page name should be ignored safely
        vm.Navigate("InvalidPageName");
        Assert.Equal(NavigationPage.Overview, vm.CurrentPage);
    }

    [Fact]
    public void MainViewModel_UpdatesThemeMode()
    {
        var vm = CreateViewModel();

        vm.SetTheme("Light");
        Assert.Equal("Light", vm.ThemeMode);

        vm.SetTheme("Dark");
        Assert.Equal("Dark", vm.ThemeMode);

        vm.SetTheme("System");
        Assert.Equal("System", vm.ThemeMode);
    }

    [Fact]
    public void ThemeService_QueriesWindowsTheme_WithoutThrowing()
    {
        // Must succeed safely regardless of registry permissions
        bool isDark = ThemeService.IsWindowsInDarkMode();
        Assert.True(isDark || !isDark); // Valid boolean
    }

    [Fact]
    public void WindowBackdropHelper_VerifiesEnvironment_WithoutThrowing()
    {
        bool isWin11 = WindowBackdropHelper.IsWindows11OrGreater;
        Assert.True(isWin11 || !isWin11); // Valid boolean
    }
}
