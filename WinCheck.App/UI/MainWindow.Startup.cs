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

    public void OnStartupSearchChanged(object sender, TextChangedEventArgs e)
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

        IEnumerable<StartupEntry> entries = _allStartupEntries;

        if (StartupFilter?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            entries = tag switch
            {
                "Registry" => entries.Where(x => x.EntryType == StartupEntryType.Registry),
                "StartupFolder" => entries.Where(x => x.EntryType == StartupEntryType.Folder),
                "ScheduledTask" => entries.Where(x => x.EntryType == StartupEntryType.ScheduledTask),
                "Service" => entries.Where(x => x.EntryType == StartupEntryType.Service),
                "StoreApp" => entries.Where(x => x.EntryType == StartupEntryType.StoreApp),
                _ => entries
            };
        }

        var search = StartupSearchBox?.Text?.Trim() ?? "";
        if (search.Length > 0)
        {
            entries = entries.Where(x =>
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Command.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        StartupList.ItemsSource = entries.ToList();
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
