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
    private List<CleanupCategory> _cleanupCategories = [];
    private bool _cleanupAnalyzed;

    public void OnNavCleanup(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;
        DiskView.Visibility = Visibility.Collapsed;
        CleanupView.Visibility = Visibility.Visible;
        StartupView.Visibility = Visibility.Collapsed;
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavStartup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
    }

    public void OnCleanupToggleDetails(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CleanupCategory cat)
            cat.IsExpanded = !cat.IsExpanded;
    }

    public async void OnCleanupAnalyze(object sender, RoutedEventArgs e)
    {
        SetLoading("Analyzing system...", true);
        CleanupAnalyzeBtn.IsEnabled = false;
        CleanupRunBtn.IsEnabled = false;
        CleanupStatus.Text = "Analyzing...";
        _cleanupAnalyzed = false;

        _cleanupCategories = CleanupService.BuildCleanupCategories();
        CleanupList.ItemsSource = _cleanupCategories;

        foreach (var cat in _cleanupCategories)
        {
            cat.Status = "Scanning...";
            var (size, details) = await Task.Run(() => CleanupService.CalculateCategorySize(cat));
            cat.SizeBytes = size;
            cat.DetailsText = details;
            cat.Status = size > 0
                ? (cat.Name == "Recycle Bin"
                    ? $"{CleanupService.QueryRecycleBin().items} items found"
                    : $"{CleanupService.CountCategoryFiles(cat)} files found")
                : "Nothing to clean";
        }

        var total = _cleanupCategories.Where(c => c.SizeBytes > 0).Sum(c => c.SizeBytes);
        CleanupStatus.Text = total > 0
            ? $"Found {FolderInfo.FormatBytesLocal(total)} of cleanable data"
            : "Nothing to clean";

        CleanupRunBtn.IsEnabled = total > 0;
        CleanupAnalyzeBtn.IsEnabled = true;
        _cleanupAnalyzed = true;
        SetLoading(null, false);
    }

    public async void OnCleanupRun(object sender, RoutedEventArgs e)
    {
        if (!_cleanupAnalyzed) return;

        SetLoading("Cleaning files...", true);

        var isAdmin = CleanupService.IsAdministrator();
        var needsAdmin = _cleanupCategories.Any(c => c.IsChecked && c.SizeBytes > 0 && c.RequiresAdmin);

        if (needsAdmin && !isAdmin)
        {
            CleanupStatus.Text = "Run as administrator to clean system paths";
            ShowAdminCategories();
            CleanupAnalyzeBtn.IsEnabled = true;
            SetLoading(null, false);
            return;
        }

        var toClean = _cleanupCategories.Where(c => c.IsChecked && c.SizeBytes > 0).ToList();
        var hasRecycle = toClean.Any(c => c.Name == "Recycle Bin");
        var confirm = new ContentDialog
        {
            Title = "Clean selected items?",
            Content = hasRecycle
                ? $"This will permanently delete files in {toClean.Count} categor{(toClean.Count == 1 ? "y" : "ies")}, including the Recycle Bin. This cannot be undone."
                : $"This will permanently delete files in {toClean.Count} categor{(toClean.Count == 1 ? "y" : "ies")}. This cannot be undone.",
            PrimaryButtonText = "Clean",
            CloseButtonText = "Cancel",
            XamlRoot = ((UIElement)Content).XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            SetLoading(null, false);
            return;
        }

        CleanupAnalyzeBtn.IsEnabled = false;
        CleanupRunBtn.IsEnabled = false;
        var totalFreed = 0L;
        var totalFiles = 0;

        foreach (var cat in _cleanupCategories.Where(c => c.IsChecked && c.SizeBytes > 0))
        {
            cat.Status = "Cleaning...";
            var (files, freed) = await Task.Run(() => CleanupService.CleanCategory(cat));
            cat.FilesDeleted = files;
            cat.BytesFreed = freed;
            cat.Status = files > 0 ? "Cleaned" : "Skipped";
            if (files == 0 && cat.IsChecked) cat.Status = "Nothing to clean";
            cat.SizeBytes = 0;
            totalFreed += freed;
            totalFiles += files;
        }

        CleanupStatus.Text = totalFiles > 0
            ? $"Cleaned {totalFiles} files | {FolderInfo.FormatBytesLocal(totalFreed)} freed"
            : "Nothing was cleaned";

        CleanupAnalyzeBtn.IsEnabled = true;
        SetLoading(null, false);
    }

    private void ShowAdminCategories()
    {
        foreach (var cat in _cleanupCategories.Where(c => c.RequiresAdmin && c.SizeBytes > 0))
            cat.Status = "Requires admin rights";
    }
}
