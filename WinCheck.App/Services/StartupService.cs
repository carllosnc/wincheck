using System.Diagnostics;
using System.IO;
using System.Xml;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class StartupService
{
    private static readonly (RegistryKey Hive, string Path, string Source)[] RegistrySources =
    [
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Run",                    "HKCU Run"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run",                    "HKLM Run"),
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\RunOnce",                "HKCU RunOnce"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce",                "HKLM RunOnce"),
        (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",  "HKCU Policies"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",  "HKLM Policies"),
    ];

    private static readonly (string Path, string Source)[] FolderSources =
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.Startup),       "User Startup"),
        (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup"),
    ];

    public static List<StartupEntry> ScanStartupEntries()
    {
        var entries = new List<StartupEntry>();

        foreach (var (hive, path, source) in RegistrySources)
        {
            ReadKey(hive, path, isEnabled: true,  source,                  entries);
            ReadKey(hive, path, isEnabled: false, source + " (disabled)",  entries);
        }

        foreach (var (path, source) in FolderSources)
            ScanFolder(path, source, entries);

        ScanServices(entries);
        ScanScheduledTasks(entries);

        return entries;
    }

    private static void ReadKey(RegistryKey hive, string basePath, bool isEnabled, string source, List<StartupEntry> list)
    {
        var keyPath = isEnabled ? basePath : basePath + "_disabled";
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key == null)
            {
                if (!isEnabled) hive.CreateSubKey(keyPath)?.Dispose();
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
                    EntryType = StartupEntryType.Registry,
                    RegistrySource = hive == Registry.CurrentUser ? "HKCU" : "HKLM",
                    RegistryPath = basePath,
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
            EntryType = StartupEntryType.Folder,
            RegistrySource = "StartupFolder",
            ValueName = file
        };
    }

    private static void ScanServices(List<StartupEntry> list)
    {
        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (servicesKey == null) return;

            foreach (var svcName in servicesKey.GetSubKeyNames())
            {
                try
                {
                    using var svc = servicesKey.OpenSubKey(svcName);
                    if (svc == null) continue;

                    var start = svc.GetValue("Start");
                    if (start is not int startVal) continue;
                    if (startVal != 2) continue;

                    var displayName = svc.GetValue("DisplayName")?.ToString() ?? svcName;
                    var imagePath = svc.GetValue("ImagePath")?.ToString() ?? "";
                    var description = svc.GetValue("Description")?.ToString() ?? "";

                    list.Add(new StartupEntry
                    {
                        Name = displayName,
                        Command = imagePath,
                        Source = "Services",
                        Description = description,
                        IsEnabled = true,
                        EntryType = StartupEntryType.Service,
                        RegistrySource = "Service",
                        ValueName = svcName
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanScheduledTasks(List<StartupEntry> list)
    {
        try
        {
            var tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
            if (!Directory.Exists(tasksDir)) return;

            foreach (var file in Directory.EnumerateFiles(tasksDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(file);

                    var nsmgr = new XmlNamespaceManager(doc.NameTable);
                    nsmgr.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

                    var hasStartupTrigger = false;
                    var triggers = doc.SelectNodes("//t:Task/t:Triggers/*", nsmgr);
                    if (triggers != null)
                    {
                        foreach (XmlNode trigger in triggers)
                        {
                            switch (trigger.Name)
                            {
                                case "BootTrigger":
                                case "LogonTrigger":
                                    var enabled = trigger.SelectSingleNode("t:Enabled", nsmgr);
                                    if (enabled == null || enabled.InnerText != "false")
                                        hasStartupTrigger = true;
                                    break;
                            }
                        }
                    }

                    if (!hasStartupTrigger) continue;

                    var taskName = doc.SelectSingleNode("//t:Task/t:RegistrationInfo/t:URI", nsmgr)?.InnerText
                                   ?? Path.GetFileNameWithoutExtension(file);
                    taskName = taskName.TrimStart('\\');

                    var actions = doc.SelectNodes("//t:Task/t:Actions/t:Exec", nsmgr);
                    var command = "";
                    if (actions is { Count: > 0 } && actions[0] is XmlElement exec)
                    {
                        command = exec.SelectSingleNode("t:Command", nsmgr)?.InnerText ?? "";
                        var args = exec.SelectSingleNode("t:Arguments", nsmgr)?.InnerText ?? "";
                        if (!string.IsNullOrEmpty(args))
                            command += " " + args;
                    }

                    var enabledNode = doc.SelectSingleNode("//t:Task/t:Settings/t:Enabled", nsmgr);
                    var isEnabled = enabledNode == null || enabledNode.InnerText != "false";

                    var description = doc.SelectSingleNode("//t:Task/t:RegistrationInfo/t:Description", nsmgr)?.InnerText ?? "";

                    list.Add(new StartupEntry
                    {
                        Name = taskName,
                        Command = command,
                        Source = "Task Scheduler",
                        Description = description,
                        IsEnabled = isEnabled,
                        EntryType = StartupEntryType.ScheduledTask,
                        RegistrySource = "ScheduledTask",
                        ValueName = file
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    public static bool ToggleStartupEntry(StartupEntry entry)
    {
        return entry.EntryType switch
        {
            StartupEntryType.Folder => ToggleFolder(entry),
            StartupEntryType.Registry => ToggleReg(entry),
            StartupEntryType.Service => ToggleService(entry),
            StartupEntryType.ScheduledTask => ToggleScheduledTask(entry),
            _ => false
        };
    }

    private static bool ToggleFolder(StartupEntry entry)
    {
        try
        {
            string newPath;
            if (entry.IsEnabled)
            {
                newPath = entry.ValueName + ".disabled";
                File.Move(entry.ValueName, newPath);
            }
            else
            {
                newPath = entry.ValueName.EndsWith(".disabled") ? entry.ValueName[..^9] : entry.ValueName;
                File.Move(entry.ValueName, newPath);
            }
            entry.ValueName = newPath;
            entry.Command = newPath;
            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch { entry.Status = "Failed"; return false; }
    }

    private static bool ToggleReg(StartupEntry entry)
    {
        var hive = entry.RegistrySource == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
        var basePath = entry.RegistryPath;
        var fromPath = entry.IsEnabled ? basePath : basePath + "_disabled";
        var toPath = entry.IsEnabled ? basePath + "_disabled" : basePath;

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

    private static bool ToggleService(StartupEntry entry)
    {
        try
        {
            using var svc = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{entry.ValueName}", writable: true);
            if (svc == null) return false;

            svc.SetValue("Start", entry.IsEnabled ? 4 : 2, RegistryValueKind.DWord);
            entry.IsEnabled = !entry.IsEnabled;
            return true;
        }
        catch (UnauthorizedAccessException) { entry.Status = "Admin required"; return false; }
        catch { entry.Status = "Failed"; return false; }
    }

    private static bool ToggleScheduledTask(StartupEntry entry)
    {
        try
        {
            var action = entry.IsEnabled ? "/DISABLE" : "/ENABLE";
            var taskFile = entry.ValueName;

            var psi = new ProcessStartInfo("schtasks.exe", $"/Change /TN \"{Path.GetFileNameWithoutExtension(taskFile)}\" {action}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);

            if (proc?.ExitCode == 0)
            {
                entry.IsEnabled = !entry.IsEnabled;
                return true;
            }

            entry.Status = "Failed";
            return false;
        }
        catch { entry.Status = "Failed"; return false; }
    }
}
