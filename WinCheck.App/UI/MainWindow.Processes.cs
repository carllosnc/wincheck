using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinCheck.Models;
using WinCheck.Services;

namespace WinCheck.UI;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<TreeNode> _treeNodes = [];
    private readonly Dictionary<int, ProcessRow> _pidIndex = [];
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _searchDebounce;
    private List<WinProcess> _allProcesses = [];
    private bool _autoRefresh = true;
    private bool _isLoadingProcesses;
    private DateTime _lastUpdate = DateTime.Now;
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _cpuInitialized;
    private ProcessRow? _selectedProcess;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

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

    private async Task LoadProcessesAsync(bool isAuto = false)
    {
        if (_isLoadingProcesses) return;
        _isLoadingProcesses = true;
        var snapshot = await new ProcessCollector().CollectAsync();
        _isLoadingProcesses = false;
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
        row.IsCritical = p.Classification == ProcessClassification.SystemCritical;
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

    private static (long TotalPhysical, int LoadPercent) GetMemoryInfo()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
            return ((long)mem.ullTotalPhys, (int)mem.dwMemoryLoad);
        return (0, 0);
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
            if (!wasAuto) _ = LoadProcessesAsync();
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
            IsCritical = p.Classification == ProcessClassification.SystemCritical,
            TagColor = CreateTagColor(p.IsWindows),
            TagGlyph = p.IsWindows ? "\uE8A9" : "\uE91B"
        };
    }

    public async void OnKillProcess(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess == null) return;

        var root = ((UIElement)Content).XamlRoot;

        if (_selectedProcess.IsCritical)
        {
            var block = new ContentDialog
            {
                Title = "Critical system process",
                Content = $"{_selectedProcess.Name} (PID {_selectedProcess.Id}) is a critical Windows process. Ending it will log you off or shut down Windows, so it cannot be ended from here.",
                CloseButtonText = "OK",
                XamlRoot = root
            };
            await block.ShowAsync();
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "End process",
            Content = $"End \"{_selectedProcess.Name}\" (PID {_selectedProcess.Id})? Unsaved work will be lost.",
            PrimaryButtonText = "End process",
            CloseButtonText = "Cancel",
            XamlRoot = root
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

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

    public async void OnRefresh(object sender, RoutedEventArgs e) => await LoadProcessesAsync();
    public void OnToggleAutoChanged(object sender, RoutedEventArgs e) => ToggleAutoRefresh();

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
