namespace Winotch;

public sealed record TransientTimerSnapshot(
    bool IsActive,
    bool IsPaused,
    bool IsExpired,
    TimeSpan Remaining,
    double Progress)
{
    public static readonly TransientTimerSnapshot Stopped = new(
        IsActive: false,
        IsPaused: false,
        IsExpired: false,
        TimeSpan.Zero,
        Progress: 1);
}

public sealed record TransientTimerState(
    TimeSpan Duration,
    DateTimeOffset StartedUtc,
    TimeSpan PausedRemaining,
    bool IsPaused)
{
    public static readonly TransientTimerState Stopped = new(
        TimeSpan.Zero,
        DateTimeOffset.MinValue,
        TimeSpan.Zero,
        IsPaused: false);

    public static TransientTimerState Start(TimeSpan duration, DateTimeOffset nowUtc) =>
        new(duration, nowUtc, duration, IsPaused: false);

    public TransientTimerState Pause(DateTimeOffset nowUtc) =>
        Duration <= TimeSpan.Zero || IsPaused
            ? this
            : this with { PausedRemaining = RemainingAt(nowUtc), IsPaused = true };

    public TransientTimerState Resume(DateTimeOffset nowUtc) =>
        Duration <= TimeSpan.Zero || !IsPaused
            ? this
            : this with { StartedUtc = nowUtc - (Duration - PausedRemaining), IsPaused = false };

    public TransientTimerSnapshot Snapshot(DateTimeOffset nowUtc)
    {
        if (Duration <= TimeSpan.Zero)
        {
            return TransientTimerSnapshot.Stopped;
        }

        var remaining = IsPaused ? PausedRemaining : RemainingAt(nowUtc);
        var progress = Math.Clamp(1 - remaining.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
        var expired = !IsPaused && remaining <= TimeSpan.Zero;
        return new TransientTimerSnapshot(
            IsActive: !expired,
            IsPaused,
            expired,
            remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining,
            progress);
    }

    private TimeSpan RemainingAt(DateTimeOffset nowUtc) =>
        Duration - (nowUtc - StartedUtc);
}
