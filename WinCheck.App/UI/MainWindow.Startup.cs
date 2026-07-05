using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCheck.Models;
using WinCheck.Services;

namespace WinCheck.UI;

public sealed partial class MainWindow
{
    public void OnScanStartup(object sender, RoutedEventArgs e)
    {
        var entries = StartupService.ScanStartupEntries();
        StartupList.ItemsSource = entries;
    }

    public void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry entry)
        {
            StartupService.ToggleStartupEntry(entry);
            btn.Content = entry.ToggleText;
        }
    }
}
