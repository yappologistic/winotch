using System.Windows.Media;

namespace Winotch.Tests;

public class StatusParsingTests
{
    [Fact]
    public void NetshParserReadsConnectedWifiAndSignal()
    {
        var output = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                State                  : connected
                SSID                   : TELUS1255
                BSSID                  : 00:11:22:33:44:55
                Signal                 : 99%
            """;

        var status = WifiService.ParseCurrentNetsh(output);

        Assert.Equal("TELUS1255", status.Name);
        Assert.Equal("99%", status.Signal);
    }

    [Fact]
    public void ProfileParserTrimsWindowsProfileIndex()
    {
        var status = WifiService.ParseCurrentProfile("TELUS1255 2\r\n");

        Assert.Equal("TELUS1255", status);
    }

    [Fact]
    public void NetworkParserDeduplicatesScannedNetworks()
    {
        var output = """
            SSID 1 : TELUS1255
                Network type            : Infrastructure
                Signal                  : 96%
            SSID 2 : TELUS1255
                Network type            : Infrastructure
                Signal                  : 75%
            SSID 3 : Guest
                Network type            : Infrastructure
                Signal                  : 41%
            """;

        var networks = WifiService.ParseNetworks(output);

        Assert.Collection(
            networks,
            first => Assert.Equal("TELUS1255", first.Name),
            second => Assert.Equal("Guest", second.Name));
    }

    [Theory]
    [InlineData(96, false, 15.36, 246, 246, 244)]
    [InlineData(96, true, 15.36, 50, 215, 75)]
    [InlineData(49, false, 7.84, 255, 204, 0)]
    [InlineData(19, false, 3.04, 255, 69, 58)]
    public void BatteryVisualUsesFillAndThresholdColors(int percent, bool isCharging, double expectedWidth, byte red, byte green, byte blue)
    {
        var visual = BatteryVisual.FromPercent(percent, isCharging);
        var brush = Assert.IsType<SolidColorBrush>(visual.Brush);

        Assert.Equal(expectedWidth, visual.FillWidth, precision: 2);
        Assert.Equal(Color.FromRgb(red, green, blue), brush.Color);
    }

    [Fact]
    public void ForegroundModeUsesMiniForDesktopAndOwnWindow()
    {
        var monitor = new NativeRect(0, 0, 1920, 1080);
        var fullscreen = new NativeRect(0, 0, 1920, 1040);

        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: true, isShell: false, isMaximized: true, fullscreen, monitor, monitor));
        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: true, isMaximized: true, fullscreen, monitor, monitor));
    }

    [Fact]
    public void ForegroundModeUsesFullBarForMaximizedOrScreenFillingApp()
    {
        var monitor = new NativeRect(0, 0, 1920, 1080);
        var normal = new NativeRect(300, 160, 1200, 760);
        var filling = new NativeRect(0, 0, 1920, 1040);
        var reservedWorkArea = new NativeRect(0, 48, 1920, 1080);
        var fillingReservedWorkArea = new NativeRect(0, 48, 1920, 1080);

        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, normal, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: true, normal, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, filling, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, fillingReservedWorkArea, monitor, reservedWorkArea));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void NotificationSilenceUsesGlobalToastToggle(int? enabled, bool expected)
    {
        Assert.Equal(expected, NotificationSilenceService.IsGloballySilenced(enabled));
    }

    [Fact]
    public void NotificationChangeTrackerPopsOnlyForNewNotifications()
    {
        var tracker = new NotificationChangeTracker();
        var first = new[] { new NotificationItem("Mail", "One", "Body") };
        var second = new[] { new NotificationItem("Mail", "Two", "Body") };

        Assert.False(tracker.ShouldPop(first));
        Assert.False(tracker.ShouldPop(first));
        Assert.True(tracker.ShouldPop(second));
        Assert.False(tracker.ShouldPop(second));
        Assert.False(tracker.ShouldPop([]));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(24, 60)]
    [InlineData(60, 60)]
    [InlineData(120, 120)]
    [InlineData(144, 144)]
    [InlineData(501, 60)]
    public void RefreshRateFallsBackOnlyForInvalidValues(int refreshRate, int expected)
    {
        Assert.Equal(expected, DisplayRefreshRateService.NormalizeRefreshRate(refreshRate));
    }

    [Theory]
    [InlineData(34, 1.0, 34)]
    [InlineData(34, 1.25, 43)]
    [InlineData(34, 1.5, 51)]
    [InlineData(0, 1.0, 1)]
    public void AppBarHeightUsesPhysicalPixels(double dip, double dpiScale, int expected)
    {
        Assert.Equal(expected, AppBarReservationService.ToPhysicalPixels(dip, dpiScale));
    }

    [Fact]
    public void ShellMetricsCentersMiniAndExpandedWidths()
    {
        Assert.Equal(850, ShellMetrics.CenterLeft(1920, ShellMetrics.MiniWidth));
        Assert.Equal(540, ShellMetrics.CenterLeft(1920, ShellMetrics.ExpandedWidth));
        Assert.Equal(new ShellGeometry(1920, 32, 34, 0), ShellMetrics.ForMode(isFullBar: true, screenWidth: 1920));
        Assert.Equal(new ShellGeometry(220, 36, 42, 850), ShellMetrics.ForMode(isFullBar: false, screenWidth: 1920));
    }
}
