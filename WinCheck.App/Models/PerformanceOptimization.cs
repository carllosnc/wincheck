using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace WinCheck.Models;

public class PerformanceOptimization : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _status = "";
    private string _description = "";
    private string _currentValue = "";
    private bool _requiresReboot;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    public string Description
    {
        get => _description;
        set { _description = value; Notify(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; Notify(); Notify(nameof(Value)); }
    }

    public string CurrentValue
    {
        get => _currentValue;
        set { _currentValue = value; Notify(); }
    }

    public bool RequiresReboot
    {
        get => _requiresReboot;
        set { _requiresReboot = value; Notify(); Notify(nameof(RebootVisibility)); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; Notify(); Notify(nameof(HasStatus)); Notify(nameof(HasStatusVisibility)); }
    }

    public string Value => IsEnabled ? "On" : "Off";
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public Visibility HasStatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RebootVisibility => RequiresReboot ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
