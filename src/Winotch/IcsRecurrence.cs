using System.Globalization;

namespace Winotch;

public static class IcsRecurrence
{
    private const int MaxGeneratedOccurrences = 50000;

    public static CalendarRecurrenceRule? Parse(string text, TimeZoneInfo fallbackZone)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2))
        {
            var key = pair[0].Trim().ToUpperInvariant();
            if (key.Length > 0)
            {
                parts.TryAdd(key, pair[1].Trim());
            }
        }

        if (!parts.TryGetValue("FREQ", out var frequencyText) || !TryFrequency(frequencyText, out var frequency))
        {
            return null;
        }

        var interval = parts.TryGetValue("INTERVAL", out var intervalText) &&
            int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedInterval) &&
            parsedInterval > 0
                ? parsedInterval
                : 1;
        var count = parts.TryGetValue("COUNT", out var countText) &&
            int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCount) &&
            parsedCount > 0
                ? parsedCount
                : (int?)null;
        var until = parts.TryGetValue("UNTIL", out var untilText) &&
            IcsDateTimeParser.TryParseValue(untilText, fallbackZone, out var parsedUntil)
                ? parsedUntil
                : null;

        return new CalendarRecurrenceRule(
            frequency,
            interval,
            count,
            until,
            ParseByDays(parts.GetValueOrDefault("BYDAY")),
            ParseByMonthDays(parts.GetValueOrDefault("BYMONTHDAY")));
    }

    public static IReadOnlyList<CalendarOccurrence> Expand(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        return events
            .SelectMany(calendarEvent => ExpandEvent(calendarEvent, windowStart, windowEnd))
            .OrderBy(occurrence => occurrence.Start)
            .ThenBy(occurrence => occurrence.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<CalendarOccurrence> ExpandEvent(
        CalendarEvent calendarEvent,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (calendarEvent.Recurrence is null)
        {
            var single = CreateOccurrence(calendarEvent, calendarEvent.Start.LocalDateTime);
            if (Intersects(single, windowStart, windowEnd))
            {
                yield return single;
            }

            yield break;
        }

        foreach (var localStart in LocalStarts(calendarEvent).Take(MaxGeneratedOccurrences))
        {
            var occurrence = CreateOccurrence(calendarEvent, localStart);
            if (!AllowedByRule(calendarEvent.Recurrence, occurrence.Start))
            {
                yield break;
            }

            if (occurrence.Start > windowEnd)
            {
                yield break;
            }

            if (!IsExcluded(calendarEvent, localStart, occurrence.Start) && Intersects(occurrence, windowStart, windowEnd))
            {
                yield return occurrence;
            }
        }
    }

    private static IEnumerable<DateTime> LocalStarts(CalendarEvent calendarEvent)
    {
        var rule = calendarEvent.Recurrence!;
        return rule.Frequency switch
        {
            CalendarFrequency.Daily => DailyStarts(calendarEvent.Start.LocalDateTime, rule.Interval, rule.Count),
            CalendarFrequency.Weekly => WeeklyStarts(calendarEvent.Start.LocalDateTime, rule),
            CalendarFrequency.Monthly => MonthlyStarts(calendarEvent.Start.LocalDateTime, rule),
            _ => []
        };
    }

    private static IEnumerable<DateTime> DailyStarts(DateTime start, int interval, int? count)
    {
        for (var index = 0; count is null || index < count; index++)
        {
            yield return start.AddDays(index * interval);
        }
    }

    private static IEnumerable<DateTime> WeeklyStarts(DateTime start, CalendarRecurrenceRule rule)
    {
        var days = rule.ByDays.Count == 0 ? [start.DayOfWeek] : rule.ByDays;
        var emitted = 0;
        for (var week = StartOfWeek(start); rule.Count is null || emitted < rule.Count; week = week.AddDays(7 * rule.Interval))
        {
            foreach (var day in days.OrderBy(DayOffset))
            {
                var candidate = week.AddDays(DayOffset(day)).Add(start.TimeOfDay);
                if (candidate < start)
                {
                    continue;
                }

                emitted++;
                yield return candidate;
                if (rule.Count is not null && emitted >= rule.Count)
                {
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<DateTime> MonthlyStarts(DateTime start, CalendarRecurrenceRule rule)
    {
        var days = rule.ByMonthDays.Count == 0 ? [start.Day] : rule.ByMonthDays;
        var emitted = 0;
        for (var cursor = new DateTime(start.Year, start.Month, 1); rule.Count is null || emitted < rule.Count; cursor = cursor.AddMonths(rule.Interval))
        {
            foreach (var day in days.OrderBy(day => day))
            {
                if (day < 1 || day > DateTime.DaysInMonth(cursor.Year, cursor.Month))
                {
                    continue;
                }

                var candidate = new DateTime(cursor.Year, cursor.Month, day).Add(start.TimeOfDay);
                if (candidate < start)
                {
                    continue;
                }

                emitted++;
                yield return candidate;
                if (rule.Count is not null && emitted >= rule.Count)
                {
                    yield break;
                }
            }
        }
    }

    private static CalendarOccurrence CreateOccurrence(CalendarEvent calendarEvent, DateTime localStart)
    {
        var start = new CalendarDateTime(localStart, calendarEvent.Start.TimeZone, calendarEvent.IsAllDay).ToOffset();
        return new CalendarOccurrence(
            calendarEvent.Uid,
            calendarEvent.Title,
            start,
            start + calendarEvent.Duration,
            calendarEvent.IsAllDay,
            calendarEvent.JoinUrl);
    }

    private static bool AllowedByRule(CalendarRecurrenceRule rule, DateTimeOffset start) =>
        rule.Until is null || start <= rule.Until.ToOffset();

    private static bool IsExcluded(CalendarEvent calendarEvent, DateTime localStart, DateTimeOffset start) =>
        calendarEvent.ExDates.Any(exDate => calendarEvent.IsAllDay
            ? exDate.LocalDateTime.Date == localStart.Date
            : exDate.ToOffset().UtcTicks == start.UtcTicks);

    private static bool Intersects(CalendarOccurrence occurrence, DateTimeOffset windowStart, DateTimeOffset windowEnd) =>
        occurrence.Start <= windowEnd && (occurrence.End > windowStart || occurrence.Start >= windowStart);

    private static bool TryFrequency(string text, out CalendarFrequency frequency)
    {
        frequency = text.ToUpperInvariant() switch
        {
            "DAILY" => CalendarFrequency.Daily,
            "WEEKLY" => CalendarFrequency.Weekly,
            "MONTHLY" => CalendarFrequency.Monthly,
            _ => (CalendarFrequency)(-1)
        };
        return Enum.IsDefined(frequency);
    }

    private static IReadOnlyList<DayOfWeek> ParseByDays(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseDay)
                .Where(day => day is not null)
                .Cast<DayOfWeek>()
                .Distinct()
                .ToArray();

    private static IReadOnlyList<int> ParseByMonthDays(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(day => int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0)
                .Where(day => day is >= 1 and <= 31)
                .Distinct()
                .ToArray();

    private static DayOfWeek? ParseDay(string text) => text.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null
    };

    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-DayOffset(date.DayOfWeek));

    private static int DayOffset(DayOfWeek day) => day == DayOfWeek.Sunday ? 6 : (int)day - 1;
}
