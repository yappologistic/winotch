using System.Globalization;

namespace Winotch;

public sealed class LiveActivityService
{
    private readonly LiveCallDetector _callDetector;
    private TransientTimerState _timer = TransientTimerState.Stopped;
    private DateTimeOffset? _callStartedUtc;

    public LiveActivityService()
        : this(LiveCallDetector.CreateDefault())
    {
    }

    public LiveActivityService(LiveCallDetector callDetector)
    {
        _callDetector = callDetector;
    }

    public void StartTimer(TimeSpan duration, DateTimeOffset nowUtc)
    {
        if (duration <= TimeSpan.Zero)
        {
            _timer = TransientTimerState.Stopped;
            return;
        }

        _timer = TransientTimerState.Start(duration, nowUtc);
    }

    public void PauseTimer(DateTimeOffset nowUtc)
    {
        _timer = _timer.Pause(nowUtc);
    }

    public void ResumeTimer(DateTimeOffset nowUtc)
    {
        _timer = _timer.Resume(nowUtc);
    }

    public void CancelTimer()
    {
        _timer = TransientTimerState.Stopped;
    }

    public LiveActivity Update(LiveActivityInput input)
    {
        var dots = input.Settings.ActivityDotsEnabled
            ? LiveActivityDots.FromPrivacy(input.Privacy)
            : [];
        var call = input.Settings.CallDetectionEnabled ? _callDetector.Detect() : new LiveCallSnapshot(false);
        var timer = input.Settings.TransientTimerEnabled ? _timer.Snapshot(input.NowUtc) : TransientTimerSnapshot.Stopped;
        if (timer.IsExpired)
        {
            _timer = TransientTimerState.Stopped;
        }

        // The active strip shows one activity. Dots are still carried as stacked context for the renderer.
        if (call.IsActive)
        {
            _callStartedUtc ??= input.NowUtc;
            return CreateCall(input.NowUtc, dots, StackCount(timer, input.Media, dots));
        }

        _callStartedUtc = null;
        if (timer.IsActive)
        {
            return CreateTimer(timer, dots, StackCount(input.Media, dots));
        }

        if (input.Settings.NowPlayingStripEnabled && input.Media.IsPlaying)
        {
            return CreateMedia(input.Media, dots, dots.Count);
        }

        if (dots.Count > 0)
        {
            return new LiveActivity(
                LiveActivityKind.ActivityDots,
                ShellMode.Live,
                "Live",
                string.Join(" · ", dots.Select(dot => dot.Label)),
                "",
                1,
                TimeSpan.Zero,
                TimerPaused: false,
                Thumbnail: null,
                Dots: dots,
                StackedActivityCount: Math.Max(0, dots.Count - 1));
        }

        return timer.IsExpired ? LiveActivity.None with { Progress = 1 } : LiveActivity.None;
    }

    private LiveActivity CreateCall(DateTimeOffset nowUtc, IReadOnlyList<LiveActivityDot> dots, int stackedCount)
    {
        var elapsed = nowUtc - (_callStartedUtc ?? nowUtc);
        return new LiveActivity(
            LiveActivityKind.Call,
            ShellMode.Live,
            "Call",
            "Active meeting",
            FormatDuration(elapsed),
            1,
            TimeSpan.Zero,
            TimerPaused: false,
            Thumbnail: null,
            Dots: dots,
            StackedActivityCount: stackedCount);
    }

    private static LiveActivity CreateTimer(
        TransientTimerSnapshot timer,
        IReadOnlyList<LiveActivityDot> dots,
        int stackedCount) =>
        new(
            LiveActivityKind.Timer,
            ShellMode.Live,
            timer.IsPaused ? "Timer paused" : "Timer",
            FormatRemaining(timer.Remaining),
            FormatRemaining(timer.Remaining),
            timer.Progress,
            timer.Remaining,
            timer.IsPaused,
            Thumbnail: null,
            Dots: dots,
            StackedActivityCount: stackedCount);

    private static LiveActivity CreateMedia(MediaSnapshot media, IReadOnlyList<LiveActivityDot> dots, int stackedCount) =>
        new(
            LiveActivityKind.NowPlaying,
            ShellMode.Live,
            media.DisplayTitle,
            media.DisplayArtist,
            FormatDuration(media.Position ?? TimeSpan.Zero),
            media.TimelineProgress,
            TimeSpan.Zero,
            TimerPaused: false,
            media.Thumbnail,
            dots,
            StackedActivityCount: stackedCount);

    private static int StackCount(TransientTimerSnapshot timer, MediaSnapshot media, IReadOnlyList<LiveActivityDot> dots) =>
        (timer.IsActive ? 1 : 0) + (media.IsPlaying ? 1 : 0) + dots.Count;

    private static int StackCount(MediaSnapshot media, IReadOnlyList<LiveActivityDot> dots) =>
        (media.IsPlaying ? 1 : 0) + dots.Count;

    public static string FormatRemaining(TimeSpan remaining)
    {
        var clamped = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return clamped.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)clamped.TotalHours}:{clamped.Minutes:00}:{clamped.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{clamped.Minutes}:{clamped.Seconds:00}");
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        var clamped = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        return clamped.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)clamped.TotalHours}:{clamped.Minutes:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{clamped.Minutes}:{clamped.Seconds:00}");
    }
}
