using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCheck.Models;
using WinCheck.Services;

namespace WinCheck.UI;

public sealed partial class MainWindow
{
    private string? _currentDrive;
    private string? _currentScanPath;
    private List<FolderInfo> _currentResults = [];
    private readonly Dictionary<string, List<FolderInfo>> _scanCache = new(StringComparer.OrdinalIgnoreCase);

    private void PopulateDrives()
    {
        DriveSelector.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                var label = string.IsNullOrEmpty(drive.VolumeLabel)
                    ? $"{drive.Name} ({FormatBytes(drive.TotalSize)})"
                    : $"{drive.Name} {drive.VolumeLabel} ({FormatBytes(drive.TotalSize)})";
                var item = new ComboBoxItem { Content = label, Tag = drive.Name };
                DriveSelector.Items.Add(item);
            }
        }
        if (DriveSelector.Items.Count > 0)
            DriveSelector.SelectedIndex = 0;
    }

    public void OnNavDisk(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;
        DiskView.Visibility = Visibility.Visible;
        CleanupView.Visibility = Visibility.Collapsed;
        StartupView.Visibility = Visibility.Collapsed;
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavStartup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
    }

    public void OnDriveChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentDrive = null;
        _currentScanPath = null;
        _currentResults.Clear();
        _scanCache.Clear();
        FolderList.ItemsSource = null;
        ScanStatus.Text = "";
        BreadcrumbPath.Visibility = Visibility.Collapsed;
        DiskBackBtn.IsEnabled = false;
    }

    public async void OnScanDisk(object sender, RoutedEventArgs e)
    {
        if (DriveSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string drive)
            return;

        _scanCache.Clear();
        await ScanPath(drive);
    }

    public void OnFolderDrill(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FolderInfo folder)
            _ = ScanPath(folder.Path);
    }

    public void OnDiskBack(object sender, RoutedEventArgs e)
    {
        if (_currentScanPath == null || _currentDrive == null) return;

        var parent = Path.GetDirectoryName(_currentScanPath);
        if (parent == null || !parent.StartsWith(_currentDrive, StringComparison.OrdinalIgnoreCase))
            parent = _currentDrive;

        _ = ScanPath(parent);
    }

    public void OnDiskSortChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySort();
    }

    private async Task ScanPath(string path)
    {
        _currentScanPath = path;
        _currentDrive ??= Path.GetPathRoot(path);

        if (_scanCache.TryGetValue(path, out var cached))
        {
            _currentResults = cached;
            BreadcrumbPath.Text = path;
            BreadcrumbPath.Visibility = Visibility.Visible;
            DiskBackBtn.IsEnabled = !string.Equals(path, _currentDrive, StringComparison.OrdinalIgnoreCase);
            var totalSize = cached.Sum(f => f.SizeBytes);
            ScanStatus.Text = $"{cached.Count} folders | {cached.Sum(f => f.FolderCount)} subfolders | {FolderInfo.FormatBytesLocal(totalSize)}";
            ApplySort();
            return;
        }

        ScanBtn.IsEnabled = false;
        ScanStatus.Text = "Scanning...";
        FolderList.ItemsSource = null;
        ScanProgress.Visibility = Visibility.Visible;
        BreadcrumbPath.Text = path;
        BreadcrumbPath.Visibility = Visibility.Visible;
        DiskBackBtn.IsEnabled = !string.Equals(path, _currentDrive, StringComparison.OrdinalIgnoreCase);

        var results = new List<FolderInfo>();
        var progress = new Progress<FolderInfo>(folder =>
        {
            ScanCurrentFolder.Text = folder.Path;
            var totalSize = results.Sum(f => f.SizeBytes) + folder.SizeBytes;
            ScanStats.Text = $"{results.Count + 1} folders | {folder.FileCount} files | {FolderInfo.FormatBytesLocal(totalSize)}";
            results.Add(folder);
        });

        await Task.Run(() => DiskService.ScanDirectory(path, progress));

        ScanProgress.Visibility = Visibility.Collapsed;
        _currentResults = results;

        if (results.Count == 0)
        {
            ScanStatus.Text = "No folders found.";
            FolderList.ItemsSource = null;
        }
        else
        {
            var totalSize = results.Sum(f => f.SizeBytes);
            foreach (var f in results)
                f.Percentage = totalSize > 0 ? (double)f.SizeBytes / totalSize * 100.0 : 0;

            _scanCache[path] = results;
            ApplySort();
            var subFolders = results.Sum(f => f.FolderCount);
            ScanStatus.Text = $"{results.Count} folders | {subFolders} subfolders | {FolderInfo.FormatBytesLocal(totalSize)}";
        }

        ScanBtn.IsEnabled = true;
    }

    private void ApplySort()
    {
        if (_currentResults.Count == 0) return;

        FolderList.ItemsSource = (DiskSortMode.SelectedIndex) switch
        {
            1 => _currentResults.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            2 => _currentResults.OrderByDescending(f => f.FileCount).ToList(),
            _ => _currentResults.OrderByDescending(f => f.SizeBytes).ToList()
        };
    }
}
