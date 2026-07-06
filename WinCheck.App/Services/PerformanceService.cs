using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinCheck.Models;

namespace WinCheck.Services;

public static class PerformanceService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr userRootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid, StringBuilder buffer, ref int bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    private const uint SPI_SETANIMATION = 0x0049;
    private const uint SPI_GETANIMATION = 0x0048;
    private const uint SPI_SETDRAGFULLWINDOWS = 0x0025;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    private static readonly Guid HighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2f");

    [StructLayout(LayoutKind.Sequential)]
    private struct ANIMATIONINFO
    {
        public int cbSize;
        public int iMinAnimate;
    }

    public static List<PerformanceOptimization> GetOptimizations()
    {
        return
        [
            new PerformanceOptimization
            {
                Id = "VisualEffects",
                Name = "Visual Effects",
                Description = "Disable animations, transitions and shadows to improve responsiveness",
                IsEnabled = GetVisualEffectsState(),
                CurrentValue = GetVisualEffectsState() ? "Default" : "Performance"
            },
            new PerformanceOptimization
            {
                Id = "PowerPlan",
                Name = "Power Plan",
                Description = "Switch to High Performance mode for maximum speed (uses more energy)",
                IsEnabled = GetPowerPlanState(),
                CurrentValue = GetPowerPlanName()
            },
            new PerformanceOptimization
            {
                Id = "BackgroundApps",
                Name = "Background Apps",
                Description = "Prevent apps from running in the background to save resources",
                IsEnabled = GetBackgroundAppsState(),
                CurrentValue = GetBackgroundAppsState() ? "Allowed" : "Blocked"
            },
            new PerformanceOptimization
            {
                Id = "SearchIndexing",
                Name = "Search Indexing",
                Description = "Disable Windows Search indexing to reduce disk and CPU overhead",
                IsEnabled = GetSearchIndexingState(),
                CurrentValue = GetSearchIndexingState() ? "Enabled" : "Disabled"
            },
            new PerformanceOptimization
            {
                Id = "GameMode",
                Name = "Game Mode",
                Description = "Prioritize game performance by allocating more system resources",
                IsEnabled = GetGameModeState(),
                CurrentValue = GetGameModeState() ? "On" : "Off"
            },
            new PerformanceOptimization
            {
                Id = "GpuScheduling",
                Name = "GPU Scheduling",
                Description = "Hardware-accelerated GPU scheduling reduces latency",
                IsEnabled = GetGpuSchedulingState(),
                CurrentValue = GetGpuSchedulingState() ? "On" : "Off",
                RequiresReboot = true
            },
        ];
    }

    public static (bool Success, string Message) Toggle(PerformanceOptimization opt)
    {
        return opt.Id switch
        {
            "VisualEffects" => ToggleVisualEffects(opt),
            "PowerPlan" => TogglePowerPlan(opt),
            "BackgroundApps" => ToggleBackgroundApps(opt),
            "SearchIndexing" => ToggleSearchIndexing(opt),
            "GameMode" => ToggleGameMode(opt),
            "GpuScheduling" => ToggleGpuScheduling(opt),
            _ => (false, "Unknown optimization")
        };
    }

    private static bool GetVisualEffectsState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            if (key?.GetValue("EnableAnimations") is int val)
                return val != 0;

            var info = new ANIMATIONINFO { cbSize = Marshal.SizeOf<ANIMATIONINFO>() };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<ANIMATIONINFO>());
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                SystemParametersInfo(SPI_GETANIMATION, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ptr, 0);
                info = Marshal.PtrToStructure<ANIMATIONINFO>(ptr);
                return info.iMinAnimate != 0;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { return true; }
    }

    private static (bool, string) ToggleVisualEffects(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                key?.SetValue("EnableAnimations", enable ? 1 : 0, RegistryValueKind.DWord);

            var info = new ANIMATIONINFO { cbSize = Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = enable ? 1 : 0 };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<ANIMATIONINFO>());
            Marshal.StructureToPtr(info, ptr, false);
            SystemParametersInfo(SPI_SETANIMATION, (uint)Marshal.SizeOf<ANIMATIONINFO>(), ptr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Marshal.FreeHGlobal(ptr);

            var clientVal = enable ? 1 : 0;
            var clientPtr = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(clientPtr, clientVal);
            SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, clientPtr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Marshal.FreeHGlobal(clientPtr);

            SystemParametersInfo(SPI_SETDRAGFULLWINDOWS, enable ? 1u : 0u, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            opt.IsEnabled = enable;
            opt.CurrentValue = enable ? "Default" : "Performance";
            return (true, enable ? "Visual effects restored to default" : "Visual effects disabled for performance");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }

    private static bool GetPowerPlanState()
    {
        try
        {
            var activePtr = IntPtr.Zero;
            if (PowerGetActiveScheme(IntPtr.Zero, out activePtr) != 0)
                return false;
            var activeGuid = Marshal.PtrToStructure<Guid>(activePtr);
            LocalFree(activePtr);
            return activeGuid == HighPerformanceGuid;
        }
        catch { return false; }
    }

    private static string GetPowerPlanName()
    {
        try
        {
            var activePtr = IntPtr.Zero;
            if (PowerGetActiveScheme(IntPtr.Zero, out activePtr) != 0)
                return "Unknown";
            var activeGuid = Marshal.PtrToStructure<Guid>(activePtr);
            LocalFree(activePtr);

            var sb = new StringBuilder(128);
            var size = 128;
            if (PowerReadFriendlyName(IntPtr.Zero, ref activeGuid, IntPtr.Zero, IntPtr.Zero, sb, ref size) == 0)
                return sb.ToString().TrimEnd('\0');
            return activeGuid == HighPerformanceGuid ? "High Performance"
                 : activeGuid == BalancedGuid ? "Balanced"
                 : "Custom";
        }
        catch { return "Unknown"; }
    }

    private static (bool, string) TogglePowerPlan(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;
            var targetGuid = enable ? HighPerformanceGuid : BalancedGuid;

            var result = PowerSetActiveScheme(IntPtr.Zero, ref targetGuid);
            if (result != 0)
                return (false, $"Failed to change power plan (error {result})");

            opt.IsEnabled = enable;
            opt.CurrentValue = GetPowerPlanName();
            return (true, $"Power plan changed to {opt.CurrentValue}");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }

    private static bool GetBackgroundAppsState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
            if (key?.GetValue("BackgroundAppGlobalToggle") is int val)
                return val != 0;
            return true;
        }
        catch { return true; }
    }

    private static (bool, string) ToggleBackgroundApps(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
            key?.SetValue("BackgroundAppGlobalToggle", enable ? 1 : 0, RegistryValueKind.DWord);

            opt.IsEnabled = enable;
            opt.CurrentValue = enable ? "Allowed" : "Blocked";
            return (true, enable ? "Background apps allowed" : "Background apps blocked");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }

    private static bool GetSearchIndexingState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch");
            if (key?.GetValue("Start") is int start)
                return start != 4;
            return true;
        }
        catch { return true; }
    }

    private static (bool, string) ToggleSearchIndexing(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WSearch", writable: true);
            if (key == null) return (false, "Cannot access service registry (admin required)");

            key.SetValue("Start", enable ? 2 : 4, RegistryValueKind.DWord);

            try
            {
                var action = enable ? "start" : "stop";
                var psi = new ProcessStartInfo("net", $"{action} WSearch")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }

            opt.IsEnabled = enable;
            opt.CurrentValue = enable ? "Enabled" : "Disabled";
            return (true, enable ? "Search indexing enabled" : "Search indexing disabled");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }

    private static bool GetGameModeState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            if (key?.GetValue("AutoGameModeEnabled") is int val)
                return val != 0;
            return true;
        }
        catch { return true; }
    }

    private static (bool, string) ToggleGameMode(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
            key?.SetValue("AutoGameModeEnabled", enable ? 1 : 0, RegistryValueKind.DWord);

            opt.IsEnabled = enable;
            opt.CurrentValue = enable ? "On" : "Off";
            return (true, enable ? "Game Mode enabled" : "Game Mode disabled");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }

    private static bool GetGpuSchedulingState()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (key?.GetValue("HwSchMode") is int val)
                return val == 2;
            return false;
        }
        catch { return false; }
    }

    private static (bool, string) ToggleGpuScheduling(PerformanceOptimization opt)
    {
        try
        {
            var enable = !opt.IsEnabled;
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
            if (key == null) return (false, "Cannot access registry (admin required)");

            key.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);

            opt.IsEnabled = enable;
            opt.CurrentValue = enable ? "On" : "Off";
            opt.RequiresReboot = true;
            return (true, $"GPU scheduling {(enable ? "enabled" : "disabled")} restart required");
        }
        catch (Exception ex) { return (false, $"Failed: {ex.Message}"); }
    }
}
