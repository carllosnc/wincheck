using System.IO;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class StartupService
{
    public static List<StartupEntry> ScanStartupEntries()
    {
        var entries = new List<StartupEntry>();

        ScanRegistryKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Current User", entries);
        ScanRegistryKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "All Users", entries);

        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Current User Startup Folder", entries);
        ScanStartupFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "All Users Startup Folder", entries);

        return entries;
    }

    private static void ScanRegistryKey(RegistryKey hive, string path, string source, List<StartupEntry> entries)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
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
                    IsEnabled = true,
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
        var disabledPath = entry.ValueName + ".disabled";
        try
        {
            if (entry.IsEnabled)
            {
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
        var keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        try
        {
            using var key = hive.OpenSubKey(keyPath, writable: true);
            if (key == null) return false;

            if (entry.IsEnabled)
            {
                key.DeleteValue(entry.ValueName);
            }
            else
            {
                key.SetValue(entry.ValueName, entry.Command);
            }

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
