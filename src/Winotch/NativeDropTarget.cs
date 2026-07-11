using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.UI.Xaml;

namespace Winotch;

public sealed record NativeDropPayload(
    IReadOnlyList<string> FilePaths,
    string? Text,
    byte[]? ImagePng)
{
    public bool HasContent => FilePaths.Count > 0 || !string.IsNullOrWhiteSpace(Text) || ImagePng is { Length: > 0 };
}

/// <summary>
/// Native OLE drop target used instead of WinUI's XAML drop target. Windows 11
/// 24H2 has a WinUI regression where Explorer drags never reach DragOver, while
/// the underlying OLE IDataObject remains fully available to a Win32 target.
/// </summary>
[ComVisible(true)]
internal sealed class NativeDropTarget : INativeDropTarget, IDisposable
{
    private const int S_OK = 0;
    private const uint DropEffectNone = 0;
    private const uint DropEffectCopy = 1;
    private readonly IntPtr _hwnd;
    private readonly Func<NativeDropPayload, Task> _onDrop;
    private bool _supportsCurrentDrag;
    private bool _disposed;

    private NativeDropTarget(IntPtr hwnd, Func<NativeDropPayload, Task> onDrop)
    {
        _hwnd = hwnd;
        _onDrop = onDrop;
    }

    public static NativeDropTarget? Attach(Window window, Func<NativeDropPayload, Task> onDrop)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onDrop);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var target = new NativeDropTarget(hwnd, onDrop);
        // XAML registers its own OLE target when AllowDrop is present. Replace it
        // after Loaded so Explorer does not hit the broken WinUI 24H2 path.
        _ = RevokeDragDrop(hwnd);
        var result = RegisterDragDrop(hwnd, target);
        if (result == S_OK)
        {
            return target;
        }

        Debug.WriteLine($"Unable to register native shelf drop target: 0x{result:X8}");
        target.Dispose();
        return null;
    }

    int INativeDropTarget.DragEnter(IDataObject data, uint keyState, NativePoint point, ref uint effect)
    {
        _supportsCurrentDrag = NativeDropDataReader.Supports(data);
        effect = _supportsCurrentDrag ? DropEffectCopy : DropEffectNone;
        return S_OK;
    }

    int INativeDropTarget.DragOver(uint keyState, NativePoint point, ref uint effect)
    {
        effect = _supportsCurrentDrag ? DropEffectCopy : DropEffectNone;
        return S_OK;
    }

    int INativeDropTarget.DragLeave()
    {
        _supportsCurrentDrag = false;
        return S_OK;
    }

    int INativeDropTarget.Drop(IDataObject data, uint keyState, NativePoint point, ref uint effect)
    {
        var payload = NativeDropDataReader.Read(data);
        _supportsCurrentDrag = false;
        effect = payload.HasContent ? DropEffectCopy : DropEffectNone;
        if (payload.HasContent)
        {
            _ = HandleDropAsync(payload);
        }

        return S_OK;
    }

    private async Task HandleDropAsync(NativeDropPayload payload)
    {
        try
        {
            await _onDrop(payload);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to stage native shelf drop: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = RevokeDragDrop(_hwnd);
    }

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] INativeDropTarget dropTarget);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);
}

[ComVisible(true)]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INativeDropTarget
{
    [PreserveSig]
    int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keyState, NativePoint point, ref uint effect);

    [PreserveSig]
    int DragOver(uint keyState, NativePoint point, ref uint effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keyState, NativePoint point, ref uint effect);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint
{
    public readonly int X;
    public readonly int Y;
}

internal static class NativeDropDataReader
{
    private const short CfText = 1;
    private const short CfBitmap = 2;
    private const short CfDib = 8;
    private const short CfUnicodeText = 13;
    private const short CfHdrop = 15;
    private const short CfDibV5 = 17;
    private const int DvAspectContent = 1;
    private static readonly short PngFormat = unchecked((short)RegisterClipboardFormat("PNG"));
    private static readonly short UrlFormat = unchecked((short)RegisterClipboardFormat("UniformResourceLocatorW"));

    public static bool Supports(IDataObject data) =>
        HasFormat(data, CfHdrop, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, PngFormat, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, CfBitmap, TYMED.TYMED_GDI) ||
        HasFormat(data, CfDibV5, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, CfDib, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, CfUnicodeText, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, UrlFormat, TYMED.TYMED_HGLOBAL) ||
        HasFormat(data, CfText, TYMED.TYMED_HGLOBAL);

    public static NativeDropPayload Read(IDataObject data)
    {
        var files = ReadFiles(data);
        if (files.Count > 0)
        {
            return new NativeDropPayload(files, null, null);
        }

        var image = ReadImage(data);
        if (image is { Length: > 0 })
        {
            return new NativeDropPayload([], null, image);
        }

        var text = ReadUnicodeText(data, CfUnicodeText) ??
                   ReadUnicodeText(data, UrlFormat) ??
                   ReadAnsiText(data);
        return new NativeDropPayload([], text, null);
    }

