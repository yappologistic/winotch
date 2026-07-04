namespace Winotch.Tests;

public class CalendarAgendaTests
{
    [Fact]
    public void AgendaRowsDeduplicateUidStartsAndRespectClockSetting()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var occurrences = new[]
        {
            Occurrence("one", "One", now.AddMinutes(5)),
            Occurrence("one", "Duplicate", now.AddMinutes(5)),
            Occurrence("two", "Two", now.AddMinutes(10), joinUrl: "https://zoom.us/j/1"),
            Occurrence("three", "Three", now.AddMinutes(15)),
            Occurrence("four", "Four", now.AddMinutes(20))
        };

        var rows = CalendarAgenda.Rows(occurrences, now, use24HourClock: true);

        Assert.Equal(3, rows.Count);
        Assert.Equal("10:05", rows[0].TimeText);
        Assert.Equal("One", rows[0].Title);
        Assert.True(rows[1].HasJoin);
    }

    [Fact]
    public void CountdownFormatsMinutesNowAndDisappearance()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var upcoming = Occurrence("standup", "Standup", now.AddMinutes(4));
        var started = Occurrence("live", "Very long meeting title", now.AddMinutes(-2));
        var stale = Occurrence("old", "Old", now.AddMinutes(-5));

        Assert.Equal("Standup \u00b7 4m", CalendarAgenda.CountdownText(upcoming, now));
        Assert.Equal("Very lo... \u00b7 now", CalendarAgenda.CountdownText(started, now));
        Assert.Null(CalendarAgenda.CountdownText(stale, now));
    }

    [Fact]
    public void PillSelectionLetsFocusTimerWin()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var occurrence = Occurrence("standup", "Standup", now.AddMinutes(3));

        Assert.Null(CalendarAgenda.SelectPillOccurrence([occurrence], now, focusActive: true));
        Assert.Equal(occurrence, CalendarAgenda.SelectPillOccurrence([occurrence], now, focusActive: false));
    }

    [Fact]
    public void PillSelectionUsesSoonestOverlappingEvent()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var started = Occurrence("started", "Started", now.AddMinutes(-1));
        var future = Occurrence("future", "Future", now.AddMinutes(1));

        Assert.Equal(started, CalendarAgenda.SelectPillOccurrence([future, started], now, focusActive: false));
    }

    [Fact]
    public void ToastTrackerRequiresPreThresholdObservationAndPopsOnce()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var occurrence = Occurrence("standup", "Standup", now.AddMinutes(5), joinUrl: "https://zoom.us/j/1");
        var tracker = new CalendarToastTracker();

        Assert.Null(tracker.NextToast([occurrence], now.AddMinutes(2)));
        Assert.Equal(occurrence, tracker.NextToast([occurrence], now.AddMinutes(3)));
        Assert.Null(tracker.NextToast([occurrence], now.AddMinutes(3).AddSeconds(10)));
    }

    [Fact]
    public void ToastTrackerSuppressesStaleLaunchInsideToastWindow()
    {
        var now = LocalTime(2026, 7, 4, 10, 0);
        var occurrence = Occurrence("standup", "Standup", now.AddMinutes(1), joinUrl: "https://zoom.us/j/1");
        var tracker = new CalendarToastTracker();

        Assert.Null(tracker.NextToast([occurrence], now));
        Assert.Null(tracker.NextToast([occurrence], now.AddSeconds(30)));
    }

    [Theory]
    [InlineData("webcal://example.com/calendar.ics", "https://example.com/calendar.ics")]
    [InlineData(" https://example.com/calendar.ics ", "https://example.com/calendar.ics")]
    public void SubscriptionUrlsNormalizeSupportedSchemes(string input, string expected)
    {
        Assert.Equal([expected], CalendarSubscriptionUrl.NormalizeAll([input]));
    }

    [Fact]
    public void SubscriptionUrlsDropEmptyGarbageAndDuplicates()
    {
        Assert.Equal(
            ["https://example.com/a.ics"],
            CalendarSubscriptionUrl.NormalizeAll(["", "not a url", "https://example.com/a.ics", "webcal://example.com/a.ics"]));
    }

    private static CalendarOccurrence Occurrence(
        string uid,
        string title,
        DateTimeOffset start,
        bool allDay = false,
        string? joinUrl = null) =>
        new(uid, title, start, start.AddMinutes(30), allDay, joinUrl);

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
