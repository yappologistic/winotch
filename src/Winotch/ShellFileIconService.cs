using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace Winotch;

public sealed class ShellFileIconService
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiUseFileAttributes = 0x10;
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetIcon(FileShelfTile tile)
    {
        var key = CacheKey(tile);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = ReadIcon(tile);
        if (icon is not null)
        {
            _cache[key] = icon;
        }

        return icon;
    }

    private static string CacheKey(FileShelfTile tile)
    {
        if (tile.IsDirectory)
        {
            return $"dir:{tile.FullPath}";
        }

        var extension = Path.GetExtension(tile.FullPath);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"exe:{tile.FullPath}"
            : $"ext:{extension}";
    }

    private static ImageSource? ReadIcon(FileShelfTile tile)
    {
        var attributes = tile.IsDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | (tile.Exists ? 0 : ShgfiUseFileAttributes);
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(tile.FullPath, attributes, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        if (result != IntPtr.Zero && info.Icon != IntPtr.Zero)
        {
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.Icon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.Icon);
            }
        }

        return ReadAssociatedIcon(tile.FullPath);
    }

    private static ImageSource? ReadAssociatedIcon(string path)
    {
        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
