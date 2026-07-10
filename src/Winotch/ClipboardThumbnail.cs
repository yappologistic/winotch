using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Winotch;

public static class ClipboardThumbnail
{
    public const int MaxPixel = 64;

    /// <summary>
    /// Decodes an incoming clipboard bitmap, scales it at the privacy boundary, and
    /// returns only a compact PNG. The original full-resolution pixels are not retained.
    /// </summary>
    public static async Task<byte[]?> FromStreamReferenceAsync(IRandomAccessStreamReference? source)
    {
        if (source is null)
        {
            return null;
        }

        try
        {
            using var input = await source.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            {
                return null;
            }

            var scale = Math.Min(1.0, MaxPixel / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
            var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                width,
                height,
                decoder.DpiX,
                decoder.DpiY,
                pixels.DetachPixelData());
            await encoder.FlushAsync();
            return await ReadAllBytesAsync(output);
        }
        catch
        {
            return null;
        }
    }

    public static Task<BitmapImage?> ToBitmapSourceAsync(byte[]? pngBytes) =>
        ToBitmapSourceAsync(pngBytes, decodePixelWidth: 0, decodePixelHeight: MaxPixel);

    public static async Task<BitmapImage?> ToBitmapSourceAsync(
        byte[]? imageBytes,
        int decodePixelWidth,
        int decodePixelHeight)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = await ToRandomAccessStreamAsync(imageBytes);
            var image = new BitmapImage
            {
                DecodePixelWidth = Math.Max(0, decodePixelWidth),
                DecodePixelHeight = Math.Max(0, decodePixelHeight)
            };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<InMemoryRandomAccessStream> ToRandomAccessStreamAsync(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var size = (uint)Math.Min(stream.Size, int.MaxValue);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
