using System.IO;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class StartupService
{
    private static readonly (RegistryKey Hive, string Path, string Source, bool IsRun)[] RegistrySources =
    [
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Run",                    "HKCU Run",           true),
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Run_disabled",            "HKCU Run (disabled)", false),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run",                    "HKLM Run",           true),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run_disabled",            "HKLM Run (disabled)", false),
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\RunOnce",                 "HKCU RunOnce",       true),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce",                 "HKLM RunOnce",       true),
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",   "HKCU Policies",      true),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",   "HKLM Policies",      true),
    ];

    private static readonly (string Path, string Source)[] FolderSources =
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.Startup),       "User Startup"),
        (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup"),
    ];

    public static List<StartupEntry> ScanStartupEntries()
    {
        var entries = new List<StartupEntry>();

        foreach (var (hive, path, source, isEnabled) in RegistrySources)
            ReadKey(hive, path, isEnabled, source, entries);

        foreach (var (path, source) in FolderSources)
            ScanFolder(path, source, entries);

        return entries;
    }

    private static void ReadKey(RegistryKey hive, string path, bool isEnabled, string source, List<StartupEntry> list)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key == null)
            {
                if (!isEnabled) hive.CreateSubKey(path)?.Dispose();
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                var cmd = key.GetValue(name)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                list.Add(new StartupEntry
                {
                    Name = name,
                    Command = cmd,
                    Source = source,
                    IsEnabled = isEnabled,
                    RegistrySource = hive == Registry.CurrentUser ? "HKCU" : "HKLM",
                    ValueName = name
                });
            }
        }
        catch { }
    }

    private static void ScanFolder(string path, string source, List<StartupEntry> list)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.GetFiles(path, "*.lnk"))
                list.Add(MakeFolderEntry(f, true, source));
            foreach (var f in Directory.GetFiles(path, "*.lnk.disabled"))
                list.Add(MakeFolderEntry(f, false, source));
        }
        catch { }
    }

    private static StartupEntry MakeFolderEntry(string file, bool enabled, string source)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name.EndsWith(".lnk")) name = name[..^4];

        return new StartupEntry
        {
            Name = name,
            Command = file,
            Source = enabled ? source : source + " (disabled)",
            IsEnabled = enabled,
            RegistrySource = "StartupFolder",
            ValueName = file
        };
    }

    public static bool ToggleStartupEntry(StartupEntry entry)
    {
        if (entry.RegistrySource == "StartupFolder")
            return ToggleFolder(entry);
        return ToggleReg(entry);
    }

    private static bool ToggleFolder(StartupEntry entry)
    {
        try
        {
            if (entry.IsEnabled)
            {
                var dst = entry.ValueName + ".disabled";
                File.Move(entry.ValueName, dst);
                entry.ValueName = dst;
            }
            else
            {
                var src = entry.ValueName.EndsWith(".disabled") ? entry.ValueName[..^9] : entry.ValueName;
                File.Move(entry.ValueName, src);
                entry.ValueName = src;
            }
            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch { entry.Status = "Failed"; return false; }
    }

    private static bool ToggleReg(StartupEntry entry)
    {
        var hive = entry.RegistrySource == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        var runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var disabledKey = runKey + "_disabled";

        var fromPath = entry.IsEnabled ? runKey : disabledKey;
        var toPath = entry.IsEnabled ? disabledKey : runKey;

        try
        {
            using var src = hive.OpenSubKey(fromPath, writable: true);
            if (src == null) return false;

            var value = src.GetValue(entry.ValueName);
            if (value == null) return false;
            src.DeleteValue(entry.ValueName);

            using var dst = hive.CreateSubKey(toPath);
            if (dst == null) return false;
            dst.SetValue(entry.ValueName, value);

            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch (UnauthorizedAccessException) { entry.Status = "Admin required"; return false; }
        catch { entry.Status = "Failed"; return false; }
    }
}
