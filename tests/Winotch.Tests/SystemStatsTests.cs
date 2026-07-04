namespace Winotch.Tests;

public sealed class SystemStatsTests
{
    private const double Kilobyte = 1024;
    private const double Megabyte = Kilobyte * 1024;
    private const double Gigabyte = Megabyte * 1024;

    [Fact]
    public void SampleBufferKeepsOldestToNewestWindow()
    {
        var buffer = new FixedSampleBuffer(3);

        buffer.Push(1);
        buffer.Push(2);
        Assert.Equal(new double[] { 1, 2 }, buffer.Snapshot());

        buffer.Push(3);
        buffer.Push(4);

        Assert.Equal(new double[] { 2, 3, 4 }, buffer.Snapshot());
    }

    [Fact]
    public void NetworkRatesUseDeltasAndIgnoreMissingOrNewAdapters()
    {
        var previous = new[]
        {
            new NetworkCounterSnapshot("wifi", 1_000, 2_000),
            new NetworkCounterSnapshot("ethernet", 100, 100)
        };
        var current = new[]
        {
            new NetworkCounterSnapshot("wifi", 2_024, 3_024),
            new NetworkCounterSnapshot("cellular", 9_000, 9_000)
        };

        var rates = NetworkRateCalculator.FromSnapshots(previous, current, TimeSpan.FromSeconds(2));

        Assert.Equal(512, rates.DownBytesPerSecond);
        Assert.Equal(512, rates.UpBytesPerSecond);
    }

    [Fact]
    public void NetworkRatesTreatCounterResetAsZeroDelta()
    {
        var previous = new[] { new NetworkCounterSnapshot("wifi", 10_000, 5_000) };
        var current = new[] { new NetworkCounterSnapshot("wifi", 9_000, 4_500) };

        var rates = NetworkRateCalculator.FromSnapshots(previous, current, TimeSpan.FromSeconds(1));

        Assert.Equal(0, rates.DownBytesPerSecond);
        Assert.Equal(0, rates.UpBytesPerSecond);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(42, "42 B/s")]
    [InlineData(1023, "1023 B/s")]
    [InlineData(1024, "1 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(1048576, "1 MB/s")]
    [InlineData(3355443.2, "3.2 MB/s")]
    public void RateFormatterUsesReadableBinaryThresholds(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, SystemStatsFormatter.FormatRate(bytesPerSecond));
    }

    [Fact]
    public void NetworkFormatterLabelsDirection()
    {
        var rates = new NetworkRates(3.2 * Megabyte, 240 * Kilobyte);

        Assert.Equal("3.2 MB/s down \u00B7 240 KB/s up", SystemStatsFormatter.FormatNetwork(rates));
    }

    [Fact]
    public void RamFormatterRoundsUsedToOneDecimalAndTotalToOneDecimalMax()
    {
        Assert.Equal("11.2 / 16 GB", SystemStatsFormatter.FormatRam(Gib(11.24), Gib(16)));
        Assert.Equal("16.0 / 16 GB", SystemStatsFormatter.FormatRam(Gib(15.97), Gib(16)));
    }

    [Fact]
    public void SparklinePartialWindowMapsLeftToRight()
    {
        var points = SparklinePointMapper.Map([0, 5, 10], capacity: 5, width: 41, height: 20);

        Assert.Collection(
            points,
            first =>
            {
                Assert.Equal(0, first.X);
                Assert.Equal(19, first.Y);
            },
            second =>
            {
                Assert.Equal(10, second.X);
                Assert.Equal(10, second.Y, precision: 1);
            },
            third =>
            {
                Assert.Equal(20, third.X);
                Assert.Equal(1, third.Y);
            });
    }

    [Fact]
    public void SparklineFlatZeroSeriesStaysVisibleAtBaseline()
    {
        var points = SparklinePointMapper.Map([0, 0], capacity: 5, width: 41, height: 20);

        Assert.All(points, point => Assert.Equal(19, point.Y));
    }

    [Fact]
    public void SparklineScalesHugeSpikesWithinBounds()
    {
        var points = SparklinePointMapper.Map([1, 1_000_000], capacity: 5, width: 41, height: 20);

        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 0, 40);
            Assert.InRange(point.Y, 1, 19);
        });
        Assert.Equal(1, points[^1].Y);
    }

    private static ulong Gib(double value) => (ulong)Math.Round(value * Gigabyte);
}
