# WinCheck — Agent Guide

## Overview

WinCheck is a Windows system utility built with **WinUI 3** + **.NET 8**. It provides process management, system monitoring, disk analysis, and system cleanup — similar to Task Manager + CCleaner.

- **Framework**: .NET 8, WinUI 3 (Windows App SDK 1.7)
- **Target**: Windows 10 19041+ (unpackaged Win32 app)
- **Language**: C# 12, XAML
- **UI**: Single-window with sidebar navigation, dark theme

## Project Structure

```
wincheck.slnx                          # Solution file (.NET 10 SDK format)
WinCheck.App/
├── App.xaml                           # Application resources, styles, themes
├── App.xaml.cs                        # Entry point (OnLaunched → MainWindow)
├── app.manifest                       # Win32 app manifest
├── WinCheck.App.csproj                # Project file
├── Models/
│   ├── WinProcess.cs                  # Process entity (id, name, memory, etc.)
│   ├── ProcessListSnapshot.cs         # Collection wrapper
│   ├── ProcessClassification.cs       # Enum: System / SystemCritical / ThirdParty
│   ├── ProcessRow.cs                  # UI-bound process row with INotifyPropertyChanged
│   ├── TreeNode.cs                    # Tree node for process grouping
│   ├── FolderInfo.cs                  # Disk folder info (size, files, bar color)
│   └── CleanupCategory.cs            # Cleanup category (paths, size, status)
├── Services/
│   ├── ProcessCollector.cs            # Process enumeration + classification
│   ├── IconService.cs                 # Executable icon extraction + cache
│   ├── DiskService.cs                 # Directory scanning + size calculation
│   └── CleanupService.cs              # Cleanup logic + Win32 P/Invoke
└── UI/
    ├── MainWindow.xaml                # Full layout (all 4 views inline)
    ├── MainWindow.xaml.cs             # Shell: ctor, nav, keyboard, loading, helpers (239 lines)
    ├── MainWindow.Processes.cs        # Process tree, stats, kill (partial class)
    ├── MainWindow.Disk.cs             # Disk scanner, drill-down, sort (partial class)
    ├── MainWindow.Cleanup.cs          # Cleanup analyze/clean (partial class)
    └── Controls/
        └── TreeNodeTemplateSelector.cs # DataTemplate selector for TreeView groups/items
```

## Architecture

### Navigation

Single-window app with **4 views** switched via sidebar visibility toggling:

| Tab | View Grid | Partial Class |
|-----|-----------|---------------|
| Processes | `ProcessesView` (Grid) | `MainWindow.Processes.cs` |
| System Info | `InfoView` (ScrollViewer) | `MainWindow.xaml.cs` (small) |
| Disk | `DiskView` (Grid) | `MainWindow.Disk.cs` |
| Cleanup | `CleanupView` (Grid) | `MainWindow.Cleanup.cs` |

Navigation handlers (`OnNavProcesses`, `OnNavInfo`, `OnNavDisk`, `OnNavCleanup`) toggle `Visibility` and swap sidebar button styles between `SidebarButtonStyle` / `SidebarButtonActiveStyle`.

### Process Tree

- Data flows: `ProcessCollector.CollectAsync()` → `List<WinProcess>` → `ProcessRow` (UI model) → grouped into `TreeNode` hierarchy
- Tree rendering: `TreeNode` → `TreeViewNode` (via `SyncTreeToRootNodes`) → `TreeView` with `TreeNodeTemplateSelector`
- Refresh: 3-second `DispatcherQueueTimer` calls `LoadProcesses()` which syncs incrementally (adds/removes/reorders TreeViewNodes in-place to preserve expand/collapse state)
- Templates: dynamically built via `XamlReader.Load()` with `{Binding Content.xxx}` paths (DataContext is `TreeViewNode`, content is `TreeNode`)

### Disk Scanner

