namespace Winotch.Tests;

public class IcsParserTests
{
    [Fact]
    public void ParserUnfoldsLinesSkipsCancelledAndKeepsParsingAfterMalformedLines()
    {
        var ics = string.Join("\r\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:standup",
            "DTSTART:20260704T160000Z",
            "DTEND:20260704T163000Z",
            "SUMMARY:Daily ",
            " standup",
            "DESCRIPTION:Join https://meet.google.com/abc-defg-hij",
            "BROKEN-LINE",
            "END:VEVENT",
            "BEGIN:VEVENT",
            "UID:cancelled",
            "STATUS:CANCELLED",
            "DTSTART:20260704T170000Z",
            "SUMMARY:Skip me",
            "END:VEVENT",
            "END:VCALENDAR");

        var events = IcsParser.Parse(ics);

        var calendarEvent = Assert.Single(events);
        Assert.Equal("standup", calendarEvent.Uid);
        Assert.Equal("Daily standup", calendarEvent.Title);
        Assert.Equal("https://meet.google.com/abc-defg-hij", calendarEvent.JoinUrl);
    }

    [Fact]
    public void ParserUsesDurationWhenEndIsMissing()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:duration",
            "DTSTART:20260704T100000Z",
            "DURATION:PT45M",
            "SUMMARY:Planning",
            "URL:https://teams.live.com/meet/123",
            "END:VEVENT",
            "END:VCALENDAR")));

        Assert.Equal(TimeSpan.FromMinutes(45), calendarEvent.Duration);
        Assert.Equal("https://teams.live.com/meet/123", calendarEvent.JoinUrl);
    }

    [Fact]
    public void ParserIgnoresMalformedDuplicateRruleParts()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:duplicate-rule",
            "DTSTART:20260704T100000Z",
            "DURATION:PT15M",
            "RRULE:FREQ=DAILY;COUNT=2;COUNT=bogus",
            "SUMMARY:Malformed rule",
            "END:VEVENT",
            "END:VCALENDAR")));

        var occurrences = IcsRecurrence.Expand(
            [calendarEvent],
            new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, occurrences.Count);
    }

    [Fact]
    public void ParserFallsBackWhenDurationOverflows()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:huge-duration",
            "DTSTART:20260704T100000Z",
            "DURATION:P2147483647W",
            "SUMMARY:Huge duration",
            "END:VEVENT",
            "END:VCALENDAR")));

        Assert.Equal(TimeSpan.Zero, calendarEvent.Duration);
    }

    [Fact]
    public void AllDayDateValuesStayAgendaOnly()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:offsite",
            "DTSTART;VALUE=DATE:20260704",
            "DTEND;VALUE=DATE:20260705",
            "SUMMARY:Company offsite",
            "END:VEVENT",
            "END:VCALENDAR")));
        var now = LocalTime(2026, 7, 4, 12, 0);

        var occurrence = Assert.Single(IcsRecurrence.Expand([calendarEvent], now.AddHours(-1), now.AddHours(12)));

        Assert.True(occurrence.IsAllDay);
        Assert.Equal("All day", CalendarAgenda.Rows([occurrence], now, use24HourClock: true)[0].TimeText);
        Assert.Null(CalendarAgenda.CountdownText(occurrence, now));
    }

    [Fact]
    public void AllDayDateWithoutEndLastsThroughTheDay()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:holiday",
            "DTSTART;VALUE=DATE:20260704",
            "SUMMARY:Holiday",
            "END:VEVENT",
            "END:VCALENDAR")));
        var now = LocalTime(2026, 7, 4, 12, 0);

        var occurrence = Assert.Single(IcsRecurrence.Expand([calendarEvent], now.AddHours(-1), now.AddHours(12)));

        Assert.True(occurrence.IsAllDay);
        Assert.Equal(TimeSpan.FromDays(1), occurrence.End - occurrence.Start);
        Assert.Equal("Holiday", CalendarAgenda.Rows([occurrence], now, use24HourClock: true)[0].Title);
    }

    [Fact]
    public void TimeZoneRecurrenceKeepsLocalTimeAcrossDst()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:dst",
            "DTSTART;TZID=America/New_York:20260307T090000",
            "DURATION:PT30M",
            "RRULE:FREQ=DAILY;COUNT=3",
            "SUMMARY:DST check",
            "END:VEVENT",
            "END:VCALENDAR")));
        var windowStart = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);

        var occurrences = IcsRecurrence.Expand([calendarEvent], windowStart, windowStart.AddDays(4));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new TimeSpan(-5, 0, 0), occurrences[0].Start.Offset);
        Assert.Equal(new TimeSpan(-4, 0, 0), occurrences[1].Start.Offset);
        Assert.All(occurrences, occurrence => Assert.Equal(9, occurrence.Start.Hour));
    }

    [Fact]
    public void WeeklyByDayCountAndExdateRemoveExpectedOccurrence()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:weekly",
            "DTSTART:20260706T090000Z",
            "DURATION:PT15M",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=5",
            "EXDATE:20260708T090000Z",
            "SUMMARY:Workout",
            "END:VEVENT",
            "END:VCALENDAR")));

        var occurrences = IcsRecurrence.Expand(
            [calendarEvent],
            new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            [6, 10, 13, 15],
            occurrences.Select(occurrence => occurrence.Start.Day).ToArray());
    }

    [Fact]
    public void MonthlyByMonthDayStopsAtUntil()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:monthly",
            "DTSTART:20260704T120000Z",
            "DURATION:PT1H",
            "RRULE:FREQ=MONTHLY;BYMONTHDAY=15;UNTIL=20260901T000000Z",
            "SUMMARY:Billing",
            "END:VEVENT",
            "END:VCALENDAR")));

        var occurrences = IcsRecurrence.Expand(
            [calendarEvent],
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal([7, 8], occurrences.Select(occurrence => occurrence.Start.Month).ToArray());
    }

    [Fact]
    public void DailyRecurrenceStartingLongAgoStillReachesWindow()
    {
        var calendarEvent = Assert.Single(IcsParser.Parse(string.Join("\n",
            "BEGIN:VCALENDAR",
            "BEGIN:VEVENT",
            "UID:legacy-daily",
            "DTSTART:19900101T090000Z",
            "DURATION:PT15M",
            "RRULE:FREQ=DAILY",
            "SUMMARY:Legacy daily",
            "END:VEVENT",
            "END:VCALENDAR")));

        var occurrences = IcsRecurrence.Expand(
            [calendarEvent],
            new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(occurrences, occurrence =>
            occurrence.Start == new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("https://zoom.us/j/12345?pwd=x.", "https://zoom.us/j/12345?pwd=x")]
    [InlineData("join https://teams.microsoft.com/l/meetup-join/abc/0", "https://teams.microsoft.com/l/meetup-join/abc/0")]
    [InlineData("https://teams.live.com/meet/123", "https://teams.live.com/meet/123")]
    [InlineData("meet https://meet.google.com/abc-defg-hij)", "https://meet.google.com/abc-defg-hij")]
    [InlineData("join https://teams.microsoft.com/l/meetup-join/abc?context=one&amp;tenant=two", "https://teams.microsoft.com/l/meetup-join/abc?context=one&tenant=two")]
    public void JoinLinkDetectorFindsSupportedMeetingUrls(string text, string expected)
    {
        Assert.Equal(expected, JoinLinkDetector.FindFirst(text));
    }

    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
