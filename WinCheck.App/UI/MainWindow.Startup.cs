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
    private List<StartupEntry> _allStartupEntries = [];
    private bool _isTogglingStartup;

    public async void OnScanStartup(object sender, RoutedEventArgs e)
    {
        await ScanStartupAsync();
    }

    public void OnStartupFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyStartupFilter();
    }

    private async Task ScanStartupAsync()
    {
        SetLoading("Scanning startup entries...", isLoading: true);
        _allStartupEntries = await Task.Run(StartupService.ScanStartupEntries);
        ApplyStartupFilter();
        SetLoading(null, isLoading: false);
    }

    private void ApplyStartupFilter()
    {
        if (StartupList is null) return;

        if (StartupFilter?.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            StartupList.ItemsSource = _allStartupEntries;
            return;
        }

        var filtered = tag switch
        {
            "Registry" => _allStartupEntries.Where(x => x.EntryType == StartupEntryType.Registry).ToList(),
            "StartupFolder" => _allStartupEntries.Where(x => x.EntryType == StartupEntryType.Folder).ToList(),
            "ScheduledTask" => _allStartupEntries.Where(x => x.EntryType == StartupEntryType.ScheduledTask).ToList(),
            "Service" => _allStartupEntries.Where(x => x.EntryType == StartupEntryType.Service).ToList(),
            _ => _allStartupEntries
        };

        StartupList.ItemsSource = filtered;
    }

    public void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        if (_isTogglingStartup) return;
        if (sender is not CheckBox cb || cb.Tag is not StartupEntry entry) return;

        _isTogglingStartup = true;
        try
        {
            if (!StartupService.ToggleStartupEntry(entry))
                cb.IsChecked = !cb.IsChecked;
        }
        finally
        {
            _isTogglingStartup = false;
        }
    }
}
