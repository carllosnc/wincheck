using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public string Name { get => _name; set { _name = value; Notify(); } }
    public string Path { get => _path; set { _path = value; Notify(); } }
    public long SizeBytes { get => _sizeBytes; set { _sizeBytes = value; Notify(); Notify(nameof(SizeFormatted)); } }
    public int FileCount { get => _fileCount; set { _fileCount = value; Notify(); } }
    public int FolderCount { get => _folderCount; set { _folderCount = value; Notify(); } }

    public double Percentage
    {
        get => _percentage;
        set { _percentage = value; Notify(); Notify(nameof(PercentageFormatted)); }
    }

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
