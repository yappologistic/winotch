using System.Globalization;

namespace Winotch;

public static class CalendarAgenda
{
    private const int TitleMaxLength = 10;

    public static IReadOnlyList<CalendarAgendaRow> Rows(
        IReadOnlyList<CalendarOccurrence> occurrences,
        DateTimeOffset now,
        bool use24HourClock)
    {
        var horizon = now.AddHours(24);
        return Deduplicate(occurrences)
            .Where(occurrence => occurrence.Start < horizon && (occurrence.Start >= now || occurrence.End > now))
            .OrderBy(occurrence => occurrence.Start)
            .ThenBy(occurrence => occurrence.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(3)
            .Select(occurrence => new CalendarAgendaRow(
                FormatAgendaTime(occurrence, now, use24HourClock),
                occurrence.Title,
                occurrence.JoinUrl))
            .ToArray();
    }

    public static CalendarOccurrence? SelectPillOccurrence(
        IReadOnlyList<CalendarOccurrence> occurrences,
        DateTimeOffset now,
        bool focusActive)
    {
        if (focusActive)
        {
            return null;
        }

        return Deduplicate(occurrences)
            .Where(occurrence => CountdownText(occurrence, now) is not null)
            .OrderBy(occurrence => occurrence.Start)
            .ThenBy(occurrence => occurrence.Title, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    public static string? CountdownText(CalendarOccurrence occurrence, DateTimeOffset now)
    {
        if (occurrence.IsAllDay)
        {
            return null;
        }

        var untilStart = occurrence.Start - now;
        if (untilStart > TimeSpan.FromMinutes(15) || now >= occurrence.Start.AddMinutes(5))
        {
            return null;
        }

        var countdown = untilStart > TimeSpan.Zero
            ? $"{Math.Max(1, (int)Math.Ceiling(untilStart.TotalMinutes))}m"
            : "now";
        return $"{Truncate(occurrence.Title)} \u00b7 {countdown}";
    }

    public static string FormatUpdatedAge(DateTimeOffset? updatedUtc, DateTimeOffset now)
    {
        if (updatedUtc is null)
        {
            return "not updated yet";
        }

        var age = now - updatedUtc.Value;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "updated now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"updated {(int)Math.Floor(age.TotalMinutes)}m ago";
        }

        return $"updated {(int)Math.Floor(age.TotalHours)}h ago";
    }

    private static IEnumerable<CalendarOccurrence> Deduplicate(IReadOnlyList<CalendarOccurrence> occurrences)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in occurrences)
        {
            if (seen.Add(occurrence.Key))
            {
                yield return occurrence;
            }
        }
    }

    private static string FormatAgendaTime(CalendarOccurrence occurrence, DateTimeOffset now, bool use24HourClock)
    {
        if (occurrence.IsAllDay)
        {
            return "All day";
        }

        var local = occurrence.Start.LocalDateTime;
        var time = local.ToString(use24HourClock ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
        return local.Date == now.LocalDateTime.Date
            ? time
            : $"{local:ddd} {time}";
    }

    private static string Truncate(string title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "Meeting" : title.Trim();
        return value.Length <= TitleMaxLength ? value : string.Concat(value.AsSpan(0, TitleMaxLength - 3), "...");
    }
}
