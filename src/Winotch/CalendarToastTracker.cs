namespace Winotch;

public sealed class CalendarToastTracker
{
    private static readonly TimeSpan ToastLeadTime = TimeSpan.FromMinutes(2);
    private readonly HashSet<string> _armed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _notified = new(StringComparer.OrdinalIgnoreCase);

    public CalendarOccurrence? NextToast(IReadOnlyList<CalendarOccurrence> occurrences, DateTimeOffset now)
    {
        foreach (var occurrence in occurrences
            .Where(occurrence => !occurrence.IsAllDay && occurrence.HasJoin)
            .OrderBy(occurrence => occurrence.Start)
            .ThenBy(occurrence => occurrence.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var toastAt = occurrence.Start - ToastLeadTime;
            if (now < toastAt)
            {
                _armed.Add(occurrence.Key);
                continue;
            }

            if (now >= occurrence.Start)
            {
                _armed.Remove(occurrence.Key);
                continue;
            }

            if (_notified.Add(occurrence.Key) && _armed.Remove(occurrence.Key))
            {
                return occurrence;
            }
        }

        return null;
    }
}
