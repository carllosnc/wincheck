using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.Storage.Streams;
using WinCheck.Models;
using WinCheck.Services;
using WinRT.Interop;

namespace WinCheck.UI;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<TreeNode> _treeNodes = [];
    private readonly Dictionary<int, ProcessRow> _pidIndex = [];
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _searchDebounce;
    private List<WinProcess> _allProcesses = [];
    private bool _autoRefresh = true;
    private DateTime _lastUpdate = DateTime.Now;
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _cpuInitialized;
    private ProcessRow? _selectedProcess;
    private string? _currentDrive;
    private string? _currentScanPath;
    private List<FolderInfo> _currentResults = [];
    private readonly Dictionary<string, List<FolderInfo>> _scanCache = new(StringComparer.OrdinalIgnoreCase);

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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void LoadProcesses(bool isAuto = false)
    {
        var snapshot = new ProcessCollector().CollectAsync().GetAwaiter().GetResult();
        _allProcesses = snapshot.Processes;

        var filter = SearchBox.Text;
        var processes = string.IsNullOrWhiteSpace(filter)
            ? snapshot.Processes
            : snapshot.Processes.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (p.Company?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                p.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            ).ToList();

        var incomingPids = new HashSet<int>();

        foreach (var p in processes)
        {
            incomingPids.Add(p.Id);

            if (_pidIndex.TryGetValue(p.Id, out var existing))
            {
                UpdateRow(existing, p);
            }
            else
            {
                var row = ToRow(p);
                _pidIndex[p.Id] = row;
                AddToGroup(row);
            }
        }

        var deadPids = _pidIndex.Keys.Where(k => !incomingPids.Contains(k)).ToList();
        foreach (var pid in deadPids)
        {
            if (_pidIndex.Remove(pid, out var row))
                RemoveFromGroup(row);
        }

        UpdateTreeNodes();
        SyncTreeToRootNodes();

        _lastUpdate = DateTime.Now;
        UpdateStats();
    }

    public void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Stop();
        _searchDebounce?.Start();
    }

    private void UpdateRow(ProcessRow row, WinProcess p)
    {
        row.WorkingSetBytes = p.WorkingSetBytes;
        row.ThreadCount = p.ThreadCount;
        row.HandleCount = p.HandleCount;
        row.IsResponding = p.IsResponding;
        row.Company = p.Company;
        row.Description = p.Description;
        row.ExecutablePath = p.ExecutablePath;
        row.IsWindows = p.IsWindows;
        row.TagColor = CreateTagColor(p.IsWindows);
        row.TagGlyph = p.IsWindows ? "\uE8A9" : "\uE91B";
    }

    private void AddToGroup(ProcessRow row)
    {
        var company = string.IsNullOrEmpty(row.Company) ? "Unknown" : row.Company;
        var node = _treeNodes.FirstOrDefault(n =>
            n.Company == company && n.IsGroup);

        if (node == null)
        {
            node = new TreeNode { IsGroup = true, Company = company };
            _treeNodes.Add(node);
        }

        node.Children.Add(new TreeNode
        {
            IsGroup = false,
            Process = row
        });
    }

    private void RemoveFromGroup(ProcessRow row)
    {
        foreach (var n in _treeNodes)
        {
            var child = n.Children.FirstOrDefault(c => c.Process == row);
            if (child != null)
            {
                n.Children.Remove(child);
                if (n.Children.Count == 0)
                    _treeNodes.Remove(n);
                return;
            }
        }
    }

    private void UpdateTreeNodes()
    {
        foreach (var n in _treeNodes)
        {
            n.TotalMemory = n.Children.Sum(c => c.Process?.WorkingSetBytes ?? 0);
            n.Count = n.Children.Count;

            var sorted = n.Children.OrderByDescending(c => c.Process?.WorkingSetBytes ?? 0).ToList();
            n.Children.Clear();
            foreach (var c in sorted) n.Children.Add(c);
        }

        var ordered = _treeNodes
            .OrderBy(n => n.Children.All(c => c.Process?.IsWindows == true) ? 1 : 0)
            .ThenBy(n => n.Company, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _treeNodes.Clear();
        foreach (var n in ordered) _treeNodes.Add(n);
    }

    private void SyncTreeToRootNodes()
    {
        if (_treeNodes.Count == 0)
        {
            ProcessTree.RootNodes.Clear();
            return;
        }

        var incomingCompanies = new HashSet<string>(_treeNodes.Select(t => t.Company), StringComparer.OrdinalIgnoreCase);

        for (int i = ProcessTree.RootNodes.Count - 1; i >= 0; i--)
        {
            if (ProcessTree.RootNodes[i].Content is TreeNode tn && tn.IsGroup && !incomingCompanies.Contains(tn.Company))
                ProcessTree.RootNodes.RemoveAt(i);
        }

        foreach (var groupTn in _treeNodes)
        {
            var existingGroup = ProcessTree.RootNodes
                .FirstOrDefault(n => n.Content is TreeNode tn && tn.IsGroup && tn.Company == groupTn.Company);

            if (existingGroup != null)
            {
                existingGroup.Content = groupTn;
                SyncGroupChildren(existingGroup, groupTn);
            }
            else
            {
                var groupNode = new TreeViewNode
                {
                    Content = groupTn,
                    IsExpanded = false
                };
                foreach (var childTn in groupTn.Children)
                    groupNode.Children.Add(new TreeViewNode { Content = childTn });
                ProcessTree.RootNodes.Add(groupNode);
            }
        }

        for (int targetIdx = 0; targetIdx < _treeNodes.Count; targetIdx++)
        {
            var targetCompany = _treeNodes[targetIdx].Company;
            for (int j = 0; j < ProcessTree.RootNodes.Count; j++)
            {
                if (ProcessTree.RootNodes[j].Content is not TreeNode tn || !tn.IsGroup || tn.Company != targetCompany) continue;
                if (j == targetIdx) break;
                var node = ProcessTree.RootNodes[j];
                ProcessTree.RootNodes.RemoveAt(j);
                ProcessTree.RootNodes.Insert(targetIdx, node);
                break;
            }
        }
    }

    private static void SyncGroupChildren(TreeViewNode groupNode, TreeNode groupTn)
    {
        var incomingPids = new HashSet<int>();
        foreach (var c in groupTn.Children)
        {
            if (c.Process != null) incomingPids.Add(c.Process.Id);
        }

        for (int i = groupNode.Children.Count - 1; i >= 0; i--)
        {
            if (groupNode.Children[i].Content is TreeNode tn && !tn.IsGroup && tn.Process != null && !incomingPids.Contains(tn.Process.Id))
                groupNode.Children.RemoveAt(i);
        }

        foreach (var childTn in groupTn.Children)
        {
            if (childTn.Process == null) continue;
            var pid = childTn.Process.Id;

            var existing = groupNode.Children
                .FirstOrDefault(c => c.Content is TreeNode tn && !tn.IsGroup && tn.Process?.Id == pid);

            if (existing != null)
            {
                existing.Content = childTn;
            }
            else
            {
                groupNode.Children.Add(new TreeViewNode { Content = childTn });
            }
        }

        for (int targetIdx = 0; targetIdx < groupTn.Children.Count; targetIdx++)
        {
            if (groupTn.Children[targetIdx].Process == null) continue;
            var targetPid = groupTn.Children[targetIdx].Process!.Id;

            for (int j = 0; j < groupNode.Children.Count; j++)
            {
                if (groupNode.Children[j].Content is not TreeNode tn || tn.IsGroup || tn.Process?.Id != targetPid) continue;
                if (j == targetIdx) break;
                var child = groupNode.Children[j];
                groupNode.Children.RemoveAt(j);
                groupNode.Children.Insert(targetIdx, child);
                break;
            }
        }
    }

    private void UpdateStats()
    {
        var winCount = _treeNodes
            .Where(n => n.Children.All(c => c.Process?.IsWindows == true))
            .Sum(n => n.Count);
        var total = _treeNodes.Sum(n => n.Count);
        var processBytes = _treeNodes.Sum(n => n.TotalMemory);
        var mem = GetMemoryInfo();
        var elapsed = (DateTime.Now - _lastUpdate).TotalSeconds;
        var mode = _autoRefresh ? $"Live ({elapsed:F0}s)" : "Manual";

        WinCount.Text = winCount.ToString();
        ThirdCount.Text = (total - winCount).ToString();
        TotalCount.Text = total.ToString();

        var cpuPct = CalculateCpuUsage();
        var (diskTotal, diskFree) = GetDiskInfo();
        var diskUsed = diskTotal - diskFree;
        var diskPct = diskTotal > 0 ? (int)(diskUsed * 100 / diskTotal) : 0;

        RamPercent.Text = $"{mem.LoadPercent}%";
        RamLabel.Text = $"{FormatBytes(processBytes)} / {FormatBytes(mem.TotalPhysical)}";
        RamBar.Value = mem.LoadPercent;

        CpuPercent.Text = $"{cpuPct}%";
        CpuLabel.Text = "System usage";
        CpuBar.Value = cpuPct;

        DiskPercent.Text = $"{diskPct}%";
        DiskLabel.Text = $"{FormatBytes(diskUsed)} / {FormatBytes(diskTotal)}";
        DiskBar.Value = diskPct;

        LiveLabel.Text = mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    private static (long TotalPhysical, int LoadPercent) GetMemoryInfo()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
            return ((long)mem.ullTotalPhys, (int)mem.dwMemoryLoad);
        return (0, 0);
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

    private static readonly ConcurrentDictionary<string, byte[]?> IconBytesCache = new(StringComparer.OrdinalIgnoreCase);

    internal static byte[]? GetIconBytes(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (IconBytesCache.TryGetValue(path, out var cached)) return cached;

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) { IconBytesCache[path] = null; return null; }
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = ms.ToArray();
            IconBytesCache[path] = bytes;
            return bytes;
        }
        catch
        {
            IconBytesCache[path] = null;
            return null;
        }
    }

    internal static ImageSource? IconFromBytes(byte[]? bytes)
    {
        if (bytes == null) return null;
        var bitmap = new BitmapImage();
        var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        }
        bitmap.SetSource(ras);
        return bitmap;
    }

    private int CalculateCpuUsage()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
            return 0;

        if (!_cpuInitialized)
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _cpuInitialized = true;
            return 0;
        }

        var idleDelta = idle - _prevIdleTime;
        var kernelDelta = kernel - _prevKernelTime;
        var userDelta = user - _prevUserTime;
        var totalDelta = kernelDelta + userDelta;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        if (totalDelta == 0) return 0;
        return (int)(100 - idleDelta * 100 / totalDelta);
    }

    private static (long Total, long Free) GetDiskInfo()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (root == null) return (0, 0);
            var drive = new DriveInfo(root);
            return (drive.TotalSize, drive.TotalFreeSpace);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void ToggleAutoRefresh()
    {
        var wasAuto = _autoRefresh;
        _autoRefresh = AutoToggle.IsChecked == true;
        if (_autoRefresh)
        {
            _timer?.Start();
            if (!wasAuto) LoadProcesses();
        }
        else
        {
            _timer?.Stop();
        }
    }

    private static ProcessRow ToRow(WinProcess p)
    {
        return new ProcessRow
        {
            Id = p.Id,
            Name = p.Name,
            WorkingSetBytes = p.WorkingSetBytes,
            ThreadCount = p.ThreadCount,
            HandleCount = p.HandleCount,
            Company = p.Company,
            Description = p.Description,
            ExecutablePath = p.ExecutablePath,
            IsResponding = p.IsResponding,
            IsWindows = p.IsWindows,
            TagColor = CreateTagColor(p.IsWindows),
            TagGlyph = p.IsWindows ? "\uE8A9" : "\uE91B"
        };
    }

    public async void OnKillProcess(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess == null) return;

        SetLoading($"Killing {_selectedProcess.Name}...", true);
        KillBtn.IsEnabled = false;

        try
        {
            await Task.Run(() =>
            {
                var proc = System.Diagnostics.Process.GetProcessById(_selectedProcess.Id);
                proc.Kill();
            });

            DetailPlaceholder.Text = $"Process {_selectedProcess.Name} (PID {_selectedProcess.Id}) terminated.";
            HideProcessDetails();
        }
        catch (Exception ex)
        {
            _ = ShowErrorDialog($"Failed to kill {_selectedProcess.Name}\n\n{ex.Message}");
        }
        finally
        {
            SetLoading(null, false);
        }
    }

    private void SetLoading(string? message, bool isLoading)
    {
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        LoadingLabel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) LoadingLabel.Text = message;
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = ((UIElement)Content).XamlRoot
        };
        await dialog.ShowAsync();
    }

    public void OnRefresh(object sender, RoutedEventArgs e) => LoadProcesses();
    public void OnToggleAutoChanged(object sender, RoutedEventArgs e) => ToggleAutoRefresh();

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
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
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

        var parent = System.IO.Path.GetDirectoryName(_currentScanPath);
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
        _currentDrive ??= System.IO.Path.GetPathRoot(path);

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

        await Task.Run(() => ScanDirectory(path, progress));

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

    public void OnNavCleanup(object sender, RoutedEventArgs e)
    {
        ProcessesView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;
        DiskView.Visibility = Visibility.Collapsed;
        CleanupView.Visibility = Visibility.Visible;
        NavCleanup.Style = (Style)Application.Current.Resources["SidebarButtonActiveStyle"];
        NavProcesses.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavInfo.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
        NavDisk.Style = (Style)Application.Current.Resources["SidebarButtonStyle"];
    }

    private List<CleanupCategory> _cleanupCategories = [];
    private bool _cleanupAnalyzed;

    public void OnCleanupToggleDetails(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CleanupCategory cat)
            cat.IsExpanded = !cat.IsExpanded;
    }

    public async void OnCleanupAnalyze(object sender, RoutedEventArgs e)
    {
        CleanupAnalyzeBtn.IsEnabled = false;
        CleanupRunBtn.IsEnabled = false;
        CleanupStatus.Text = "Analyzing...";
        _cleanupAnalyzed = false;

        _cleanupCategories = BuildCleanupCategories();
        CleanupList.ItemsSource = _cleanupCategories;

        foreach (var cat in _cleanupCategories)
        {
            cat.Status = "Scanning...";
            var (size, details) = await Task.Run(() => CalculateCategorySize(cat));
            cat.SizeBytes = size;
            cat.DetailsText = details;
            cat.Status = size > 0
                ? (cat.Name == "Recycle Bin"
                    ? $"{QueryRecycleBin().items} items found"
                    : $"{CountCategoryFiles(cat)} files found")
                : "Nothing to clean";
        }

        var total = _cleanupCategories.Where(c => c.SizeBytes > 0).Sum(c => c.SizeBytes);
        CleanupStatus.Text = total > 0
            ? $"Found {FolderInfo.FormatBytesLocal(total)} of cleanable data"
            : "Nothing to clean";

        CleanupRunBtn.IsEnabled = total > 0;
        CleanupAnalyzeBtn.IsEnabled = true;
        _cleanupAnalyzed = true;
    }

    public async void OnCleanupRun(object sender, RoutedEventArgs e)
    {
        if (!_cleanupAnalyzed) return;

        var isAdmin = IsAdministrator();
        var needsAdmin = _cleanupCategories.Any(c => c.IsChecked && c.SizeBytes > 0 && c.RequiresAdmin);

        if (needsAdmin && !isAdmin)
        {
            CleanupStatus.Text = "Run as administrator to clean system paths";
            ShowAdminCategories();
            CleanupAnalyzeBtn.IsEnabled = true;
            return;
        }

        CleanupAnalyzeBtn.IsEnabled = false;
        CleanupRunBtn.IsEnabled = false;
        var totalFreed = 0L;
        var totalFiles = 0;

        foreach (var cat in _cleanupCategories.Where(c => c.IsChecked && c.SizeBytes > 0))
        {
            cat.Status = "Cleaning...";
            var (files, freed) = await Task.Run(() => CleanCategory(cat));
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
    }

    private void ShowAdminCategories()
    {
        foreach (var cat in _cleanupCategories.Where(c => c.RequiresAdmin && c.SizeBytes > 0))
            cat.Status = "Requires admin rights";
    }

    private static List<CleanupCategory> BuildCleanupCategories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var temp = Path.GetTempPath();

        return
        [
            new CleanupCategory
            {
                Name = "Windows Temp Files",
                Description = "Temporary files in %TEMP% and Windows\\Temp",
                Paths = [temp, @"C:\Windows\Temp"],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Recycle Bin",
                Description = "Files in the recycle bin",
                Paths = [@"C:\`$Recycle.Bin"],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Chrome Cache",
                Description = "Google Chrome browser cache",
                Paths = [Path.Combine(localAppData, @"Google\Chrome\User Data")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Edge Cache",
                Description = "Microsoft Edge browser cache",
                Paths = [Path.Combine(localAppData, @"Microsoft\Edge\User Data")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Firefox Cache",
                Description = "Mozilla Firefox browser cache",
                Paths = [Path.Combine(appData, @"Mozilla\Firefox\Profiles")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Windows Update Cache",
                Description = "Windows Update download cache",
                Paths = [@"C:\Windows\SoftwareDistribution\Download"],
                RequiresAdmin = true
            },
            new CleanupCategory
            {
                Name = "Delivery Optimization",
                Description = "Windows Delivery Optimization files",
                Paths = [@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"],
                RequiresAdmin = true
            }
        ];
    }

    private static (long size, string details) CalculateCategorySize(CleanupCategory cat)
    {
        long total = 0;
        var details = new List<string>();

        if (cat.Name == "Recycle Bin")
        {
            var (size, items) = QueryRecycleBin();
            if (size > 0)
                details.Add($"  Recycle Bin  →  {FolderInfo.FormatBytesLocal(size)}  ({items} items)");
            return (size, string.Join("\n", details));
        }

        foreach (var path in cat.Paths)
        {
            try
            {
                if (!Directory.Exists(path)) continue;

                if (cat.Name == "Chrome Cache" || cat.Name == "Edge Cache")
                {
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "Cache");
                        if (Directory.Exists(cachePath))
                        {
                            var (size, files, _) = GetDirectorySize(cachePath);
                            if (size > 0)
                                details.Add($"  {cachePath}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files)");
                            total += size;
                        }
                    }
                }
                else if (cat.Name == "Firefox Cache")
                {
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cachePath))
                        {
                            var (size, files, _) = GetDirectorySize(cachePath);
                            if (size > 0)
                                details.Add($"  {cachePath}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files)");
                            total += size;
                        }
                    }
                }
                else
                {
                    var (size, files, folders) = GetDirectorySize(path);
                    if (size > 0)
                        details.Add($"  {path}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files, {folders} folders)");
                    total += size;
                }
            }
            catch { }
        }

        return (total, details.Count > 0 ? string.Join("\n", details) : "");
    }

    private static (int files, long freed) CleanCategory(CleanupCategory cat)
    {
        int files = 0;
        long freed = 0;
        bool isAdmin = IsAdministrator();

        foreach (var path in cat.Paths)
        {
            try
            {
                if (cat.Name == "Recycle Bin")
                {
                    var (size, items) = QueryRecycleBin();
                    EmptyRecycleBin();
                    files += (int)items;
                    freed += size;
                }
                else if (cat.Name == "Chrome Cache" || cat.Name == "Edge Cache")
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "Cache");
                        if (Directory.Exists(cachePath))
                            (files, freed) = DeleteContents(cachePath, files, freed);
                    }
                }
                else if (cat.Name == "Firefox Cache")
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cachePath))
                            (files, freed) = DeleteContents(cachePath, files, freed);
                    }
                }
                else
                {
                    if (!Directory.Exists(path)) continue;
                    if (!isAdmin && cat.RequiresAdmin) continue;
                    (files, freed) = DeleteContents(path, files, freed);
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (!isAdmin && cat.RequiresAdmin)
                    cat.Status = "Requires admin rights";
            }
            catch (Exception ex)
            {
                cat.Status = $"Error: {ex.Message}";
            }
        }

        return (files, freed);
    }

    private static (int files, long freed) DeleteContents(string path, int files, long freed)
    {
        var di = new DirectoryInfo(path);

        foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                if (file.IsReadOnly) file.Attributes = FileAttributes.Normal;
                freed += file.Length;
                file.Delete();
                files++;
            }
            catch { }
        }

        foreach (var dir in di.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            try
            {
                if ((dir.Attributes & FileAttributes.ReadOnly) != 0)
                    dir.Attributes = FileAttributes.Normal;
                Directory.Delete(dir.FullName, true);
            }
            catch { }
        }

        try
        {
            foreach (var file in di.EnumerateFiles("*"))
            {
                try { file.Delete(); files++; } catch { }
            }

            foreach (var dir in di.EnumerateDirectories("*"))
            {
                try
                {
                    if ((dir.Attributes & FileAttributes.ReadOnly) != 0)
                        dir.Attributes = FileAttributes.Normal;
                    Directory.Delete(dir.FullName, true);
                }
                catch { }
            }
        }
        catch { }

        return (files, freed);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int CountCategoryFiles(CleanupCategory cat)
    {
        try
        {
            foreach (var path in cat.Paths)
            {
                if (Directory.Exists(path))
                {
                    var (_, files, _) = GetDirectorySize(path);
                    return files;
                }
            }
        }
        catch { }
        return 0;
    }

    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private static (long size, long items) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var result = SHQueryRecycleBin(null, ref info);
        return result == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    private static void EmptyRecycleBin()
    {
        const uint SHERB_NOCONFIRMATION = 0x1;
        const uint SHERB_NOPROGRESSUI = 0x2;
        const uint SHERB_NOSOUND = 0x4;
        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }

    private static void ScanDirectory(string root, IProgress<FolderInfo> progress)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var (size, files, folders) = GetDirectorySize(dir);
                    progress.Report(new FolderInfo
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        SizeBytes = size,
                        FileCount = files,
                        FolderCount = folders
                    });
                }
                catch
                {
                    progress.Report(new FolderInfo
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        SizeBytes = 0,
                        FileCount = 0,
                        FolderCount = 0
                    });
                }
            }
        }
        catch { }
    }

    private static (long size, int files, int folders) GetDirectorySize(string path)
    {
        long size = 0;
        int files = 0;
        int folders = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch { }
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var (subSize, subFiles, subFolders) = GetDirectorySize(dir);
                    size += subSize;
                    files += subFiles;
                    folders += subFolders + 1;
                }
                catch { }
            }
        }
        catch { }

        return (size, files, folders);
    }

    public void OnProcessItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = (args.InvokedItem as TreeViewNode)?.Content as TreeNode;

        if (node != null && !node.IsGroup && node.Process != null)
        {
            _selectedProcess = node.Process;
            ShowProcessDetails(node.Process);
        }
        else
        {
            _selectedProcess = null;
            HideProcessDetails();
        }
    }

    private void ShowProcessDetails(ProcessRow row)
    {
        DetailPlaceholder.Visibility = Visibility.Collapsed;
        DetailGrid.Visibility = Visibility.Visible;
        KillBtn.IsEnabled = true;

        DetailName.Text = row.Name;
        DetailPid.Text = row.Id.ToString();
        DetailType.Text = row.IsWindows ? "Windows" : "Third-party";
        DetailMemory.Text = row.WorkingSetFormatted;
        DetailThreads.Text = row.ThreadCount.ToString();
        DetailHandles.Text = row.HandleCount.ToString();
        DetailCompany.Text = row.Company;
        DetailDesc.Text = row.Description;
        DetailResponding.Text = row.IsResponding ? "Yes" : "No";
        DetailPath.Text = row.ExecutablePath.Length > 100
            ? row.ExecutablePath[..100] + "..."
            : row.ExecutablePath;
    }

    private void HideProcessDetails()
    {
        DetailPlaceholder.Visibility = Visibility.Visible;
        DetailGrid.Visibility = Visibility.Collapsed;
        KillBtn.IsEnabled = false;
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

public sealed class TreeNodeTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate GroupTemplate = BuildGroupTemplate();
    private static readonly DataTemplate ProcessTemplate = BuildProcessTemplate();

    protected override DataTemplate SelectTemplateCore(object item)
    {
        var node = (item as TreeViewNode)?.Content as TreeNode ?? item as TreeNode;
        if (node is { IsGroup: true }) return GroupTemplate;
        return ProcessTemplate;
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    private static DataTemplate BuildGroupTemplate()
    {
        var xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                        BorderBrush="{ThemeResource SurfaceStrokeColorDefaultBrush}"
                        BorderThickness="0,0,0,1" Padding="8,5">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="52"/><ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="72"/><ColumnDefinition Width="86"/><ColumnDefinition Width="58"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Grid.ColumnSpan="3" FontWeight="SemiBold"
                                   Foreground="{ThemeResource AccentFillColorDefaultBrush}"
                                   TextTrimming="CharacterEllipsis" VerticalAlignment="Center">
                            <Run Text="{Binding Content.Company}"/><Run Text="  ("/><Run Text="{Binding Content.Count}"/><Run Text=")"/>
                        </TextBlock>
                        <TextBlock Grid.Column="3" Text="{Binding Content.TotalMemoryFormatted}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   HorizontalAlignment="Right" VerticalAlignment="Center"/>
                        <TextBlock Grid.Column="4" Text="{Binding Content.Count}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Grid>
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)XamlReader.Load(xaml);
    }

    private static DataTemplate BuildProcessTemplate()
    {
        var xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        BorderBrush="{ThemeResource SurfaceStrokeColorDefaultBrush}"
                        BorderThickness="0,0,0,1" Padding="8,3">
                    <Grid MinHeight="26">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="52"/><ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="72"/><ColumnDefinition Width="86"/><ColumnDefinition Width="58"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                            <Grid Width="16" Height="16">
                                <FontIcon Glyph="{Binding Content.Process.TagGlyph}" FontSize="14"
                                          Foreground="{Binding Content.Process.TagColor}"
                                          HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                <Image Source="{Binding Content.Process.IconSource}"
                                       Width="16" Height="16" Stretch="Uniform"/>
                            </Grid>
                        </StackPanel>
                        <TextBlock Grid.Column="1" Text="{Binding Content.Process.Name}"
                                   VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="2" Text="{Binding Content.Process.Id}"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Right"/>
                        <TextBlock Grid.Column="3" Text="{Binding Content.Process.WorkingSetFormatted}"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Right"/>
                        <TextBlock Grid.Column="4" Text="{Binding Content.Process.ThreadCount}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"/>
                    </Grid>
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)XamlReader.Load(xaml);
    }
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
            _iconSource = MainWindow.IconFromBytes(MainWindow.GetIconBytes(_executablePath));
            return _iconSource;
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
