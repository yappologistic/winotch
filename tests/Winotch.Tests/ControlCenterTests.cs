namespace Winotch.Tests;

public class ControlCenterTests
{
    [Theory]
    [InlineData("Spotify", "Spotify AB", "Music", "spotify", false, "Spotify")]
    [InlineData("", "Contoso Player", "Music", "player", false, "Contoso Player")]
    [InlineData("", "", "Zoom Meeting", "zoom", false, "Zoom Meeting")]
    [InlineData("", "", "", "audiodg", false, "audiodg")]
    [InlineData("", "", "", "", true, "System sounds")]
    [InlineData("", "", "", "", false, "Audio session")]
    public void AudioSessionNamingUsesTheFriendlyFallbackChain(
        string fileDescription,
        string productName,
        string sessionDisplayName,
        string processName,
        bool isSystemSounds,
        string expected)
    {
        var name = AudioSessionNaming.Resolve(
            fileDescription,
            productName,
            sessionDisplayName,
            processName,
            isSystemSounds);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void AudioDeviceOrderingKeepsDefaultFirstThenNames()
    {
        var devices = new[]
        {
            new AudioOutputDevice("headset", "USB Headset", false),
            new AudioOutputDevice("speakers", "Speakers", true),
            new AudioOutputDevice("monitor", "Display Audio", false)
        };

        var ordered = AudioDeviceOrdering.DefaultFirst(devices);

        Assert.Collection(
            ordered,
            first => Assert.True(first.IsDefault),
            second => Assert.Equal("Display Audio", second.Name),
            third => Assert.Equal("USB Headset", third.Name));
    }

    [Theory]
    [InlineData(20, 60, 100, 50)]
    [InlineData(20, 10, 100, 0)]
    [InlineData(20, 120, 100, 100)]
    [InlineData(90, 50, 10, 50)]
    public void BrightnessMathNormalizesDdcRanges(int minimum, int current, int maximum, int expected)
    {
        Assert.Equal(expected, BrightnessMath.ToPercent(minimum, current, maximum));
    }

    [Theory]
    [InlineData(20, 100, 50, 60)]
    [InlineData(20, 100, -10, 20)]
    [InlineData(20, 100, 120, 100)]
    [InlineData(90, 10, 75, 75)]
    public void BrightnessMathClampsWritesToDeviceRanges(int minimum, int maximum, int percent, int expected)
    {
        Assert.Equal(expected, BrightnessMath.FromPercent(minimum, maximum, percent));
    }

    [Fact]
    public async Task BrightnessWriterDebouncesRepeatedWritesPerDisplay()
    {
        var delays = new Queue<TaskCompletionSource>();
        var written = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var display = new BrightnessDisplay("ddc:1", "Display", 40, BrightnessDisplayKind.External);
        using var writer = new DebouncedBrightnessWriter(
            TimeSpan.FromMilliseconds(150),
            (_, percent, _) =>
            {
                written.TrySetResult(percent);
                return Task.CompletedTask;
            },
            (_, cancellationToken) =>
            {
                var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() => delay.TrySetCanceled(cancellationToken));
                delays.Enqueue(delay);
                return delay.Task;
            });

        writer.Queue(display, 10);
        writer.Queue(display, 70);

        Assert.Equal(2, delays.Count);
        Assert.False(delays.Dequeue().TrySetResult());
        delays.Dequeue().SetResult();

        Assert.Equal(70, await written.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(true, false, MicPillKind.Live, "Live")]
    [InlineData(true, true, MicPillKind.Muted, "Muted")]
    [InlineData(false, true, MicPillKind.Muted, "Muted")]
    [InlineData(false, false, MicPillKind.Idle, "Mic")]
    public void MicPillStateMapsActivityAndMute(bool isActive, bool isMuted, MicPillKind kind, string label)
    {
        var state = MicPillState.From(isActive, isMuted);

        Assert.Equal(kind, state.Kind);
        Assert.Equal(label, state.Label);
    }
}
