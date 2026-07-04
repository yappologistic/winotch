using System.Collections.Specialized;
using System.Windows.Media.Imaging;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
using WpfIDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace Winotch;

public sealed class WindowsClipboardContentReader
{
    public ClipboardHistoryEntry? Read(DateTimeOffset capturedAt)
    {
        var data = WpfClipboard.GetDataObject();
        if (data is null || !ClipboardPrivacyPolicy.CanCapture(new ClipboardDataObjectFormatReader(data)))
        {
            return null;
        }

        var files = ReadFiles(data);
        if (files.Count > 0)
        {
            return ClipboardHistoryEntry.FromFiles(files, capturedAt);
        }

        var thumbnail = ReadImageThumbnail(data);
        if (thumbnail is not null)
        {
            return ClipboardHistoryEntry.FromImage(thumbnail, capturedAt);
        }

        return ClipboardHistoryEntry.FromText(ReadText(data), capturedAt);
    }

    public void Write(ClipboardHistoryEntry entry)
    {
        var data = new WpfDataObject();
        // Self-copied entries should not be re-added by Winotch or Windows clipboard history.
        MarkAsPrivateSelfCopy(data);

        switch (entry.Kind)
        {
            case ClipboardHistoryKind.Text:
            case ClipboardHistoryKind.Link:
                data.SetText(entry.Text ?? entry.Preview, WpfTextDataFormat.UnicodeText);
                break;
            case ClipboardHistoryKind.Files:
                var files = new StringCollection();
                files.AddRange(entry.FilePaths.ToArray());
                data.SetFileDropList(files);
                break;
            case ClipboardHistoryKind.Image:
                var image = ClipboardThumbnail.ToBitmapSource(entry.ThumbnailPng)
                    ?? throw new InvalidOperationException("Clipboard image thumbnail could not be decoded.");
                data.SetImage(image);
                break;
            default:
                throw new InvalidOperationException("Unsupported clipboard history entry.");
        }

        WpfClipboard.SetDataObject(data, copy: true);
    }

    private static IReadOnlyList<string> ReadFiles(WpfIDataObject data)
    {
        if (!data.GetDataPresent(WpfDataFormats.FileDrop, autoConvert: false))
        {
            return [];
        }

        return data.GetData(WpfDataFormats.FileDrop, autoConvert: false) switch
        {
            string[] paths => paths,
            StringCollection collection => collection.Cast<string>().ToArray(),
            IEnumerable<string> paths => paths.ToArray(),
            _ => []
        };
    }

    private static byte[]? ReadImageThumbnail(WpfIDataObject data)
    {
        if (!data.GetDataPresent(WpfDataFormats.Bitmap, autoConvert: true))
        {
            return null;
        }

        // Only the downscaled thumbnail is kept after this read; the full bitmap is never stored.
        return data.GetData(WpfDataFormats.Bitmap, autoConvert: true) is BitmapSource image
            ? ClipboardThumbnail.FromBitmapSource(image)
            : null;
    }

    private static string? ReadText(WpfIDataObject data) =>
        data.GetDataPresent(WpfDataFormats.UnicodeText, autoConvert: true)
            ? data.GetData(WpfDataFormats.UnicodeText, autoConvert: true) as string
            : null;

    private static void MarkAsPrivateSelfCopy(WpfDataObject data)
    {
        data.SetData(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);
        data.SetData(ClipboardPrivacyPolicy.CanIncludeInClipboardHistory, BitConverter.GetBytes(0));
    }
}

public sealed class ClipboardDataObjectFormatReader(WpfIDataObject data) : IClipboardFormatReader
{
    public bool HasFormat(string formatName) =>
        data.GetDataPresent(formatName, autoConvert: false);

    public bool TryGetData(string formatName, out object? value)
    {
        if (!HasFormat(formatName))
        {
            value = null;
            return false;
        }

        value = data.GetData(formatName, autoConvert: false);
        return true;
    }
}
