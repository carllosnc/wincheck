using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinCheck.Models;

public class CleanupCategory : INotifyPropertyChanged
{
    private string _name = "";
    private string _description = "";
    private long _sizeBytes;
    private bool _isChecked = true;
    private string _status = "";
    private int _filesDeleted;
    private long _bytesFreed;

    public string Name { get => _name; set { _name = value; Notify(); } }
    public string Description { get => _description; set { _description = value; Notify(); } }
    public List<string> Paths { get; init; } = [];
    public bool RequiresAdmin { get; init; }

    public long SizeBytes
    {
        get => _sizeBytes;
        set { _sizeBytes = value; Notify(); Notify(nameof(SizeFormatted)); }
    }

    public string SizeFormatted => FolderInfo.FormatBytesLocal(SizeBytes);

    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; Notify(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; Notify(); Notify(nameof(StatusVisibility)); }
    }

    public int FilesDeleted
    {
        get => _filesDeleted;
        set { _filesDeleted = value; Notify(); Notify(nameof(CleanupSummary)); }
    }

    public long BytesFreed
    {
        get => _bytesFreed;
        set { _bytesFreed = value; Notify(); Notify(nameof(CleanupSummary)); }
    }

    public string CleanupSummary => FilesDeleted > 0
        ? $"{FilesDeleted} files | {FolderInfo.FormatBytesLocal(BytesFreed)} freed"
        : "";

    public bool StatusVisibility => !string.IsNullOrEmpty(Status);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
