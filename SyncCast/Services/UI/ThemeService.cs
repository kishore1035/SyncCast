using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SyncCast.Services.UI;

public enum AppThemeMode
{
    Dark,
    Light,
    System
}

public static class ThemeService
{
    private static AppThemeMode _currentMode = AppThemeMode.Dark;
    public static AppThemeMode CurrentMode => _currentMode;

    public static bool IsDarkTheme => _currentMode switch
    {
        AppThemeMode.Light => false,
        AppThemeMode.Dark => true,
        AppThemeMode.System => IsWindowsInDarkMode(),
        _ => true
    };

    public static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightVal)
            {
                return lightVal == 0;
            }
        }
        catch
        {
        }
        return true; // Default to Dark
    }

    public static void ApplyTheme(AppThemeMode mode, Window? window = null)
    {
        _currentMode = mode;
        bool isDark = IsDarkTheme;

        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            if (isDark)
            {
                // Windows 11 Dark Fluent Palette
                SetBrush(resources, "BgDarkBrush", "#1C1C1F");
                SetBrush(resources, "RailBgBrush", "#161618");
                SetBrush(resources, "SurfaceDarkBrush", "#202024");
                SetBrush(resources, "SurfacePanelBrush", "#25252A");
                SetBrush(resources, "SurfaceCardBrush", "#2A2A30");
                SetBrush(resources, "SurfaceCardHoverBrush", "#32323A");
                SetBrush(resources, "BorderDarkBrush", "#1FFFFFFF");
                SetBrush(resources, "CardStrokeBrush", "#14FFFFFF");
                SetBrush(resources, "InputBgBrush", "#2E2E35");

                SetBrush(resources, "TextPrimaryBrush", "#F8FAFC");
                SetBrush(resources, "TextSecondaryBrush", "#94A3B8");
                SetBrush(resources, "TextMutedBrush", "#64748B");

                SetBrush(resources, "AccentCyanBrush", "#00A3D9");
                SetBrush(resources, "AccentCyanDarkBrush", "#0086B3");
                SetBrush(resources, "AccentGlowBrush", "#2600A3D9");
            }
            else
            {
                // Windows 11 Light Fluent Palette
                SetBrush(resources, "BgDarkBrush", "#F3F3F5");
                SetBrush(resources, "RailBgBrush", "#EBEBEF");
                SetBrush(resources, "SurfaceDarkBrush", "#FFFFFF");
                SetBrush(resources, "SurfacePanelBrush", "#F8F8FA");
                SetBrush(resources, "SurfaceCardBrush", "#FFFFFF");
                SetBrush(resources, "SurfaceCardHoverBrush", "#F4F4F7");
                SetBrush(resources, "BorderDarkBrush", "#E2E8F0");
                SetBrush(resources, "CardStrokeBrush", "#E5E7EB");
                SetBrush(resources, "InputBgBrush", "#F1F5F9");

                SetBrush(resources, "TextPrimaryBrush", "#0F172A");
                SetBrush(resources, "TextSecondaryBrush", "#475569");
                SetBrush(resources, "TextMutedBrush", "#94A3B8");

                SetBrush(resources, "AccentCyanBrush", "#0078D4");
                SetBrush(resources, "AccentCyanDarkBrush", "#005A9E");
                SetBrush(resources, "AccentGlowBrush", "#260078D4");
            }
        }

        if (window != null)
        {
            WindowBackdropHelper.ApplyBackdrop(window, isDark);
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hexColor)
    {
        var color = (Color)ColorConverter.ConvertFromString(hexColor);
        if (resources.Contains(key))
        {
            if (resources[key] is SolidColorBrush solid)
            {
                if (solid.IsFrozen)
                {
                    resources[key] = new SolidColorBrush(color);
                }
                else
                {
                    solid.Color = color;
                }
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }
}
