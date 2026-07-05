using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

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
    private bool _isExpanded;
    private string _detailsText = "";

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

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); Notify(nameof(DetailsVisibility)); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; Notify(); Notify(nameof(HasStatus)); Notify(nameof(HasStatusVisibility)); }
    }

    public string DetailsText
    {
        get => _detailsText;
        set { _detailsText = value; Notify(); Notify(nameof(HasDetails)); }
    }

    public bool HasDetails => !string.IsNullOrEmpty(DetailsText);
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public Visibility DetailsVisibility => IsExpanded && HasDetails ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility SummaryVisibility => FilesDeleted > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasDetailsVisibility => HasDetails ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HasStatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
