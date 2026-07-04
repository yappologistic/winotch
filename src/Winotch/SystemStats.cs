namespace Winotch;

public sealed class FixedSampleBuffer
{
    private readonly double[] _values;
    private int _next;
    private int _count;

    public FixedSampleBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _values = new double[capacity];
    }

    public void Push(double value)
    {
        _values[_next] = double.IsFinite(value) ? value : 0;
        _next = (_next + 1) % _values.Length;
        _count = Math.Min(_count + 1, _values.Length);
    }

    public IReadOnlyList<double> Snapshot()
    {
        var snapshot = new double[_count];
        var start = _count == _values.Length ? _next : 0;
        for (var index = 0; index < snapshot.Length; index++)
        {
            snapshot[index] = _values[(start + index) % _values.Length];
        }

        return snapshot;
    }

    public void Clear()
    {
        Array.Clear(_values);
        _next = 0;
        _count = 0;
    }
}

public sealed record SparklinePoint(double X, double Y);

public static class SparklinePointMapper
{
    public static IReadOnlyList<SparklinePoint> Map(
        IReadOnlyList<double> values,
        int capacity,
        double width,
        double height)
    {
        if (values.Count == 0 || capacity <= 1 || width <= 0 || height <= 0)
        {
            return [];
        }

        var count = Math.Min(values.Count, capacity);
        var start = values.Count - count;
        var sanitized = new double[count];
        for (var index = 0; index < count; index++)
        {
            var value = values[start + index];
            sanitized[index] = double.IsFinite(value) ? Math.Max(0, value) : 0;
        }

        var max = sanitized.Max();
        var leftToRightStep = Math.Max(0, width - 1) / (capacity - 1);
        var top = height > 2 ? 1 : 0;
        var bottom = height > 2 ? height - 1 : height;
        var verticalRange = bottom - top;
        var points = new SparklinePoint[count];
        for (var index = 0; index < count; index++)
        {
            var normalized = max > 0 ? sanitized[index] / max : 0;
            var x = index * leftToRightStep;
            var y = bottom - normalized * verticalRange;
            points[index] = new SparklinePoint(
                Math.Clamp(x, 0, Math.Max(0, width - 1)),
                Math.Clamp(y, top, bottom));
        }

        return points;
    }
}

public sealed record NetworkCounterSnapshot(string Id, long BytesReceived, long BytesSent);

public sealed record NetworkRates(double DownBytesPerSecond, double UpBytesPerSecond);

public static class NetworkRateCalculator
{
    public static NetworkRates FromSnapshots(
        IEnumerable<NetworkCounterSnapshot> previous,
        IEnumerable<NetworkCounterSnapshot> current,
        TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0)
        {
            return new NetworkRates(0, 0);
        }

        var previousById = previous.ToDictionary(snapshot => snapshot.Id, StringComparer.Ordinal);
        double downBytes = 0;
        double upBytes = 0;
        foreach (var snapshot in current)
        {
            if (!previousById.TryGetValue(snapshot.Id, out var old))
            {
                continue;
            }

            downBytes += PositiveDelta(snapshot.BytesReceived, old.BytesReceived);
            upBytes += PositiveDelta(snapshot.BytesSent, old.BytesSent);
        }

        return new NetworkRates(downBytes / elapsed.TotalSeconds, upBytes / elapsed.TotalSeconds);
    }

    private static long PositiveDelta(long current, long previous) => current >= previous ? current - previous : 0;
}

public sealed record SystemStatRowSnapshot(string ValueText, IReadOnlyList<double> Samples);

public sealed record SystemStatsSnapshot(
    SystemStatRowSnapshot? Cpu,
    SystemStatRowSnapshot? Ram,
    SystemStatRowSnapshot? Network)
{
    public bool HasRows => Cpu is not null || Ram is not null || Network is not null;
}

public static class SystemStatsFormatter
{
    private const double BytesPerKilobyte = 1024;
    private const double BytesPerMegabyte = BytesPerKilobyte * 1024;
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024;

    public static string FormatCpu(double percent) =>
        FormattableString.Invariant($"{Math.Clamp(percent, 0, 100):0}%");

    public static string FormatRam(ulong usedBytes, ulong totalBytes) =>
        FormattableString.Invariant($"{usedBytes / BytesPerGigabyte:0.0} / {totalBytes / BytesPerGigabyte:0.#} GB");

    public static string FormatNetwork(NetworkRates rates) =>
        $"{FormatRate(rates.DownBytesPerSecond)} down \u00B7 {FormatRate(rates.UpBytesPerSecond)} up";

    public static string FormatRate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        if (value < BytesPerKilobyte)
        {
            return FormattableString.Invariant($"{Math.Round(value):0} B/s");
        }

        if (value < BytesPerMegabyte)
        {
            return FormattableString.Invariant($"{value / BytesPerKilobyte:0.#} KB/s");
        }

        return FormattableString.Invariant($"{value / BytesPerMegabyte:0.#} MB/s");
    }
}
