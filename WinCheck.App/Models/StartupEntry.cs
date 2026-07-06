using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using WinCheck.Services;

namespace WinCheck.Models;

public enum StartupEntryType { Registry, Folder, ScheduledTask, Service, StoreApp }

public class StartupEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _command = "";
    private string _source = "";
    private bool _isEnabled;
    private string _status = "";
    private string _description = "";
    private StartupEntryType _entryType;

    public string Name { get => _name; set { _name = value; Notify(); } }
    public string Command { get => _command; set { _command = value; _iconSource = null; Notify(); Notify(nameof(IconSource)); } }
    public string Source { get => _source; set { _source = value; Notify(); } }
    public string RegistrySource { get; set; } = "";
    public string RegistryPath { get; set; } = "";
    public string ValueName { get; set; } = "";

    public string Description
    {
        get => _description;
        set { _description = value; Notify(); }
    }

    public StartupEntryType EntryType
    {
        get => _entryType;
        set { _entryType = value; Notify(); Notify(nameof(TypeLabel)); }
    }

    public string TypeLabel => EntryType switch
    {
        StartupEntryType.Registry => "Registry",
        StartupEntryType.Folder => "Startup Folder",
        StartupEntryType.ScheduledTask => "Scheduled Task",
        StartupEntryType.Service => "Service",
        StartupEntryType.StoreApp => "Store App",
        _ => ""
    };

    private ImageSource? _iconSource;
    public ImageSource? IconSource
    {
        get
        {
            if (_iconSource is not null) return _iconSource;
            if (_command.Length == 0) return GetGenericIcon();
            var exe = ExtractExePath(_command);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return GetGenericIcon();
            _iconSource = IconService.IconFromBytes(IconService.GetIconBytes(exe));
            return _iconSource ?? GetGenericIcon();
        }
    }

    private static ImageSource? _genericIcon;
    private static ImageSource GetGenericIcon()
    {
        if (_genericIcon is not null) return _genericIcon;
        try
        {
            using var icon = System.Drawing.SystemIcons.Application;
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            _genericIcon = IconService.IconFromBytes(ms.ToArray());
        }
        catch { }
        return _genericIcon!;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; Notify(); Notify(nameof(Status)); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; Notify(); }
    }

    public static string ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : "";
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
