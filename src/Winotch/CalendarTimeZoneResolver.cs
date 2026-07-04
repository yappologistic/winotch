namespace Winotch;

public static class CalendarTimeZoneResolver
{
    private static readonly IReadOnlyDictionary<string, string> IanaToWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Detroit"] = "Eastern Standard Time",
        ["America/Toronto"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Winnipeg"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Edmonton"] = "Mountain Standard Time",
        ["America/Phoenix"] = "US Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["America/Vancouver"] = "Pacific Standard Time",
        ["America/Anchorage"] = "Alaskan Standard Time",
        ["Pacific/Honolulu"] = "Hawaiian Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Dublin"] = "GMT Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Europe/Madrid"] = "Romance Standard Time",
        ["Europe/Rome"] = "W. Europe Standard Time",
        ["Europe/Amsterdam"] = "W. Europe Standard Time",
        ["Europe/Zurich"] = "W. Europe Standard Time",
        ["Europe/Warsaw"] = "Central European Standard Time",
        ["Europe/Helsinki"] = "FLE Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Asia/Shanghai"] = "China Standard Time",
        ["Asia/Hong_Kong"] = "China Standard Time",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Asia/Kolkata"] = "India Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["Australia/Melbourne"] = "AUS Eastern Standard Time"
    };

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var value = timeZoneId?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeZoneInfo.Local;
        }

        if (value.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("GMT", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        if (TryFind(value, out var direct))
        {
            return direct;
        }

        if (IanaToWindows.TryGetValue(value, out var windowsId) && TryFind(windowsId, out var mapped))
        {
            return mapped;
        }

        return TimeZoneInfo.Local;
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Local;
        return false;
    }
}
