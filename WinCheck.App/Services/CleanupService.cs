using System.Runtime.InteropServices;
using System.Security.Principal;
using WinCheck.Models;

namespace WinCheck.Services;

public static class CleanupService
{
    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static List<CleanupCategory> BuildCleanupCategories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var temp = Path.GetTempPath();

        return
        [
            new CleanupCategory
            {
                Name = "Windows Temp Files",
                Description = "Temporary files in %TEMP% and Windows\\Temp",
                Paths = [temp, @"C:\Windows\Temp"],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Recycle Bin",
                Description = "Files in the recycle bin",
                Paths = [@"C:\`$Recycle.Bin"],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Chrome Cache",
                Description = "Google Chrome browser cache",
                Paths = [Path.Combine(localAppData, @"Google\Chrome\User Data")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Edge Cache",
                Description = "Microsoft Edge browser cache",
                Paths = [Path.Combine(localAppData, @"Microsoft\Edge\User Data")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Firefox Cache",
                Description = "Mozilla Firefox browser cache",
                Paths = [Path.Combine(appData, @"Mozilla\Firefox\Profiles")],
                RequiresAdmin = false
            },
            new CleanupCategory
            {
                Name = "Windows Update Cache",
                Description = "Windows Update download cache",
                Paths = [@"C:\Windows\SoftwareDistribution\Download"],
                RequiresAdmin = true
            },
            new CleanupCategory
            {
                Name = "Delivery Optimization",
                Description = "Windows Delivery Optimization files",
                Paths = [@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"],
                RequiresAdmin = true
            }
        ];
    }

    public static (long size, string details) CalculateCategorySize(CleanupCategory cat)
    {
        long total = 0;
        var details = new List<string>();

        if (cat.Name == "Recycle Bin")
        {
            var (size, items) = QueryRecycleBin();
            if (size > 0)
                details.Add($"  Recycle Bin  →  {FolderInfo.FormatBytesLocal(size)}  ({items} items)");
            return (size, string.Join("\n", details));
        }

        foreach (var path in cat.Paths)
        {
            try
            {
                if (!Directory.Exists(path)) continue;

                if (cat.Name == "Chrome Cache" || cat.Name == "Edge Cache")
                {
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "Cache");
                        if (Directory.Exists(cachePath))
                        {
                            var (size, files, _) = DiskService.GetDirectorySize(cachePath);
                            if (size > 0)
                                details.Add($"  {cachePath}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files)");
                            total += size;
                        }
                    }
                }
                else if (cat.Name == "Firefox Cache")
                {
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cachePath))
                        {
                            var (size, files, _) = DiskService.GetDirectorySize(cachePath);
                            if (size > 0)
                                details.Add($"  {cachePath}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files)");
                            total += size;
                        }
                    }
                }
                else
                {
                    var (size, files, folders) = DiskService.GetDirectorySize(path);
                    if (size > 0)
                        details.Add($"  {path}  →  {FolderInfo.FormatBytesLocal(size)}  ({files} files, {folders} folders)");
                    total += size;
                }
            }
            catch { }
        }

        return (total, details.Count > 0 ? string.Join("\n", details) : "");
    }

    public static (int files, long freed) CleanCategory(CleanupCategory cat)
    {
        int files = 0;
        long freed = 0;
        bool isAdmin = IsAdministrator();

        foreach (var path in cat.Paths)
        {
            try
            {
                if (cat.Name == "Recycle Bin")
                {
                    var (size, items) = QueryRecycleBin();
                    EmptyRecycleBin();
                    files += (int)items;
                    freed += size;
                }
                else if (cat.Name == "Chrome Cache" || cat.Name == "Edge Cache")
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "Cache");
                        if (Directory.Exists(cachePath))
                            (files, freed) = DeleteContents(cachePath, files, freed);
                    }
                }
                else if (cat.Name == "Firefox Cache")
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var profile in Directory.EnumerateDirectories(path))
                    {
                        var cachePath = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cachePath))
                            (files, freed) = DeleteContents(cachePath, files, freed);
                    }
                }
                else
                {
                    if (!Directory.Exists(path)) continue;
                    if (!isAdmin && cat.RequiresAdmin) continue;
                    (files, freed) = DeleteContents(path, files, freed);
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (!isAdmin && cat.RequiresAdmin)
                    cat.Status = "Requires admin rights";
            }
            catch (Exception ex)
            {
                cat.Status = $"Error: {ex.Message}";
            }
        }

        return (files, freed);
    }

    public static (int files, long freed) DeleteContents(string path, int files, long freed)
    {
        var di = new DirectoryInfo(path);

        foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                if (file.IsReadOnly) file.Attributes = FileAttributes.Normal;
                freed += file.Length;
                file.Delete();
                files++;
            }
            catch { }
        }

        foreach (var dir in di.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            try
            {
                if ((dir.Attributes & FileAttributes.ReadOnly) != 0)
                    dir.Attributes = FileAttributes.Normal;
                Directory.Delete(dir.FullName, true);
            }
            catch { }
        }

        try
        {
            foreach (var file in di.EnumerateFiles("*"))
            {
                try { file.Delete(); files++; } catch { }
            }

            foreach (var dir in di.EnumerateDirectories("*"))
            {
                try
                {
                    if ((dir.Attributes & FileAttributes.ReadOnly) != 0)
                        dir.Attributes = FileAttributes.Normal;
                    Directory.Delete(dir.FullName, true);
                }
                catch { }
            }
        }
        catch { }

        return (files, freed);
    }

    public static int CountCategoryFiles(CleanupCategory cat)
    {
        try
        {
            foreach (var path in cat.Paths)
            {
                if (Directory.Exists(path))
                {
                    var (_, files, _) = DiskService.GetDirectorySize(path);
                    return files;
                }
            }
        }
        catch { }
        return 0;
    }

    public static (long size, long items) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var result = SHQueryRecycleBin(null, ref info);
        return result == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public static void EmptyRecycleBin()
    {
        const uint SHERB_NOCONFIRMATION = 0x1;
        const uint SHERB_NOPROGRESSUI = 0x2;
        const uint SHERB_NOSOUND = 0x4;
        SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }
}
