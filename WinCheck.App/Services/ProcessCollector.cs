using System.Diagnostics;
using WinCheck.Models;

namespace WinCheck.Services;

public class ProcessCollector
{
    private static readonly HashSet<string> WindowsProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "System Idle Process", "Idle", "Registry", "Memory Compression",
        "Secure System", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "spoolsv", "dwm",
        "audiodg", "fontdrvhost", "LogonUI", "WUDFHost",
        "conhost", "sihost", "taskhostw", "RuntimeBroker",
        "ShellExperienceHost", "SearchIndexer", "SecurityHealthService",
        "MsMpEng", "NisSrv", "SystemSettings", "TextInputHost",
        "ctfmon", "dllhost", "SearchApp", "StartMenuExperienceHost",
        "Widgets", "explorer", "ApplicationFrameHost", "taskmgr",
        "wlms", "WmiPrvSE", "VSSVC"
    };

    public Task<ProcessListSnapshot> CollectAsync()
    {
        return Task.Run(() =>
        {
            var processes = new List<WinProcess>();
            Process[] rawProcs;

            try { rawProcs = Process.GetProcesses(); }
            catch { rawProcs = []; }

            foreach (var p in rawProcs)
            {
                try
                {
                    var proc = new WinProcess
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                    };

                    try { proc.WorkingSetBytes = p.WorkingSet64; } catch { }
                    try { proc.ThreadCount = p.Threads.Count; } catch { }
                    try { proc.HandleCount = p.HandleCount; } catch { }
                    try { proc.IsResponding = p.Responding; } catch { }

                    ProcessModule? mainModule = null;
                    try { mainModule = p.MainModule; } catch { }

                    if (mainModule != null)
                    {
                        try { proc.ExecutablePath = mainModule.FileName; } catch { }
                        try
                        {
                            var fvi = mainModule.FileVersionInfo;
                            proc.Company = fvi.CompanyName ?? "";
                            proc.Description = fvi.FileDescription ?? "";
                        }
                        catch { }
                    }

                    Classify(proc);
                    processes.Add(proc);
                }
                catch { }
                finally { p.Dispose(); }
            }

            return new ProcessListSnapshot { Processes = processes };
        });
    }

    private static void Classify(WinProcess proc)
    {
        if (WindowsProcessNames.Contains(proc.Name))
        {
            proc.Classification = proc.Name is "lsass" or "winlogon" or "smss" or "csrss"
                ? ProcessClassification.SystemCritical
                : ProcessClassification.System;
            return;
        }

        if (proc.Company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            proc.Classification = ProcessClassification.System;
            return;
        }

        if (!string.IsNullOrEmpty(proc.ExecutablePath))
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (proc.ExecutablePath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
            {
                proc.Classification = ProcessClassification.System;
                return;
            }
        }

        proc.Classification = ProcessClassification.ThirdParty;
    }
}
