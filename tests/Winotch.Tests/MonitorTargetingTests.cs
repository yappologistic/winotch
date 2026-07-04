using System.Drawing;

namespace Winotch.Tests;

public class MonitorTargetingTests
{
    [Fact]
    public void SelectMonitorUsesForegroundWindowMonitor()
    {
        var monitors = TwoHorizontalMonitors();
        var request = new MonitorTargetRequest(
            new NativeRect(2200, 120, 3000, 900),
            UseCursorMonitor: false,
            new Point(200, 200),
            LastMonitorDeviceName: monitors[0].DeviceName);

        var selected = MonitorTargeting.SelectMonitor(monitors, request);

        Assert.Equal(monitors[1], selected);
    }

    [Fact]
    public void SelectMonitorUsesCursorForShellForeground()
    {
        var monitors = TwoHorizontalMonitors();
        var request = new MonitorTargetRequest(
            ForegroundRect: null,
            UseCursorMonitor: true,
            new Point(2500, 400),
            LastMonitorDeviceName: monitors[0].DeviceName);

        var selected = MonitorTargeting.SelectMonitor(monitors, request);

        Assert.Equal(monitors[1], selected);
    }

    [Fact]
    public void SelectMonitorFallsBackToLastMonitorWhenCursorIsOutsideScreens()
    {
        var monitors = TwoHorizontalMonitors();
        var request = new MonitorTargetRequest(
            ForegroundRect: null,
            UseCursorMonitor: true,
            new Point(-200, -200),
            LastMonitorDeviceName: monitors[1].DeviceName);

        var selected = MonitorTargeting.SelectMonitor(monitors, request);

        Assert.Equal(monitors[1], selected);
    }

    [Fact]
    public void SelectMonitorFallsBackToPrimaryWhenLastMonitorWasUnplugged()
    {
        var primary = PrimaryMonitor();
        var request = new MonitorTargetRequest(
            ForegroundRect: null,
            UseCursorMonitor: false,
            new Point(2500, 400),
            LastMonitorDeviceName: "secondary");

        var selected = MonitorTargeting.SelectMonitor([primary], request);

        Assert.Equal(primary, selected);
    }

    [Fact]
    public void SelectMonitorChoosesLargestOverlapForSpanningWindow()
    {
        var monitors = new[]
        {
            PrimaryMonitor(),
            new MonitorSnapshot(
                "secondary",
                new NativeRect(2500, 0, 4500, 1080),
                new NativeRect(2500, 0, 4500, 1040),
                IsPrimary: false,
                DpiScaleX: 1,
                DpiScaleY: 1)
        };
        var request = new MonitorTargetRequest(
            new NativeRect(1800, 100, 2700, 900),
            UseCursorMonitor: false,
            new Point(200, 200),
            LastMonitorDeviceName: monitors[0].DeviceName);

        var selected = MonitorTargeting.SelectMonitor(monitors, request);

        Assert.Equal(monitors[1], selected);
    }

    [Fact]
    public void MonitorDipBoundsUseTargetMonitorDpi()
    {
        var monitor = new MonitorSnapshot(
            "secondary",
            new NativeRect(1280, 0, 2560, 960),
            new NativeRect(1280, 0, 2560, 933),
            IsPrimary: false,
            DpiScaleX: 1.5,
            DpiScaleY: 1.5);

        var geometry = ShellMetrics.ForMode(isFullBar: false, monitor.WidthDip) with
        {
            Left = monitor.LeftDip + ShellMetrics.CenterLeft(monitor.WidthDip, ShellMetrics.MiniWidth),
            Top = monitor.TopDip
        };

        Assert.Equal(1280, monitor.LeftDip);
        Assert.Equal(1280, monitor.WidthDip);
        Assert.Equal(2560, monitor.WorkAreaRightDip);
        Assert.Equal(933, monitor.WorkAreaBottomDip);
        Assert.Equal(1.5, monitor.DpiScaleY);
        Assert.Equal(1798, geometry.Left);
        Assert.Equal(0, geometry.Top);
    }

    [Fact]
    public void ForegroundPollIntervalStaysResponsiveForMonitorFollowing()
    {
        Assert.InRange(ShellAnimationTiming.ForegroundPollMilliseconds, 100, 250);
    }

    private static MonitorSnapshot[] TwoHorizontalMonitors() =>
    [
        PrimaryMonitor(),
        new(
            "secondary",
            new NativeRect(1920, 0, 3840, 1080),
            new NativeRect(1920, 0, 3840, 1040),
            IsPrimary: false,
            DpiScaleX: 1,
            DpiScaleY: 1)
    ];

    private static MonitorSnapshot PrimaryMonitor() => new(
        "primary",
        new NativeRect(0, 0, 1920, 1080),
        new NativeRect(0, 0, 1920, 1040),
        IsPrimary: true,
        DpiScaleX: 1,
        DpiScaleY: 1);
}