- Scan runs on background thread via `Task.Run(() => DiskService.ScanDirectory(...))`
- Results cached in `Dictionary<string, List<FolderInfo>>` by path — back navigation returns instantly
- Drill-down: click folder → scan subfolder → breadcrumb + back button

### Cleanup

- 7 categories: Temp files, Recycle Bin, Chrome/Edge/Firefox caches, Windows Update, Delivery Optimization
- Recycle Bin uses `SHQueryRecycleBin` / `SHEmptyRecycleBin` (shell32)
- Admin check: `WindowsPrincipal.IsInRole(Administrator)` — system paths skipped if not elevated
- File deletion: removes read-only attributes before delete, handles `UnauthorizedAccessException`

### Icons

- `IconService`: extracts `.exe` icons via `System.Drawing.Icon.ExtractAssociatedIcon()`, converts to PNG bytes, caches in `ConcurrentDictionary<string, byte[]>`
- `ProcessRow.IconSource`: lazy-loaded `ImageSource`, re-evaluated when `ExecutablePath` changes

## Code Conventions

### Naming
- Private fields: `_camelCase` (`_treeNodes`, `_selectedProcess`)
- Public properties: `PascalCase` (`IsWindows`, `WorkingSetFormatted`)
- Methods: `PascalCase` (`LoadProcesses`, `SyncTreeToRootNodes`)
- XAML named elements: `PascalCase` (`ProcessTree`, `SearchBox`, `KillBtn`)
- Event handlers: `On` prefix (`OnNavProcesses`, `OnKillProcess`, `OnRefresh`)

### Patterns
- **MVVM-light**: Models implement `INotifyPropertyChanged`, UI binds directly
- **Partial classes**: `MainWindow` split by feature into `MainWindow.*.cs`
- **P/Invoke**: Native calls grouped in the class that uses them (e.g., `CleanupService` for shell32)
- **Error handling**: try/catch with silent fallback for non-critical operations (file enumeration, icon extraction)

### Spacing / Layout
- 12px horizontal gaps between panels, 8px vertical gaps inside panels
- `CornerRadius="6"` on all cards/panels
- Theme resources only (no hardcoded colors except kill button `#C50514` and bar colors in `FolderInfo`)

### WinUI 3 TreeView Gotchas
- **Use `RootNodes`, not `ItemsSource`** for hierarchical data
- DataContext of templates is `TreeViewNode`, not your content → bind with `{Binding Content.xxx}`
- `ItemTemplateSelector.SelectTemplateCore()` receives `TreeViewNode` when using `RootNodes`
- `ItemInvokedEventArgs.InvokedItem` is `TreeViewNode`, extract content via `.Content`

## Build & Run

```powershell
# Build
dotnet build WinCheck.App

# Run
.\WinCheck.App\bin\Debug\net8.0-windows10.0.19041.0\WinCheck.App.exe

# Kill running instance (often needed before rebuild)
taskkill /F /IM WinCheck.App.exe
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.WindowsAppSDK` | 1.7.250310001 | WinUI 3 runtime |
| `System.Drawing.Common` | 8.0.10 | Icon extraction |

## Key Win32 APIs Used

| API | DLL | Purpose |
|-----|-----|---------|
| `GlobalMemoryStatusEx` | kernel32 | RAM stats |
| `GetSystemTimes` | kernel32 | CPU usage calculation |
| `GetAsyncKeyState` | user32 | Keyboard shortcut detection |
| `SHEmptyRecycleBin` | shell32 | Empty recycle bin |
| `SHQueryRecycleBin` | shell32 | Recycle bin size/items |

## Backlog

See [BACKLOG.md](BACKLOG.md) for prioritized improvement tasks and [GitHub Issues](https://github.com/carllosnc/wincheck/issues) for tracked work items.

Labels: `p1` (next), `p2` (upcoming), `p3` (future) × `processes`, `system-info`, `disk`, `cleanup`, `general`.
