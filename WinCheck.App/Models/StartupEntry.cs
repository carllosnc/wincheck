using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; Notify(); Notify(nameof(Status)); Notify(nameof(ToggleText)); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; Notify(); }
    }

    public string ToggleText => IsEnabled ? "Disable" : "Enable";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
