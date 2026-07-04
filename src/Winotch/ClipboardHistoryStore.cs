namespace Winotch;

public sealed class ClipboardHistoryStore
{
    public const int Capacity = 10;
    private readonly List<ClipboardHistoryEntry> _items = [];

    public IReadOnlyList<ClipboardHistoryEntry> Items => _items.ToList();

    public void Push(ClipboardHistoryEntry entry)
    {
        if (_items.Count > 0 && StringComparer.Ordinal.Equals(_items[0].Signature, entry.Signature))
        {
            _items[0] = entry;
            return;
        }

        _items.Insert(0, entry);
        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
    }

    public ClipboardHistoryEntry? Find(Guid id) =>
        _items.FirstOrDefault(item => item.Id == id);

    public bool Delete(Guid id)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        return true;
    }

    public bool Clear()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        return true;
    }
}
