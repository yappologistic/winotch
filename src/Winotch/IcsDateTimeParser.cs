using System.Globalization;
using System.Text.RegularExpressions;

namespace Winotch;

internal static class IcsDateTimeParser
{
    private static readonly string[] DateTimeFormats = ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"];
    private static readonly Regex DurationPattern = new(
        "^P(?:(?<weeks>\\d+)W)?(?:(?<days>\\d+)D)?(?:T(?:(?<hours>\\d+)H)?(?:(?<minutes>\\d+)M)?(?:(?<seconds>\\d+)S)?)?$",
        RegexOptions.CultureInvariant);

    public static bool TryParse(IcsProperty property, TimeZoneInfo? fallbackZone, out CalendarDateTime value)
    {
        property.Parameters.TryGetValue("TZID", out var timeZoneId);
        property.Parameters.TryGetValue("VALUE", out var valueKind);
        return TryParseValue(property.Value, valueKind, timeZoneId, fallbackZone, out value);
    }

    public static bool TryParseValue(string raw, TimeZoneInfo? fallbackZone, out CalendarDateTime value) =>
        TryParseValue(raw, null, null, fallbackZone, out value);

    public static bool TryParseDuration(string raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = DurationPattern.Match(raw.Trim());
        if (!match.Success)
        {
            return false;
        }

        var weeks = Number(match, "weeks");
        var days = Number(match, "days");
        var hours = Number(match, "hours");
        var minutes = Number(match, "minutes");
        var seconds = Number(match, "seconds");
        if (weeks + days + hours + minutes + seconds == 0)
        {
            return false;
        }

        try
        {
            duration = TimeSpan.FromDays(((long)weeks * 7) + days) +
                TimeSpan.FromHours(hours) +
                TimeSpan.FromMinutes(minutes) +
                TimeSpan.FromSeconds(seconds);
            return true;
        }
        catch (OverflowException)
        {
            duration = TimeSpan.Zero;
            return false;
        }
    }

    private static bool TryParseValue(
        string raw,
        string? valueKind,
        string? timeZoneId,
        TimeZoneInfo? fallbackZone,
        out CalendarDateTime value)
    {
        value = new CalendarDateTime(DateTime.MinValue, TimeZoneInfo.Local, false);
        var text = raw.Trim();
        var isDate = string.Equals(valueKind, "DATE", StringComparison.OrdinalIgnoreCase) ||
            (text.Length == 8 && text.All(char.IsDigit));
        if (isDate)
        {
            if (!DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return false;
            }

            value = new CalendarDateTime(date.Date, fallbackZone ?? TimeZoneInfo.Local, true);
            return true;
        }

        var isUtc = text.EndsWith('Z');
        if (isUtc)
        {
            text = text[..^1];
        }

        if (!DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        var timeZone = isUtc
            ? TimeZoneInfo.Utc
            : string.IsNullOrWhiteSpace(timeZoneId)
                ? fallbackZone ?? TimeZoneInfo.Local
                : CalendarTimeZoneResolver.Resolve(timeZoneId);
        value = new CalendarDateTime(local, timeZone, false);
        return true;
    }

    private static int Number(Match match, string groupName) =>
        int.TryParse(match.Groups[groupName].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
