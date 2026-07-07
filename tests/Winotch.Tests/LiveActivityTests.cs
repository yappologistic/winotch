namespace Winotch.Tests;

public class LiveActivityTests
{
    [Fact]
    public void LiveActivityArbitrationSelectsHighestPriorityEnabledActivity()
    {
        var service = new LiveActivityService(new LiveCallDetector(() =>
        [
            new LiveCallWindow("Teams", "Weekly sync meeting")
        ]));
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        service.StartTimer(TimeSpan.FromMinutes(5), now.AddMinutes(-1));

        var activity = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities with { CallDetectionEnabled = true },
            new PrivacyActivitySnapshot(CameraActive: true, MicrophoneActive: true, ScreenShareActive: false),
            new MediaSnapshot("Song", "Artist", "Spotify.exe", null, MediaState.Playing, false, true, true, false),
            now));

        Assert.Equal(LiveActivityKind.Call, activity.Kind);
        Assert.Equal(ShellMode.Live, activity.ShellMode);
        Assert.Equal("Call", activity.Title);
    }

    [Fact]
    public void LiveActivityArbitrationFallsBackThroughTimerMediaAndDots()
    {
        var service = new LiveActivityService(new LiveCallDetector(() => []));
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        service.StartTimer(TimeSpan.FromMinutes(5), now.AddMinutes(-1));

        var timer = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(CameraActive: true, MicrophoneActive: false, ScreenShareActive: false),
            new MediaSnapshot("Song", "Artist", "Spotify.exe", null, MediaState.Playing, false, true, true, false),
            now));
        Assert.Equal(LiveActivityKind.Timer, timer.Kind);

        service.CancelTimer();
        var media = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(CameraActive: true, MicrophoneActive: false, ScreenShareActive: false),
            new MediaSnapshot("Song", "Artist", "Spotify.exe", null, MediaState.Playing, false, true, true, false),
            now));
        Assert.Equal(LiveActivityKind.NowPlaying, media.Kind);

        var dots = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities with { NowPlayingStripEnabled = false },
            new PrivacyActivitySnapshot(CameraActive: true, MicrophoneActive: false, ScreenShareActive: false),
            MediaSnapshot.Empty,
            now));
        Assert.Equal(LiveActivityKind.ActivityDots, dots.Kind);
    }

    [Fact]
    public void NowPlayingActivityUsesMediaTimelineProgress()
    {
        var service = new LiveActivityService(new LiveCallDetector(() => []));
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var media = new MediaSnapshot("Song", "Artist", "Spotify.exe", null, MediaState.Playing, false, true, true, false)
        {
            Position = TimeSpan.FromSeconds(45),
            Duration = TimeSpan.FromMinutes(3)
        };

        var activity = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(false, false, false),
            media,
            now));

        Assert.Equal(LiveActivityKind.NowPlaying, activity.Kind);
        Assert.Equal(0.25, activity.Progress, precision: 3);
        Assert.Equal("0:45", activity.TimeText);
    }

    [Fact]
    public void LiveActivityModeReturnsMiniWhenNoActivityIsActive()
    {
        var service = new LiveActivityService(new LiveCallDetector(() => []));

        var activity = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(CameraActive: false, MicrophoneActive: false, ScreenShareActive: false),
            MediaSnapshot.Empty,
            DateTimeOffset.UtcNow));

        Assert.Equal(LiveActivityKind.None, activity.Kind);
        Assert.Equal(ShellMode.Mini, activity.ShellMode);
    }

    [Fact]
    public void TransientTimerUsesWallClockRemainingProgressAndClamp()
    {
        var service = new LiveActivityService(new LiveCallDetector(() => []));
        var start = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        service.StartTimer(TimeSpan.FromMinutes(5), start);

        var halfway = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(false, false, false),
            MediaSnapshot.Empty,
            start.AddMinutes(2.5)));

        Assert.Equal(LiveActivityKind.Timer, halfway.Kind);
        Assert.Equal(TimeSpan.FromMinutes(2.5), halfway.Remaining);
        Assert.Equal(0.5, halfway.Progress, precision: 3);

        var expired = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(false, false, false),
            MediaSnapshot.Empty,
            start.AddMinutes(8)));

        Assert.Equal(LiveActivityKind.None, expired.Kind);
        Assert.Equal(1, expired.Progress);
        Assert.Equal(TimeSpan.Zero, expired.Remaining);
    }

    [Fact]
    public void TransientTimerPauseFreezesRemainingUntilResume()
    {
        var service = new LiveActivityService(new LiveCallDetector(() => []));
        var start = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        service.StartTimer(TimeSpan.FromMinutes(10), start);
        service.PauseTimer(start.AddMinutes(3));

        var paused = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(false, false, false),
            MediaSnapshot.Empty,
            start.AddMinutes(7)));

        Assert.True(paused.TimerPaused);
        Assert.Equal(TimeSpan.FromMinutes(7), paused.Remaining);

        service.ResumeTimer(start.AddMinutes(8));
        var resumed = service.Update(new LiveActivityInput(
            WinotchSettings.Defaults.LiveActivities,
            new PrivacyActivitySnapshot(false, false, false),
            MediaSnapshot.Empty,
            start.AddMinutes(10)));

        Assert.False(resumed.TimerPaused);
        Assert.Equal(TimeSpan.FromMinutes(5), resumed.Remaining);
    }

    [Theory]
    [InlineData("Teams", "Weekly sync meeting", true)]
    [InlineData("Zoom", "Zoom Meeting", true)]
    [InlineData("chrome", "Team standup - Google Meet", true)]
    [InlineData("notepad", "meeting notes", false)]
    [InlineData("Teams", "Calendar", false)]
    public void CallDetectionMapsProcessAndTitleHeuristics(string processName, string title, bool expected)
    {
        var detector = new LiveCallDetector(() => [new LiveCallWindow(processName, title)]);

        Assert.Equal(expected, detector.Detect().IsActive);
    }

    [Fact]
    public void ActivityDotsUseRequiredColorMapping()
    {
        var dots = LiveActivityDots.FromPrivacy(new PrivacyActivitySnapshot(
            CameraActive: true,
            MicrophoneActive: true,
            ScreenShareActive: true));

        Assert.Collection(
            dots,
            dot =>
            {
                Assert.Equal(LiveActivityDotKind.Camera, dot.Kind);
                Assert.Equal("#FF9F0A", dot.ColorHex);
            },
            dot =>
            {
                Assert.Equal(LiveActivityDotKind.Microphone, dot.Kind);
                Assert.Equal("#FF453A", dot.ColorHex);
            },
            dot =>
            {
                Assert.Equal(LiveActivityDotKind.ScreenShare, dot.Kind);
                Assert.Equal("#BF5AF2", dot.ColorHex);
            });
    }
}
