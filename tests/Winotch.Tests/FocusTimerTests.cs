namespace Winotch.Tests;

public class FocusTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartCreatesRunningFocusSnapshot()
    {
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start);

        var snapshot = timer.SnapshotAt(Start);

        Assert.Equal(FocusTimerStatus.Running, snapshot.Status);
        Assert.Equal(FocusTimerPhase.Focus, snapshot.Phase);
        Assert.Equal("Focus", snapshot.PhaseLabel);
        Assert.Equal("25:00", snapshot.RemainingText);
        Assert.Equal(0, snapshot.Progress);
    }

    [Fact]
    public void PauseResumeUsesWallClockWithoutCountingPausedTime()
    {
        var timer = FocusTimerState
            .Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start)
            .Pause(Start.AddMinutes(5));

        Assert.Equal("20:00", timer.SnapshotAt(Start.AddMinutes(30)).RemainingText);

        var resumed = timer.Resume(Start.AddMinutes(40));

        Assert.Equal("15:00", resumed.SnapshotAt(Start.AddMinutes(45)).RemainingText);
    }

    [Fact]
    public void FocusCompletionAdvancesToBreak()
    {
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start);

        var advanced = timer.AdvanceTo(Start.AddMinutes(26));

        Assert.Equal(FocusTimerPhase.Break, advanced.State.Phase);
        Assert.Equal(FocusTimerStatus.Running, advanced.State.Status);
        Assert.Equal("04:00", advanced.State.SnapshotAt(Start.AddMinutes(26)).RemainingText);
        var completion = Assert.Single(advanced.Completions);
        Assert.Equal(FocusTimerCompletionKind.FocusComplete, completion.Kind);
        Assert.Equal("Focus complete \u2014 5:00 break", completion.ToastTitle);
    }

    [Fact]
    public void BreakCompletionStopsWhenAutoCycleIsOff()
    {
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start);

        var advanced = timer.AdvanceTo(Start.AddMinutes(31));

        Assert.Equal(FocusTimerStatus.Stopped, advanced.State.Status);
        Assert.Collection(
            advanced.Completions,
            first => Assert.Equal(FocusTimerCompletionKind.FocusComplete, first.Kind),
            second => Assert.Equal(FocusTimerCompletionKind.BreakComplete, second.Kind));
        Assert.Equal("Break over", advanced.Completions[^1].ToastTitle);
    }

    [Fact]
    public void AutoCycleChainsAcrossLongSleepGaps()
    {
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), true), Start);

        var advanced = timer.AdvanceTo(Start.AddMinutes(61));

        Assert.Equal(FocusTimerStatus.Running, advanced.State.Status);
        Assert.Equal(FocusTimerPhase.Focus, advanced.State.Phase);
        Assert.Equal(2, advanced.State.CompletedFocusCycles);
        Assert.Equal("24:00", advanced.State.SnapshotAt(Start.AddMinutes(61)).RemainingText);
        Assert.Equal(4, advanced.Completions.Count);
        Assert.Equal(2, advanced.Completions.Count(completion => completion.Kind == FocusTimerCompletionKind.FocusComplete));
        Assert.Equal(2, advanced.Completions.Count(completion => completion.Kind == FocusTimerCompletionKind.BreakComplete));
    }

    [Fact]
    public void SkipDuringPauseStartsTheNextPhaseWithoutPausedResidue()
    {
        var timer = FocusTimerState
            .Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), true), Start)
            .Pause(Start.AddMinutes(10));

        var skipped = timer.Skip(Start.AddMinutes(20));

        Assert.Equal(FocusTimerStatus.Running, skipped.Status);
        Assert.Equal(FocusTimerPhase.Break, skipped.Phase);
        Assert.Null(skipped.PausedAtUtc);
        Assert.Equal("05:00", skipped.SnapshotAt(Start.AddMinutes(20)).RemainingText);
    }

    [Fact]
    public void StartingNewTimerUsesFreshTiming()
    {
        var restarted = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10), false), Start.AddMinutes(12));

        Assert.Equal(FocusTimerPhase.Focus, restarted.Phase);
        Assert.Equal(0, restarted.CompletedFocusCycles);
        Assert.Equal("50:00", restarted.SnapshotAt(Start.AddMinutes(12)).RemainingText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("181")]
    public void CustomMinutesRejectsGarbageAndOutOfRangeInput(string text)
    {
        Assert.False(FocusTimerSettings.TryCreateCustom(text, autoCycle: false, out _, out _));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 45 ", 45)]
    [InlineData("180", 180)]
    public void CustomMinutesCreatesFocusWithDefaultBreak(string text, int expectedMinutes)
    {
        Assert.True(FocusTimerSettings.TryCreateCustom(text, autoCycle: true, out var settings, out _));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), settings.FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.BreakDuration);
        Assert.True(settings.AutoCycle);
    }

    [Theory]
    [InlineData(-1, "00:00")]
    [InlineData(0, "00:00")]
    [InlineData(61, "01:01")]
    public void RemainingFormatterClampsNegativeValues(int seconds, string expected)
    {
        Assert.Equal(expected, FocusTimerFormatter.FormatRemaining(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ProgressFractionIsClamped()
    {
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start);

        Assert.Equal(0, timer.ProgressAt(Start.AddMinutes(-1)));
        Assert.Equal(0.5, timer.ProgressAt(Start.AddMinutes(12.5)), precision: 3);
        Assert.Equal(1, timer.ProgressAt(Start.AddMinutes(30)));
    }

    [Fact]
    public void StoreRoundTripsPausedTimerWithoutExpiringIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var store = new FocusTimerStore(path);
        var timer = FocusTimerState
            .Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start)
            .Pause(Start.AddMinutes(8));

        try
        {
            store.Save(timer);

            var loaded = store.Load(Start.AddHours(2));

            Assert.Empty(loaded.Completions);
            Assert.Equal(FocusTimerStatus.Paused, loaded.State.Status);
            Assert.Equal("17:00", loaded.State.SnapshotAt(Start.AddHours(2)).RemainingText);
        }
        finally
        {
            store.Clear();
        }
    }

    [Fact]
    public void StoreAdvancesExpiredTimerOnceWhenLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var store = new FocusTimerStore(path);
        var timer = FocusTimerState.Start(new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Start);

        try
        {
            store.Save(timer);

            var loaded = store.Load(Start.AddMinutes(40));
            var loadedAgain = store.Load(Start.AddMinutes(41));

            Assert.Equal(FocusTimerStatus.Stopped, loaded.State.Status);
            Assert.Equal("Break over", loaded.Completions[^1].ToastTitle);
            Assert.Empty(loadedAgain.Completions);
            Assert.Equal(FocusTimerStatus.Stopped, loadedAgain.State.Status);
        }
        finally
        {
            store.Clear();
        }
    }

    [Fact]
    public void StoreDropsMalformedEnumState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        File.WriteAllText(path, """
            {
              "Status": "Running",
              "Phase": 99,
              "FocusDurationTicks": 15000000000,
              "BreakDurationTicks": 3000000000,
              "AutoCycle": false,
              "PhaseStartedAtUtc": "2026-07-04T10:00:00+00:00",
              "PausedElapsedTicks": 0,
              "PausedAtUtc": null,
              "CompletedFocusCycles": 0
            }
            """);
        var store = new FocusTimerStore(path);

        try
        {
            var loaded = store.Load(Start);

            Assert.Equal(FocusTimerStatus.Stopped, loaded.State.Status);
            Assert.Empty(loaded.Completions);
        }
        finally
        {
            store.Clear();
        }
    }
}
