using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Winotch;

public sealed class NotificationService : IDisposable
{
    private const ulong MaxIconBytes = 512 * 1024;

    public async Task<NotificationSnapshot> ReadAsync()
    {
        if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
        {
            return new NotificationSnapshot("Notification history unavailable.", []);
        }

        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (!CanReadNotificationHistory(access))
            {
                return new NotificationSnapshot(HistoryAccessStatus(access), []);
            }

            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<NotificationItem>();
            foreach (var notification in notifications.OrderByDescending(notification => notification.CreationTime).Take(4))
            {
                var item = await ReadNotificationAsync(notification);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return new NotificationSnapshot("Windows notifications", items);
        }
        catch (Exception ex) when (ex is NotImplementedException || (uint)ex.HResult == 0x80004001)
        {
            return new NotificationSnapshot("Notification history needs package identity.", []);
        }
        catch (Exception)
        {
            return new NotificationSnapshot("Notification access unavailable.", []);
        }
    }

    public async Task<UserNotificationListenerAccessStatus> RequestHistoryAccessAsync()
    {
        if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
        {
            return UserNotificationListenerAccessStatus.Denied;
        }

        return await UserNotificationListener.Current.RequestAccessAsync();
    }

    public static bool CanReadNotificationHistory(UserNotificationListenerAccessStatus access) =>
        access == UserNotificationListenerAccessStatus.Allowed;

    public void Dispose()
    {
        // UserNotificationListener has no subscription owned by this service.
    }

    private static string HistoryAccessStatus(UserNotificationListenerAccessStatus access) =>
        access == UserNotificationListenerAccessStatus.Unspecified
            ? "Request notification access in Settings."
            : "Allow notifications in Windows Settings.";

    private static async Task<NotificationItem?> ReadNotificationAsync(UserNotification notification)
    {
        var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
        var text = binding?.GetTextElements()
            .Select(element => element.Text)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];

        if (text.Length == 0)
        {
            return null;
        }

        var title = text[0];
        var body = text.Length > 1 ? string.Join(" ", text.Skip(1)) : notification.AppInfo.DisplayInfo.DisplayName;
        var icon = await ReadIconAsync(notification.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32)));
        return new NotificationItem(notification.AppInfo.DisplayInfo.DisplayName, title, body, notification.CreationTime, icon, []);
    }

    private static async Task<byte[]?> ReadIconAsync(IRandomAccessStreamReference? icon)
    {
        if (icon is null)
        {
            return null;
        }

        try
        {
            using var stream = await icon.OpenReadAsync();
            var length = (uint)Math.Min(stream.Size, MaxIconBytes);
            if (length == 0)
            {
                return null;
            }

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(length);
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

}

public sealed record NotificationSnapshot(string Status, IReadOnlyList<NotificationItem> Items);

public sealed record NotificationItem(
    string App,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    byte[]? Icon,
    IReadOnlyList<NotificationAction> Actions)
{
    public NotificationItem(string app, string title, string body)
        : this(app, title, body, DateTimeOffset.MinValue, null, [])
    {
    }

    public string BadgeText => string.IsNullOrWhiteSpace(App) ? "!" : App.Trim()[0].ToString().ToUpperInvariant();
    public string TimeText => CreatedAt == DateTimeOffset.MinValue ? "" : CreatedAt.LocalDateTime.ToString("h:mm tt");

    public override string ToString() => $"{App}: {Title} {Body}";
}

public sealed record NotificationAction(string Label, Func<Task> InvokeAsync);
