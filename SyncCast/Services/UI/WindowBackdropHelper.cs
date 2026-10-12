using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SyncCast.Services.UI;

/// <summary>
/// Helper to configure Windows 11 Fluent DWM window attributes:
/// - Mica window backdrop
/// - Immersive Dark / Light mode on title bar and frame
/// - Rounded window corners
/// Gracefully falls back on older Windows builds or when DWM composition is disabled.
/// </summary>
public static class WindowBackdropHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_MICA_EFFECT = 1029;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2; // Rounded corners
    private const int DWMSBT_MAINWINDOW = 2; // Mica backdrop
    private const int DWMSBT_AUTO = 0;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static bool IsWindows11OrGreater
    {
        get
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version.Build >= 22000;
        }
    }

    public static void ApplyBackdrop(Window window, bool isDark)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        IntPtr hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero) return;

        ApplyBackdrop(hwnd, isDark);
    }

    public static void ApplyBackdrop(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            // 1. Set Immersive Dark / Light Mode
            int darkVal = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

            // 2. Set Windows 11 Rounded Corners (Build >= 22000)
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int cornerVal = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerVal, sizeof(int));

                // 3. Attempt Mica System Backdrop (Build >= 22621 uses DWMWA_SYSTEMBACKDROP_TYPE = 38)
                if (Environment.OSVersion.Version.Build >= 22621)
                {
                    int backdropVal = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropVal, sizeof(int));
                }
                else
                {
                    // Windows 11 21H2 (Build 22000) used undocumented attribute 1029
                    int micaVal = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref micaVal, sizeof(int));
                }
            }
        }
        catch
        {
            // Silently fall back to standard WPF rendering
        }
    }

    public static void SetDarkMode(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int darkVal = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
        }
        catch
        {
        }
    }
}
