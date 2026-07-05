using System.IO;
using System.Windows.Automation;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DrawingIcon = System.Drawing.Icon;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Winotch;

public sealed class NotificationService : IDisposable
{
    private const ulong MaxIconBytes = 512 * 1024;
    private readonly object _gate = new();
    private readonly List<NotificationItem> _liveToasts = [];
    private bool _watchingLiveToasts;
    private volatile bool _disposed;

    public event EventHandler? NotificationsChanged;

    public NotificationService()
    {
        try
        {
            Automation.AddAutomationEventHandler(
                WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Children,
                OnWindowOpened);
            _watchingLiveToasts = true;
        }
        catch
        {
            _watchingLiveToasts = false;
        }
    }

    public async Task<NotificationSnapshot> ReadAsync()
    {
        var liveToasts = GetLiveToasts();
        if (liveToasts.Count > 0)
        {
            return new NotificationSnapshot("Live Windows toast watcher", liveToasts);
        }

        if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
        {
            return new NotificationSnapshot("Notification listener is not available on this Windows build.", []);
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

            var status = items.Count == 0 && _watchingLiveToasts
                ? "Watching for live toast pop-ups"
                : "Windows notifications";
            return new NotificationSnapshot(items.Count == 0 ? status : "Windows notifications", items);
        }
        catch (Exception ex) when (ex is NotImplementedException || (uint)ex.HResult == 0x80004001)
        {
            return new NotificationSnapshot(_watchingLiveToasts
                ? "Watching live toasts; packaged capability needed for history."
                : "Notification access needs packaged Windows capability.", []);
        }
        catch (Exception ex)
        {
            return new NotificationSnapshot($"Notification access unavailable: {ex.Message}", []);
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
        _disposed = true;
        if (!_watchingLiveToasts)
        {
            return;
        }

        try
        {
            Automation.RemoveAutomationEventHandler(
                WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement,
                OnWindowOpened);
        }
        catch
        {
        }

        _watchingLiveToasts = false;
    }

    private static string HistoryAccessStatus(UserNotificationListenerAccessStatus access) =>
        access == UserNotificationListenerAccessStatus.Unspecified
            ? "Notification history access has not been requested."
            : "Allow notification access in Windows Settings.";

    private async void OnWindowOpened(object sender, AutomationEventArgs e)
    {
        if (_disposed || sender is not AutomationElement element)
        {
            return;
        }

        await Task.Delay(250);
        if (_disposed)
        {
            return;
        }

        var item = TryReadLiveToast(element);
        if (item is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_liveToasts.Any(existing =>
                existing.App == item.App &&
                existing.Title == item.Title &&
                existing.Body == item.Body &&
                item.CreatedAt - existing.CreatedAt < TimeSpan.FromSeconds(2)))
            {
                return;
            }

            _liveToasts.Insert(0, item);
            if (_liveToasts.Count > 4)
            {
                _liveToasts.RemoveAt(_liveToasts.Count - 1);
            }
        }

        if (!_disposed)
        {
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static NotificationItem? TryReadLiveToast(AutomationElement element)
    {
        try
        {
            if (element.Current.ProcessId == Environment.ProcessId)
            {
                return null;
            }

            var bounds = element.Current.BoundingRectangle;
            if (bounds.IsEmpty || bounds.Width < 180 || bounds.Width > 1400 || bounds.Height < 60 || bounds.Height > 800)
            {
                return null;
            }

            var textElements = element.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Text));

            var rawTexts = textElements
                .Cast<AutomationElement>()
                .Select(text => text.Current.Name.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray();
            var actions = ReadLiveActions(element);
            var actionLabels = actions.Select(action => action.Label).ToHashSet(StringComparer.Ordinal);
            var texts = rawTexts
                .Where(text => !actionLabels.Contains(text))
                .Take(3)
                .ToArray();

            if (texts.Length < 2)
            {
                return null;
            }

            var app = string.IsNullOrWhiteSpace(element.Current.Name) ? "Windows" : element.Current.Name;
            return new NotificationItem(
                app,
                texts[0],
                string.Join(" ", texts.Skip(1)),
                DateTimeOffset.Now,
                ReadProcessIcon(element.Current.ProcessId),
                actions);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<NotificationItem> GetLiveToasts()
    {
        lock (_gate)
        {
            return _liveToasts.ToList();
        }
    }

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

    private static IReadOnlyList<NotificationAction> ReadLiveActions(AutomationElement element)
    {
        var buttons = element.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

        return buttons
            .Cast<AutomationElement>()
            .Select(TryCreateAction)
            .Where(action => action is not null)
            .Cast<NotificationAction>()
            .Take(2)
            .ToList();
    }

    private static NotificationAction? TryCreateAction(AutomationElement button)
    {
        var label = button.Current.Name.Trim();
        if (string.IsNullOrWhiteSpace(label) ||
            label.Equals("Close", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Dismiss", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Minimize", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Maximize", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Restore", StringComparison.OrdinalIgnoreCase) ||
            !button.TryGetCurrentPattern(InvokePattern.Pattern, out _))
        {
            return null;
        }

        return new NotificationAction(label, () =>
        {
            try
            {
                ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            catch
            {
            }

            return Task.CompletedTask;
        });
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

    private static byte[]? ReadProcessIcon(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
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
