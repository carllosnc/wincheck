using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinCheck.Models;

public class FolderInfo : INotifyPropertyChanged
{
    private string _name = "";
    private string _path = "";
    private long _sizeBytes;
    private int _fileCount;
    private int _folderCount;
    private double _percentage;
    private double _barWidth;
    private SolidColorBrush? _barBrush;

    private static readonly HashSet<string> CleanupPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Temp", "tmp", "Cache", ".cache", "caches", "Logs", "logs",
        "CrashDumps", "Crash Reports", "Recent", "Prefetch"
    };

    private static readonly SolidColorBrush RedBrush = new(Microsoft.UI.Colors.Red);
    private static readonly SolidColorBrush OrangeBrush = new(Microsoft.UI.Colors.Orange);
    private static readonly SolidColorBrush GreenBrush = new(Microsoft.UI.Colors.LimeGreen);

    public string Name { get => _name; set { _name = value; Notify(); } }
    public string Path { get => _path; set { _path = value; Notify(); } }
    public long SizeBytes { get => _sizeBytes; set { _sizeBytes = value; Notify(); Notify(nameof(SizeFormatted)); } }
    public int FileCount { get => _fileCount; set { _fileCount = value; Notify(); } }
    public int FolderCount { get => _folderCount; set { _folderCount = value; Notify(); } }

    public double Percentage
    {
        get => _percentage;
        set { _percentage = value; _barBrush = null; Notify(); Notify(nameof(PercentageFormatted)); Notify(nameof(BarBrush)); }
    }

    public SolidColorBrush BarBrush
    {
        get
        {
            if (_barBrush is not null) return _barBrush;
            _barBrush = Percentage switch
            {
                >= 50 => RedBrush,
                >= 25 => OrangeBrush,
                _ => GreenBrush
            };
            return _barBrush;
        }
    }

    public bool IsCleanupCandidate => CleanupPatterns.Contains(Name);
    public Visibility CleanupVisibility => IsCleanupCandidate ? Visibility.Visible : Visibility.Collapsed;

    public double BarWidth
    {
        get => _barWidth;
        set { _barWidth = value; Notify(); }
    }

    public string SizeFormatted => FormatBytesLocal(SizeBytes);

    public string PercentageFormatted => $"{Percentage:F1}%";

    public static string FormatBytesLocal(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F0} MB",
        >= 1_024 => $"{bytes / 1_024.0:F0} KB",
        _ => $"{bytes} B"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
