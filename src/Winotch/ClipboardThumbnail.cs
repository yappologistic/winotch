using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Winotch;

public static class ClipboardThumbnail
{
    public const int MaxPixel = 64;

    public static byte[]? FromBitmapSource(BitmapSource source)
    {
        try
        {
            if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
            {
                return null;
            }

            var scale = Math.Min(1.0, MaxPixel / (double)Math.Max(source.PixelWidth, source.PixelHeight));
            var thumbnail = scale >= 1
                ? source
                : new TransformedBitmap(source, new ScaleTransform(scale, scale));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource? ToBitmapSource(byte[]? pngBytes)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(pngBytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelHeight = MaxPixel;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
