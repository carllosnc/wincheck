using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCheck.Models;
using WinCheck.Services;

namespace WinCheck.UI;

public sealed partial class MainWindow
{
    public void OnScanStartup(object sender, RoutedEventArgs e)
    {
        StartupList.ItemsSource = StartupService.ScanStartupEntries();
    }

    public void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not StartupEntry entry) return;

        var success = StartupService.ToggleStartupEntry(entry);
        if (!success)
            cb.IsChecked = !cb.IsChecked;
    }
}
