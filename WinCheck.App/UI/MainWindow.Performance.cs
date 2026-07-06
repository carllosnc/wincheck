using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCheck.Models;
using WinCheck.Services;

namespace WinCheck.UI;

public sealed partial class MainWindow
{
    private List<PerformanceOptimization> _optimizations = [];
    private bool _isPerfReady;

    public async void OnNavPerformance(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;
        DiskView.Visibility = Visibility.Collapsed;
        CleanupView.Visibility = Visibility.Collapsed;
        StartupView.Visibility = Visibility.Collapsed;
        PerformanceView.Visibility = Visibility.Visible;
        NavPerformance.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavStartup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        await LoadOptimizationsAsync();
    }

    private async Task LoadOptimizationsAsync()
    {
        _isPerfReady = false;
        SetLoading("Checking system settings...", isLoading: true);
        _optimizations = await Task.Run(PerformanceService.GetOptimizations);
        PerfList.ItemsSource = _optimizations;
        _isPerfReady = true;
        SetLoading(null, isLoading: false);
    }

    public async void OnPerfToggle(object sender, RoutedEventArgs e)
    {
        if (!_isPerfReady) return;
        if (sender is not ToggleSwitch ts || ts.Tag is not PerformanceOptimization opt) return;

        ts.IsEnabled = false;
        try
        {
            var (success, message) = await Task.Run(() => PerformanceService.Toggle(opt));
            PerfStatus.Text = message;

            if (!success)
            {
                ts.IsOn = !ts.IsOn;
                opt.IsEnabled = !opt.IsEnabled;
            }
        }
        finally
        {
            ts.IsEnabled = true;
        }
    }
}
