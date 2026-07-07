namespace Winotch;

// Live Activities settings: proactive notch morphing for ongoing activities.
// Owned by the Live Activities feature; expand fields here without touching SettingsService.cs.
public sealed record LiveActivitySettings
{
    // Tiny colored dots on the pill edge for camera/mic/screen-share activity.
    public bool ActivityDotsEnabled { get; init; } = true;
    // Slim artwork + title + scrubber extending the pill while media plays.
    public bool NowPlayingStripEnabled { get; init; } = true;
    // Quick transient countdown timer with a progress ring (separate from the focus timer).
    public bool TransientTimerEnabled { get; init; } = true;
    // Detect Teams/Zoom/Meet in-call state via process + window-title heuristics. Off by default.
    public bool CallDetectionEnabled { get; init; }

    public LiveActivitySettings Normalize() => this;
}
