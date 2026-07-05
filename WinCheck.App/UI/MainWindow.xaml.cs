using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Graphics;
using WinCheck.Models;
using WinCheck.Services;
using WinCheck.UI.Controls;
using WinRT.Interop;

namespace WinCheck.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ProcessTree.ItemTemplateSelector = new TreeNodeTemplateSelector();

        var queue = DispatcherQueue.GetForCurrentThread();

        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => LoadProcesses(isAuto: true);

        _searchDebounce = queue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(200);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => LoadProcesses();

        Activated += OnActivated;
        if (Content is UIElement ui)
            ui.KeyDown += OnKeyDown;

        CollectHardwareInfo();
        PopulateDrives();
        StartupList.ItemsSource = StartupService.ScanStartupEntries();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnActivated;
        SetWindowSize(1100, 700);
        LoadProcesses();
        AutoToggle.IsChecked = true;
        _timer?.Start();
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
    }

    private void SetWindowSize(int width, int height)
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        var x = (displayArea.WorkArea.Width - width) / 2;
        var y = (displayArea.WorkArea.Height - height) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.F5)
        {
            LoadProcesses();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Space)
        {
            ToggleAutoRefresh();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.F &&
                 (GetAsyncKeyState(0x11) & 0x8000) != 0)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && SearchBox.FocusState != FocusState.Unfocused)
        {
            SearchBox.Text = "";
            ProcessTree.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void SetLoading(string? message, bool isLoading)
    {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) LoadingOverlayText.Text = message;
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F0} MB",
        >= 1_024 => $"{bytes / 1_024.0:F0} KB",
        _ => $"{bytes} B"
    };

    private static SolidColorBrush CreateTagColor(bool isWindows) =>
        (SolidColorBrush)(isWindows
            ? Application.Current.Resources["AccentFillColorDefaultBrush"]
            : Application.Current.Resources["TextFillColorSecondaryBrush"]);

    public void OnNavProcesses(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Visible;
        InfoView.Visibility = Visibility.Collapsed;
        DiskView.Visibility = Visibility.Collapsed;
        CleanupView.Visibility = Visibility.Collapsed;
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
    }

    public void OnNavInfo(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Visible;
        DiskView.Visibility = Visibility.Collapsed;
        CleanupView.Visibility = Visibility.Collapsed;
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        CollectHardwareInfo();
        var entries = StartupService.ScanStartupEntries();
        StartupList.ItemsSource = entries;
    }

    private void CollectHardwareInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            InfoCpuName.Text = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown";
        }
        catch { InfoCpuName.Text = "Unknown"; }

        InfoCpuCores.Text = Environment.ProcessorCount.ToString();
        InfoCpuLogical.Text = Environment.ProcessorCount.ToString();
        InfoCpuArch.Text = RuntimeInformation.OSArchitecture.ToString();
        InfoCpuUsage.Text = $"{CalculateCpuUsage()}%";

        var mem = GetMemoryInfo();
        InfoRamTotal.Text = FormatBytes(mem.TotalPhysical);
        InfoRamUsed.Text = FormatBytes((long)(mem.TotalPhysical * (mem.LoadPercent / 100.0)));
        InfoRamAvail.Text = FormatBytes(mem.TotalPhysical - (long)(mem.TotalPhysical * (mem.LoadPercent / 100.0)));
        InfoRamLoad.Text = $"{mem.LoadPercent}%";

        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            InfoDiskDrive.Text = drive.Name.TrimEnd('\\');
            InfoDiskTotal.Text = FormatBytes(drive.TotalSize);
            InfoDiskFree.Text = FormatBytes(drive.TotalFreeSpace);
            InfoDiskFs.Text = drive.DriveFormat;
        }
        catch
        {
            InfoDiskDrive.Text = "Unknown";
            InfoDiskTotal.Text = "-";
            InfoDiskFree.Text = "-";
            InfoDiskFs.Text = "-";
        }

        InfoOsName.Text = RuntimeInformation.OSDescription;
        InfoOsVersion.Text = Environment.OSVersion.Version.ToString();
        InfoOsBuild.Text = Environment.OSVersion.Version.Build.ToString();
        InfoOsArch.Text = RuntimeInformation.OSArchitecture.ToString();
        InfoMachineName.Text = Environment.MachineName;
    }
}

public class TreeNode
{
    public bool IsGroup { get; set; }
    public string Company { get; set; } = "";
    public int Count { get; set; }
    public long TotalMemory { get; set; }
    public string TotalMemoryFormatted => MainWindow.FormatBytes(TotalMemory);
    public ProcessRow? Process { get; set; }
    public ObservableCollection<TreeNode> Children { get; } = [];
}

public class ProcessRow : INotifyPropertyChanged
{
    public int Id { get; init; }
    private string _name = "";
    public string Name { get => _name; set { _name = value; Notify(); } }
    private long _workingSetBytes;
    public long WorkingSetBytes { get => _workingSetBytes; set { _workingSetBytes = value; Notify(); Notify(nameof(WorkingSetFormatted)); } }
    public string WorkingSetFormatted => MainWindow.FormatBytes(WorkingSetBytes);
    private int _threadCount;
    public int ThreadCount { get => _threadCount; set { _threadCount = value; Notify(); } }
    private int _handleCount;
    public int HandleCount { get => _handleCount; set { _handleCount = value; Notify(); } }
    private string _company = "";
    public string Company { get => _company; set { _company = value; Notify(); } }
    private string _description = "";
    public string Description { get => _description; set { _description = value; Notify(); } }
    private string _executablePath = "";
    public string ExecutablePath { get => _executablePath; set { _executablePath = value; _iconSource = null; Notify(); Notify(nameof(IconSource)); } }
    private bool _isResponding;
    public bool IsResponding { get => _isResponding; set { _isResponding = value; Notify(); } }
    private bool _isWindows;
    public bool IsWindows { get => _isWindows; set { _isWindows = value; Notify(); } }
    private SolidColorBrush _tagColor = new(Colors.Gray);
    public SolidColorBrush TagColor { get => _tagColor; set { _tagColor = value; Notify(); } }
    private string _tagGlyph = "\uE8A9";
    public string TagGlyph { get => _tagGlyph; set { _tagGlyph = value; Notify(); } }
    private ImageSource? _iconSource;
    public ImageSource? IconSource
    {
        get
        {
            if (_iconSource is not null) return _iconSource;
            _iconSource = IconService.IconFromBytes(IconService.GetIconBytes(_executablePath));
            return _iconSource;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
