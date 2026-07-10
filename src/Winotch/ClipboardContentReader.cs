using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Winotch;

public sealed class WindowsClipboardContentReader
{
    public async Task<ClipboardHistoryEntry?> ReadAsync(DateTimeOffset capturedAt)
    {
        var data = Clipboard.GetContent();
        var formats = await ClipboardDataObjectFormatReader.CreateAsync(data);
        if (!ClipboardPrivacyPolicy.CanCapture(formats))
        {
            return null;
        }

        var files = await ReadFilesAsync(data);
        if (files.Count > 0)
        {
            return ClipboardHistoryEntry.FromFiles(files, capturedAt);
        }

        var thumbnail = await ReadImageThumbnailAsync(data);
        if (thumbnail is not null)
        {
            return ClipboardHistoryEntry.FromImage(thumbnail, capturedAt);
        }

        return ClipboardHistoryEntry.FromText(await ReadTextAsync(data), capturedAt);
    }

    public async Task WriteAsync(ClipboardHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        // This marker protects the in-process monitor. The native options below also
        // keep self-copies out of Windows clipboard history and cross-device roaming.
        data.SetData(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);

        InMemoryRandomAccessStream? imageStream = null;
        try
        {
            switch (entry.Kind)
            {
                case ClipboardHistoryKind.Text:
                case ClipboardHistoryKind.Link:
                    data.SetText(entry.Text ?? entry.Preview);
                    break;
                case ClipboardHistoryKind.Files:
                    data.SetStorageItems(await ResolveStorageItemsAsync(entry.FilePaths));
                    break;
                case ClipboardHistoryKind.Image:
                    imageStream = await ClipboardThumbnail.ToRandomAccessStreamAsync(
                        entry.ThumbnailPng
                        ?? throw new InvalidOperationException("Clipboard image thumbnail is missing."));
                    data.SetBitmap(RandomAccessStreamReference.CreateFromStream(imageStream));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported clipboard history entry.");
            }

            var options = new ClipboardContentOptions
            {
                IsAllowedInHistory = false,
                IsRoamable = false
            };
            if (!Clipboard.SetContentWithOptions(data, options))
            {
                throw new InvalidOperationException("The clipboard is currently unavailable.");
            }

            // Flush makes the data independent of this DataPackage and its image stream.
            Clipboard.Flush();
        }
        finally
        {
            imageStream?.Dispose();
        }
    }

    private static async Task<IReadOnlyList<string>> ReadFilesAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems))
        {
            return [];
        }

        var items = await data.GetStorageItemsAsync();
        return items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static async Task<byte[]?> ReadImageThumbnailAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.Bitmap))
        {
            return null;
        }

        // Only the downscaled thumbnail is kept after this read; the full bitmap is never stored.
        return await ClipboardThumbnail.FromStreamReferenceAsync(await data.GetBitmapAsync());
    }

    private static async Task<string?> ReadTextAsync(DataPackageView data) =>
        data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;

    private static async Task<IReadOnlyList<IStorageItem>> ResolveStorageItemsAsync(
        IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                items.Add(await StorageFile.GetFileFromPathAsync(path));
            }
            else if (Directory.Exists(path))
            {
                items.Add(await StorageFolder.GetFolderFromPathAsync(path));
            }
            else
            {
                throw new FileNotFoundException("A clipboard item no longer exists.", path);
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Clipboard file entries must contain at least one item.");
        }

        return items;
    }
}

public sealed class ClipboardDataObjectFormatReader : IClipboardFormatReader
{
    private readonly DataPackageView _data;
    private readonly IReadOnlyDictionary<string, object?> _values;

    private ClipboardDataObjectFormatReader(
        DataPackageView data,
        IReadOnlyDictionary<string, object?> values)
    {
        _data = data;
        _values = values;
    }

    public static async Task<ClipboardDataObjectFormatReader> CreateAsync(DataPackageView data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var format = ClipboardPrivacyPolicy.CanIncludeInClipboardHistory;
        if (data.Contains(format))
        {
            try
            {
                values[format] = await NormalizeDataAsync(await data.GetDataAsync(format));
            }
            catch
            {
                // A present but unreadable privacy marker is deliberately treated as private.
                values[format] = null;
            }
        }

        return new ClipboardDataObjectFormatReader(data, values);
    }

    public bool HasFormat(string formatName) => _data.Contains(formatName);

    public bool TryGetData(string formatName, out object? value) =>
        _values.TryGetValue(formatName, out value);

    private static async Task<object?> NormalizeDataAsync(object? value)
    {
        switch (value)
        {
            case IBuffer buffer:
                CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
                return bytes;
            case IRandomAccessStream stream:
                using (stream)
                {
                    return await ReadPrefixAsync(stream);
                }
            case IRandomAccessStreamReference reference:
                using (var stream = await reference.OpenReadAsync())
                {
                    return await ReadPrefixAsync(stream);
                }
            default:
                return value;
        }
    }

    private static async Task<byte[]> ReadPrefixAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var size = (uint)Math.Min(stream.Size, sizeof(int));
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
