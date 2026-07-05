using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using WinCheck.Services;

namespace WinCheck.Models;

public class StartupEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _command = "";
    private string _source = "";
    private bool _isEnabled;
    private string _status = "";

    public string Name { get => _name; set { _name = value; Notify(); } }
    public string Command { get => _command; set { _command = value; Notify(); } }
    public string Source { get => _source; set { _source = value; Notify(); } }
    public string RegistrySource { get; set; } = "";
    public string ValueName { get; set; } = "";

    public ImageSource? IconSource
    {
        get
        {
            if (_command.Length == 0) return null;
            var exe = ExtractExePath(_command);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            return IconService.IconFromBytes(IconService.GetIconBytes(exe));
        }
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

    private static string ExtractExePath(string command)
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
