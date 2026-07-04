namespace Winotch;

public sealed record CalendarDateTime(DateTime LocalDateTime, TimeZoneInfo TimeZone, bool IsDate)
{
    public DateTimeOffset ToOffset()
    {
        var local = DateTime.SpecifyKind(LocalDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}

public sealed record CalendarEvent(
    string Uid,
    string Title,
    CalendarDateTime Start,
    TimeSpan Duration,
    bool IsAllDay,
    string? JoinUrl,
    CalendarRecurrenceRule? Recurrence,
    IReadOnlyList<CalendarDateTime> ExDates);

public sealed record CalendarOccurrence(
    string Uid,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? JoinUrl)
{
    public string Key => string.Concat(Uid, "\u001f", Start.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public bool HasJoin => !string.IsNullOrWhiteSpace(JoinUrl);
}

public enum CalendarFrequency
{
    Daily,
    Weekly,
    Monthly
}

public sealed record CalendarRecurrenceRule(
    CalendarFrequency Frequency,
    int Interval,
    int? Count,
    CalendarDateTime? Until,
    IReadOnlyList<DayOfWeek> ByDays,
    IReadOnlyList<int> ByMonthDays);

public sealed record CalendarAgendaRow(string TimeText, string Title, string? JoinUrl)
{
    public bool HasJoin => !string.IsNullOrWhiteSpace(JoinUrl);
}

public sealed record CalendarRefreshResult(IReadOnlyList<CalendarEvent> Events, DateTimeOffset? LastUpdatedUtc);
