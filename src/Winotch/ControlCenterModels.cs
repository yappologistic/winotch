namespace Winotch;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

public static class AudioDeviceOrdering
{
    public static IReadOnlyList<AudioOutputDevice> DefaultFirst(IEnumerable<AudioOutputDevice> devices) =>
        devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class AudioSessionNaming
{
    public static string Resolve(
        string? fileDescription,
        string? productName,
        string? sessionDisplayName,
        string? processName,
        bool isSystemSounds) =>
        FirstText(fileDescription, productName, sessionDisplayName, processName)
        ?? (isSystemSounds ? "System sounds" : "Audio session");

    private static string? FirstText(params string?[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public enum BrightnessDisplayKind
{
    Internal,
    External
}

public sealed record BrightnessDisplay(
    string Id,
    string Name,
    int Percent,
    BrightnessDisplayKind Kind,
    int Minimum = 0,
    int Maximum = 100);

public static class BrightnessMath
{
    public static int ToPercent(int minimum, int current, int maximum)
    {
        if (maximum <= minimum)
        {
            return Math.Clamp(current, 0, 100);
        }

        var percent = (double)(current - minimum) / (maximum - minimum) * 100;
        return Math.Clamp((int)Math.Round(percent), 0, 100);
    }

    public static int FromPercent(int minimum, int maximum, int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        if (maximum <= minimum)
        {
            return clamped;
        }

        var value = minimum + (double)clamped / 100 * (maximum - minimum);
        return Math.Clamp((int)Math.Round(value), minimum, maximum);
    }
}

public sealed class DebouncedBrightnessWriter : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<BrightnessDisplay, int, CancellationToken, Task> _writeAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Dictionary<string, CancellationTokenSource> _pending = [];
    private readonly object _sync = new();
    private bool _disposed;

    public DebouncedBrightnessWriter(
        TimeSpan delay,
        Func<BrightnessDisplay, int, CancellationToken, Task> writeAsync,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _delay = delay;
        _writeAsync = writeAsync;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public void Queue(BrightnessDisplay display, int percent)
    {
        var source = new CancellationTokenSource();
        lock (_sync)
        {
            if (_disposed)
            {
                source.Dispose();
                return;
            }

            if (_pending.Remove(display.Id, out var previous))
            {
                previous.Cancel();
            }

            _pending[display.Id] = source;
        }

        _ = RunAsync(display, Math.Clamp(percent, 0, 100), source);
    }

    private async Task RunAsync(BrightnessDisplay display, int percent, CancellationTokenSource source)
    {
        try
        {
            await _delayAsync(_delay, source.Token);
            await _writeAsync(display, percent, source.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            lock (_sync)
            {
                if (_pending.TryGetValue(display.Id, out var current) && ReferenceEquals(current, source))
                {
                    _pending.Remove(display.Id);
                }
            }

            source.Dispose();
        }
    }

    public void Dispose()
    {
        List<CancellationTokenSource> pending;
        lock (_sync)
        {
            _disposed = true;
            pending = _pending.Values.ToList();
            _pending.Clear();
        }

        foreach (var source in pending)
        {
            source.Cancel();
            source.Dispose();
        }
    }
}

public enum MicPillKind
{
    Idle,
    Live,
    Muted
}

public sealed record MicPillState(MicPillKind Kind, string Glyph, string Label)
{
    public static MicPillState From(bool isActive, bool isMuted)
    {
        if (isMuted)
        {
            return new MicPillState(MicPillKind.Muted, "\uE720", "Muted");
        }

        return isActive
            ? new MicPillState(MicPillKind.Live, "\uE720", "Live")
            : new MicPillState(MicPillKind.Idle, "\uE720", "Mic");
    }
}
