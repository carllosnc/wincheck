using System.IO;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunDisabledKey = @"Software\Microsoft\Windows\CurrentVersion\Run_disabled";

    public static List<StartupEntry> ScanStartupEntries()
    {
        var entries = new List<StartupEntry>();

        ReadKey(Registry.CurrentUser, RunKey, true, "Current User", entries);
        ReadKey(Registry.CurrentUser, RunDisabledKey, false, "Current User (disabled)", entries);
        ReadKey(Registry.LocalMachine, RunKey, true, "All Users", entries);
        ReadKey(Registry.LocalMachine, RunDisabledKey, false, "All Users (disabled)", entries);

        ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup Folder", entries);
        ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup Folder", entries);

        return entries;
    }

    private static void ReadKey(RegistryKey hive, string path, bool isEnabled, string source, List<StartupEntry> list)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key == null)
            {
                if (!isEnabled)
                    hive.CreateSubKey(path)?.Dispose();
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
                list.Add(CreateFolderEntry(f, true, source));
            foreach (var f in Directory.GetFiles(path, "*.lnk.disabled"))
                list.Add(CreateFolderEntry(f, false, source));
        }
        catch { }
    }

    private static StartupEntry CreateFolderEntry(string file, bool enabled, string source)
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
        var fromPath = entry.IsEnabled ? RunKey : RunDisabledKey;
        var toPath = entry.IsEnabled ? RunDisabledKey : RunKey;

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
