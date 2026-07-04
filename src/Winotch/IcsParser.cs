namespace Winotch;

public static class IcsParser
{
    public static IReadOnlyList<CalendarEvent> Parse(string text)
    {
        var events = new List<CalendarEvent>();
        List<IcsProperty>? current = null;
        foreach (var property in IcsContent.ReadProperties(text ?? string.Empty))
        {
            if (property.Name == "BEGIN" && property.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = [];
                continue;
            }

            if (property.Name == "END" && property.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && BuildEvent(current) is { } calendarEvent)
                {
                    events.Add(calendarEvent);
                }

                current = null;
                continue;
            }

            current?.Add(property);
        }

        return events;
    }

    private static CalendarEvent? BuildEvent(IReadOnlyList<IcsProperty> properties)
    {
        if (Text(properties, "STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var startProperty = properties.LastOrDefault(property => property.Name == "DTSTART");
        if (startProperty is null || !IcsDateTimeParser.TryParse(startProperty, null, out var start))
        {
            return null;
        }

        var duration = Duration(properties, start);
        var title = Text(properties, "SUMMARY");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Untitled";
        }

        var uid = Text(properties, "UID");
        if (string.IsNullOrWhiteSpace(uid))
        {
            uid = string.Concat(title, "|", start.ToOffset().UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var location = Text(properties, "LOCATION");
        var description = Text(properties, "DESCRIPTION");
        var url = Text(properties, "URL");
        var recurrence = properties.LastOrDefault(property => property.Name == "RRULE") is { } rrule
            ? IcsRecurrence.Parse(rrule.Value, start.TimeZone)
            : null;

        return new CalendarEvent(
            uid,
            title.Trim(),
            start,
            duration,
            start.IsDate,
            JoinLinkDetector.FindFirst(location, description, url),
            recurrence,
            ExDates(properties, start.TimeZone));
    }

    private static TimeSpan Duration(IReadOnlyList<IcsProperty> properties, CalendarDateTime start)
    {
        var endProperty = properties.LastOrDefault(property => property.Name == "DTEND");
        if (endProperty is not null && IcsDateTimeParser.TryParse(endProperty, start.TimeZone, out var end))
        {
            var duration = end.ToOffset() - start.ToOffset();
            return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        var durationProperty = properties.LastOrDefault(property => property.Name == "DURATION");
        return durationProperty is not null && IcsDateTimeParser.TryParseDuration(durationProperty.Value, out var parsed)
            ? parsed
            : TimeSpan.Zero;
    }

    private static IReadOnlyList<CalendarDateTime> ExDates(IReadOnlyList<IcsProperty> properties, TimeZoneInfo fallbackZone)
    {
        var dates = new List<CalendarDateTime>();
        foreach (var property in properties.Where(property => property.Name == "EXDATE"))
        {
            foreach (var value in property.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var exdateProperty = property with { Value = value };
                if (IcsDateTimeParser.TryParse(exdateProperty, fallbackZone, out var exdate))
                {
                    dates.Add(exdate);
                }
            }
        }

        return dates;
    }

    private static string Text(IReadOnlyList<IcsProperty> properties, string name) =>
        IcsContent.UnescapeText(properties.LastOrDefault(property => property.Name == name)?.Value ?? string.Empty);
}
