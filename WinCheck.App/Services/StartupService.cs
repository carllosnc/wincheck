using System.IO;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunDisabledKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run_disabled";

    public static List<StartupEntry> ScanStartupEntries()
    {
        var entries = new List<StartupEntry>();

        ScanRegistryKey(Registry.CurrentUser, RunKeyPath, true, "Current User", entries);
        ScanRegistryKey(Registry.CurrentUser, RunDisabledKeyPath, false, "Current User", entries);
        ScanRegistryKey(Registry.LocalMachine, RunKeyPath, true, "All Users", entries);
        ScanRegistryKey(Registry.LocalMachine, RunDisabledKeyPath, false, "All Users", entries);

        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Current User Startup Folder", entries);
        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "All Users Startup Folder", entries);

        return entries;
    }

    private static void ScanRegistryKey(RegistryKey hive, string keyPath, bool isEnabled, string source, List<StartupEntry> entries)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var command = key.GetValue(valueName)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(command)) continue;

                entries.Add(new StartupEntry
                {
                    Name = valueName,
                    Command = command,
                    Source = source,
                    IsEnabled = isEnabled,
                    RegistrySource = hive == Registry.CurrentUser ? "HKCU" : "HKLM",
                    ValueName = valueName
                });
            }
        }
        catch { }
    }

    private static void ScanStartupFolder(string folderPath, string source, List<StartupEntry> entries)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return;

            foreach (var file in Directory.GetFiles(folderPath, "*.lnk"))
            {
                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Source = source,
                    IsEnabled = true,
                    RegistrySource = "StartupFolder",
                    ValueName = file
                });
            }

            foreach (var file in Directory.GetFiles(folderPath, "*.lnk.disabled"))
            {
                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)),
                    Command = file,
                    Source = source,
                    IsEnabled = false,
                    RegistrySource = "StartupFolder",
                    ValueName = file
                });
            }
        }
        catch { }
    }

    public static bool ToggleStartupEntry(StartupEntry entry)
    {
        if (entry.RegistrySource == "StartupFolder")
            return ToggleStartupFolder(entry);

        return ToggleRegistryEntry(entry);
    }

    private static bool ToggleStartupFolder(StartupEntry entry)
    {
        try
        {
            if (entry.IsEnabled)
            {
                var disabledPath = entry.ValueName + ".disabled";
                File.Move(entry.ValueName, disabledPath);
                entry.ValueName = disabledPath;
            }
            else
            {
                var originalPath = entry.ValueName.EndsWith(".disabled")
                    ? entry.ValueName[..^9]
                    : entry.ValueName;
                File.Move(entry.ValueName, originalPath);
                entry.ValueName = originalPath;
            }
            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch
        {
            entry.Status = "Failed";
            return false;
        }
    }

    private static bool ToggleRegistryEntry(StartupEntry entry)
    {
        var hive = entry.RegistrySource == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        var fromKey = entry.IsEnabled ? RunKeyPath : RunDisabledKeyPath;
        var toKey = entry.IsEnabled ? RunDisabledKeyPath : RunKeyPath;

        try
        {
            using var sourceKey = hive.OpenSubKey(fromKey, writable: true);
            if (sourceKey == null) return false;

            var value = sourceKey.GetValue(entry.ValueName);
            if (value == null) return false;

            sourceKey.DeleteValue(entry.ValueName);

            using var targetKey = hive.CreateSubKey(toKey);
            targetKey?.SetValue(entry.ValueName, value);

            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            entry.Status = "Requires admin";
            return false;
        }
        catch
        {
            entry.Status = "Failed";
            return false;
        }
    }
}
