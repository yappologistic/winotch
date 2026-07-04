using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Winotch;

public partial class MainWindow
{
    private static readonly TimeSpan CalendarRefreshInterval = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer _calendarTimer = new() { Interval = CalendarRefreshInterval };
    private readonly CalendarRefreshService _calendarRefresh = new();
    private readonly CalendarToastTracker _calendarToastTracker = new();
    private IReadOnlyList<CalendarOccurrence> _calendarOccurrences = [];
    private DateTimeOffset? _calendarLastUpdatedUtc;
    private string _calendarUrlSignature = string.Empty;
    private bool _calendarActive;
    private bool _refreshingCalendar;
    private bool _calendarRefreshQueued;

    private void ApplyCalendarSettings(WinotchSettings settings)
    {
        var active = settings.Calendar.Enabled && settings.Calendar.SubscriptionUrls.Count > 0;
        var signature = CalendarUrlSignature(settings.Calendar);
        var changed = active != _calendarActive || !StringComparer.Ordinal.Equals(signature, _calendarUrlSignature);
        _calendarActive = active;
        _calendarUrlSignature = signature;

        if (!_calendarActive || _notchPaused)
        {
            _calendarTimer.Stop();
            _calendarOccurrences = [];
            _calendarLastUpdatedUtc = null;
            ApplyCalendarUi(DateTimeOffset.UtcNow);
            return;
        }

        if (!_calendarTimer.IsEnabled)
        {
            _calendarTimer.Start();
        }

        if (changed)
        {
            _ = RefreshCalendarAsync();
        }
    }

    private async Task RefreshCalendarAsync()
    {
        if (_refreshingCalendar)
        {
            _calendarRefreshQueued = true;
            return;
        }

        do
        {
            _calendarRefreshQueued = false;
            var settings = _settings.Current.Calendar;
            var requestedSignature = CalendarUrlSignature(settings);
            var now = DateTimeOffset.UtcNow;
            if (!settings.Enabled || settings.SubscriptionUrls.Count == 0 || _notchPaused)
            {
                ApplyCalendarUi(now);
                return;
            }

            _refreshingCalendar = true;
            try
            {
                var result = await _calendarRefresh.RefreshAsync(settings.SubscriptionUrls, now);
                var currentSettings = _settings.Current.Calendar;
                if (_notchPaused ||
                    !currentSettings.Enabled ||
                    currentSettings.SubscriptionUrls.Count == 0 ||
                    !StringComparer.Ordinal.Equals(requestedSignature, CalendarUrlSignature(currentSettings)))
                {
                    continue;
                }

                now = DateTimeOffset.UtcNow;
                _calendarLastUpdatedUtc = result.LastUpdatedUtc;
                _calendarOccurrences = IcsRecurrence.Expand(result.Events, now.AddMinutes(-5), now.AddDays(7));
                ApplyCalendarUi(now);
                ShowCalendarToastIfDue(now);
            }
            finally
            {
                _refreshingCalendar = false;
            }
        }
        while (_calendarRefreshQueued);
    }

    private void ApplyCalendarUi(DateTimeOffset now)
    {
        var settings = _settings.Current.Calendar;
        var rows = settings.Enabled && settings.SubscriptionUrls.Count > 0
            ? CalendarAgenda.Rows(_calendarOccurrences, now, _settings.Current.General.Use24HourClock)
            : [];
        AgendaList.ItemsSource = rows;
        AgendaList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AgendaEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AgendaEmptyText.Text = settings.Enabled
            ? settings.SubscriptionUrls.Count == 0 ? "Add ICS URLs in Settings" : "No meetings in the next 24h"
            : "Calendar off";
        AgendaUpdatedText.Visibility = settings.Enabled && settings.SubscriptionUrls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AgendaUpdatedText.Text = CalendarAgenda.FormatUpdatedAge(_calendarLastUpdatedUtc, now);
        RefreshCalendarLiveStatus(now);
    }

    private void RefreshCalendarLiveStatus(DateTimeOffset now)
    {
        if (!_calendarActive)
        {
            CalendarLiveActivity.Visibility = Visibility.Collapsed;
            CalendarLiveText.Text = string.Empty;
            return;
        }

        var focusActive = _focusTimer.SnapshotAt(now).Status != FocusTimerStatus.Stopped;
        var occurrence = CalendarAgenda.SelectPillOccurrence(_calendarOccurrences, now, focusActive);
        var text = occurrence is null ? null : CalendarAgenda.CountdownText(occurrence, now);
        CalendarLiveActivity.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        CalendarLiveText.Text = text ?? string.Empty;
    }

    private void ShowCalendarToastIfDue(DateTimeOffset now)
    {
        if (!_calendarActive)
        {
            return;
        }

        var occurrence = _calendarToastTracker.NextToast(_calendarOccurrences, now);
        if (occurrence is not null)
        {
            ShowCalendarToast(occurrence);
        }
    }

    private void ShowCalendarToast(CalendarOccurrence occurrence)
    {
        NotificationToastTitleText.Text = "Meeting soon";
        NotificationToastBodyText.Text = occurrence.Title;
        NotificationToastAppText.Text = "Calendar";
        NotificationToastTimeText.Text = "Now";
        NotificationToastIconImage.Source = null;
        NotificationToastIconImage.Visibility = Visibility.Collapsed;
        NotificationToastIconFallback.FontFamily = ToastIconFont;
        NotificationToastIconFallback.FontSize = 17;
        NotificationToastIconFallback.Text = "\uE787";
        NotificationToastIconFallback.Visibility = Visibility.Visible;
        ApplyNotificationToastActions([
            new NotificationAction("Join", () =>
            {
                OpenMeetingUrl(occurrence.JoinUrl);
                return Task.CompletedTask;
            })
        ]);
        ShowCompactToast(NotificationToastPanel);
    }

    private void AgendaJoin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string url })
        {
            OpenMeetingUrl(url);
        }
    }

    private static void OpenMeetingUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
        }
    }

    private static string CalendarUrlSignature(CalendarSettings settings) => string.Join('\n', settings.SubscriptionUrls);
}
