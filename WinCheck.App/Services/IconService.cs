using System.Collections.Concurrent;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WinCheck.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, byte[]?> IconBytesCache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? GetIconBytes(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (IconBytesCache.TryGetValue(path, out var cached)) return cached;

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) { IconBytesCache[path] = null; return null; }
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = ms.ToArray();
            IconBytesCache[path] = bytes;
            return bytes;
        }
        catch
        {
            IconBytesCache[path] = null;
            return null;
        }
    }

    public static ImageSource? IconFromBytes(byte[]? bytes)
    {
        if (bytes == null) return null;
        var bitmap = new BitmapImage();
        var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        }
        bitmap.SetSource(ras);
        return bitmap;
    }
}
