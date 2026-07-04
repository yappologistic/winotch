namespace Winotch;

public sealed class NotificationChangeTracker
{
    private string? _lastSignature;

    public bool ShouldPop(IReadOnlyList<NotificationItem> items)
    {
        var signature = CreateSignature(items);
        if (_lastSignature is null)
        {
            _lastSignature = signature;
            return false;
        }

        var changed = signature.Length > 0 && !StringComparer.Ordinal.Equals(_lastSignature, signature);
        _lastSignature = signature;
        return changed;
    }

    public static string CreateSignature(IReadOnlyList<NotificationItem> items) =>
        string.Join('\u001f', items.Select(item => $"{item.App}\u001e{item.Title}\u001e{item.Body}\u001e{item.CreatedAt.UtcTicks}"));
}
