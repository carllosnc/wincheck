using System.IO;
using WinCheck.Models;

namespace WinCheck.Services;

public static class DiskService
{
    public static void ScanDirectory(string root, IProgress<FolderInfo> progress)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var (size, files, folders) = GetDirectorySize(dir);
                    progress.Report(new FolderInfo
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        SizeBytes = size,
                        FileCount = files,
                        FolderCount = folders
                    });
                }
                catch
                {
                    progress.Report(new FolderInfo
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        SizeBytes = 0,
                        FileCount = 0,
                        FolderCount = 0
                    });
                }
            }
        }
        catch { }
    }

    public static (long size, int files, int folders) GetDirectorySize(string path)
    {
        long size = 0;
        int files = 0;
        int folders = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    files++;
                }
                catch { }
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var (subSize, subFiles, subFolders) = GetDirectorySize(dir);
                    size += subSize;
                    files += subFiles;
                    folders += subFolders + 1;
                }
                catch { }
            }
        }
        catch { }

        return (size, files, folders);
    }
}
