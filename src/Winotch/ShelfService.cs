using System.Collections.Specialized;
using System.Windows.Media.Imaging;
using WpfDataFormats = System.Windows.DataFormats;
using WpfIDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace Winotch;

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

    public ShelfItem? ReadDrop(WpfIDataObject data, DateTimeOffset stagedAt)
    {
        if (!ClipboardPrivacyPolicy.CanCapture(new ClipboardDataObjectFormatReader(data)))
        {
            return null;
        }

        var files = ReadFiles(data);
        if (files.Count > 0)
        {
            return ShelfItem.FromFiles(files, stagedAt);
        }

        var thumbnail = ReadImageThumbnail(data);
        if (thumbnail is not null)
        {
            return ShelfItem.FromImage(thumbnail, stagedAt);
        }

        return ShelfItem.FromText(ReadText(data), stagedAt);
    }

    private void TrimToCap()
    {
        if (_items.Count > _cap)
        {
            _items.RemoveRange(_cap, _items.Count - _cap);
            Changed?.Invoke(this, EventArgs.Empty);
        }
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

        // Privacy boundary: only the downscaled thumbnail survives staging.
        return data.GetData(WpfDataFormats.Bitmap, autoConvert: true) is BitmapSource image
            ? ClipboardThumbnail.FromBitmapSource(image)
            : null;
    }

    private static string? ReadText(WpfIDataObject data) =>
        data.GetDataPresent(WpfDataFormats.UnicodeText, autoConvert: true)
            ? data.GetData(WpfDataFormats.UnicodeText, autoConvert: true) as string
            : data.GetDataPresent(WpfTextDataFormat.Text.ToString(), autoConvert: true)
                ? data.GetData(WpfTextDataFormat.Text.ToString(), autoConvert: true) as string
                : null;
}