    private static IReadOnlyList<string> ReadFiles(IDataObject data)
    {
        if (!TryGetData(data, CfHdrop, TYMED.TYMED_HGLOBAL, out var medium))
        {
            return [];
        }

        try
        {
            var count = DragQueryFile(medium.unionmember, uint.MaxValue, null, 0);
            var paths = new List<string>((int)Math.Min(count, int.MaxValue));
            for (uint index = 0; index < count; index++)
            {
                var length = DragQueryFile(medium.unionmember, index, null, 0);
                var path = new StringBuilder((int)length + 1);
                if (DragQueryFile(medium.unionmember, index, path, path.Capacity) > 0)
                {
                    paths.Add(path.ToString());
                }
            }

            return paths;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[]? ReadImage(IDataObject data)
    {
        var png = ReadHGlobalBytes(data, PngFormat);
        if (png is { Length: > 0 })
        {
            return png;
        }

        if (TryGetData(data, CfBitmap, TYMED.TYMED_GDI, out var bitmapMedium))
        {
            try
            {
                using var bitmap = Image.FromHbitmap(bitmapMedium.unionmember);
                return EncodePng(bitmap);
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException)
            {
                Debug.WriteLine($"Unable to decode dropped HBITMAP: {ex.Message}");
            }
            finally
            {
                ReleaseStgMedium(ref bitmapMedium);
            }
        }

        var dib = ReadHGlobalBytes(data, CfDibV5) ?? ReadHGlobalBytes(data, CfDib);
        return dib is { Length: > 0 } ? DecodeDibToPng(dib) : null;
    }

    internal static byte[]? DecodeDibToPng(byte[] dib)
    {
        if (!TryBuildBitmapFile(dib, out var bitmapFile))
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(bitmapFile);
            using var image = Image.FromStream(input);
            return EncodePng(image);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            Debug.WriteLine($"Unable to decode dropped DIB: {ex.Message}");
            return null;
        }
    }

    internal static bool TryBuildBitmapFile(byte[] dib, out byte[] bitmapFile)
    {
        bitmapFile = [];
        if (dib.Length < 12)
        {
            return false;
        }

        var headerSize = BitConverter.ToUInt32(dib, 0);
        if (headerSize < 12 || headerSize > dib.Length)
        {
            return false;
        }

        var extraBeforePixels = 0u;
        if (headerSize == 12)
        {
            var bitCount = BitConverter.ToUInt16(dib, 10);
            if (bitCount <= 8)
            {
                extraBeforePixels = 3u * (1u << bitCount);
            }
        }
        else if (headerSize >= 40 && dib.Length >= 40)
        {
            var bitCount = BitConverter.ToUInt16(dib, 14);
            var compression = BitConverter.ToUInt32(dib, 16);
            var colorsUsed = BitConverter.ToUInt32(dib, 32);
            if (bitCount <= 8)
            {
                extraBeforePixels = 4u * (colorsUsed == 0 ? 1u << bitCount : colorsUsed);
            }
            else if (headerSize == 40 && compression is 3 or 6)
            {
                extraBeforePixels = compression == 6 ? 16u : 12u;
            }
        }

        var pixelOffset = 14u + headerSize + extraBeforePixels;
        if (pixelOffset > dib.Length + 14u)
        {
            return false;
        }

        bitmapFile = new byte[dib.Length + 14];
        bitmapFile[0] = (byte)'B';
        bitmapFile[1] = (byte)'M';
        BitConverter.GetBytes(bitmapFile.Length).CopyTo(bitmapFile, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(bitmapFile, 10);
        dib.CopyTo(bitmapFile, 14);
        return true;
    }

    private static byte[] EncodePng(Image image)
    {
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static string? ReadUnicodeText(IDataObject data, short format)
    {
        if (!TryGetData(data, format, TYMED.TYMED_HGLOBAL, out var medium))
        {
            return null;
        }

        try
        {
            var pointer = GlobalLock(medium.unionmember);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer)?.TrimEnd('\0');
            }
            finally
            {
                _ = GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static string? ReadAnsiText(IDataObject data)
    {
        if (!TryGetData(data, CfText, TYMED.TYMED_HGLOBAL, out var medium))
        {
            return null;
        }

        try
        {
            var pointer = GlobalLock(medium.unionmember);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringAnsi(pointer)?.TrimEnd('\0');
            }
            finally
            {
                _ = GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[]? ReadHGlobalBytes(IDataObject data, short format)
    {
        if (!TryGetData(data, format, TYMED.TYMED_HGLOBAL, out var medium))
        {
            return null;
        }

        try
        {
            var size = GlobalSize(medium.unionmember);
            if (size == UIntPtr.Zero || size.ToUInt64() > int.MaxValue)
            {
                return null;
            }

            var pointer = GlobalLock(medium.unionmember);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bytes = new byte[(int)size.ToUInt64()];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                _ = GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static bool HasFormat(IDataObject data, short format, TYMED medium)
    {
        if (format == 0)
        {
            return false;
        }

        var descriptor = CreateFormat(format, medium);
        return data.QueryGetData(ref descriptor) == S_OK;
    }

    private static bool TryGetData(IDataObject data, short format, TYMED medium, out STGMEDIUM storage)
    {
        storage = default;
        if (!HasFormat(data, format, medium))
        {
            return false;
        }

        var descriptor = CreateFormat(format, medium);
        try
        {
            data.GetData(ref descriptor, out storage);
            return storage.unionmember != IntPtr.Zero;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static FORMATETC CreateFormat(short format, TYMED medium) => new()
    {
        cfFormat = format,
        dwAspect = (DVASPECT)DvAspectContent,
        lindex = -1,
        ptd = IntPtr.Zero,
        tymed = medium
    };

    private const int S_OK = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr drop, uint fileIndex, StringBuilder? fileName, int fileNameSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
