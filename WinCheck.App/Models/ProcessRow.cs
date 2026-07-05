using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WinCheck.Services;
using WinCheck.UI;

namespace WinCheck.Models;

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
