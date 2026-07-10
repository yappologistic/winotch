using Windows.ApplicationModel.DataTransfer;

namespace Winotch;

/// <summary>
/// Owns the shelf's in-memory staging list and converts WinUI drag payloads into
/// privacy-bounded shelf items. Images are reduced to thumbnails before staging.
/// </summary>
public sealed class ShelfService
{
    private readonly List<ShelfItem> _items = [];
    private int _cap;

    public ShelfService(ShelfSettings settings)
    {
        _cap = settings.Normalize().Cap;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ShelfItem> Items => _items.ToList();

    public void ApplySettings(ShelfSettings settings)
    {
        _cap = settings.Normalize().Cap;
        TrimToCap();
    }

    public bool Stage(ShelfItem? item, IClipboardFormatReader? formats = null)
    {
        if (item is null || (formats is not null && !ClipboardPrivacyPolicy.CanCapture(formats)))
        {
            return false;
        }

        _items.RemoveAll(existing => StringComparer.Ordinal.Equals(existing.Signature, item.Signature));
        _items.Insert(0, item);
        TrimToCap();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(Guid id)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Clear()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public ShelfItem? Find(Guid id) => _items.FirstOrDefault(item => item.Id == id);

    /// <summary>
    /// Reads an actual WinUI drag payload. The privacy markers are evaluated before
    /// any content is requested, matching clipboard capture behavior.
    /// </summary>
    public async Task<ShelfItem?> ReadDropAsync(DataPackageView data, DateTimeOffset stagedAt)
    {
        ArgumentNullException.ThrowIfNull(data);

        var formats = await ClipboardDataObjectFormatReader.CreateAsync(data);
        if (!ClipboardPrivacyPolicy.CanCapture(formats))
        {
            return null;
        }

        var files = await ReadFilesAsync(data);
        if (files.Count > 0)
        {
            return ShelfItem.FromFiles(files, stagedAt);
        }

        var thumbnail = await ReadImageThumbnailAsync(data);
        if (thumbnail is not null)
        {
            return ShelfItem.FromImage(thumbnail, stagedAt);
        }

        return ShelfItem.FromText(await ReadTextAsync(data), stagedAt);
    }

    private void TrimToCap()
    {
        if (_items.Count > _cap)
        {
            _items.RemoveRange(_cap, _items.Count - _cap);
            Changed?.Invoke(this, EventArgs.Empty);
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

        // ClipboardThumbnail enforces the 64 px privacy boundary while decoding.
        return await ClipboardThumbnail.FromStreamReferenceAsync(await data.GetBitmapAsync());
    }

    private static async Task<string?> ReadTextAsync(DataPackageView data) =>
        data.Contains(StandardDataFormats.Text) ? await data.GetTextAsync() : null;
}
