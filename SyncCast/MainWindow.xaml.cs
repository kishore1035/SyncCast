using System.Windows;
using SyncCast.Services.UI;

namespace SyncCast;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// Windows 11 Fluent Design App Shell with Mica backdrop and immersive title bar
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        bool isDark = ThemeService.IsDarkTheme;
        WindowBackdropHelper.ApplyBackdrop(this, isDark);
    }
}