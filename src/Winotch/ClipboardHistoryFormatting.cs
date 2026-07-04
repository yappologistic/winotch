namespace Winotch;

public static class ClipboardHistoryFormatting
{
    public static string RelativeTime(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var elapsed = now - capturedAt;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h";
        }

        return $"{(int)elapsed.TotalDays}d";
    }
}
