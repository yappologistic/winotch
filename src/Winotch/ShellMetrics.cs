namespace Winotch;

public readonly record struct ShellGeometry(
    double Width,
    double ShellHeight,
    double WindowHeight,
    double Left,
    double Top = 0,
    double DpiScale = 0);

public static class ShellMetrics
{
    public const double MiniWidth = 260;
    public const double MiniShellHeight = 68;
    public const double MiniWindowHeight = 68;
    public const double FullBarShellHeight = 32;
    public const double FullBarWindowHeight = 32;
    public const double LiveStripWidth = 440;
    public const double LiveStripShellHeight = 76;
    public const double LiveStripWindowHeight = 76;
    public const double MediaToastWidth = 440;
    public const double MediaToastShellHeight = 76;
    public const double MediaToastWindowHeight = 76;
    public const double ExpandedWidth = 960;
    public const double ExpandedShellHeight = 520;
    public const double ExpandedWindowHeight = 520;
    public const double CommandWidth = 600;
    public const double CommandMinimumHeight = 120;
    public const double CommandMaximumHeight = 344;
    public const double CommandResultHeight = 56;

    public static double CenterLeft(double screenWidth, double width) =>
        Math.Max(0, (screenWidth - width) / 2);

    public static double ToDeviceIndependentWidth(double physicalScreenWidth, double dpiScale) =>
        physicalScreenWidth / (dpiScale > 0 ? dpiScale : 1);

    public static ShellGeometry ForMode(bool isFullBar, double screenWidth)
    {
        var width = isFullBar ? Math.Max(0, screenWidth) : FitWidth(MiniWidth, screenWidth);
        return new ShellGeometry(
            width,
            isFullBar ? FullBarShellHeight : MiniShellHeight,
            isFullBar ? FullBarWindowHeight : MiniWindowHeight,
            isFullBar ? 0 : CenterLeft(screenWidth, width));
    }

    public static ShellGeometry Expanded(double screenWidth)
    {
        var width = FitWidth(ExpandedWidth, screenWidth);
        return new ShellGeometry(width, ExpandedShellHeight, ExpandedWindowHeight, CenterLeft(screenWidth, width));
    }

    public static ShellGeometry MediaToast(double screenWidth)
    {
        var width = FitWidth(MediaToastWidth, screenWidth);
        return new ShellGeometry(width, MediaToastShellHeight, MediaToastWindowHeight, CenterLeft(screenWidth, width));
    }

    public static ShellGeometry LiveStrip(double screenWidth)
    {
        var width = FitWidth(LiveStripWidth, screenWidth);
        return new ShellGeometry(width, LiveStripShellHeight, LiveStripWindowHeight, CenterLeft(screenWidth, width));
    }

    public static ShellGeometry Command(double screenWidth, int resultCount = 0)
    {
        var width = FitWidth(CommandWidth, screenWidth);
        var visibleResults = Math.Clamp(resultCount, 0, 4);
        var height = Math.Min(CommandMaximumHeight, CommandMinimumHeight + (visibleResults * CommandResultHeight));
        return new ShellGeometry(width, height, height, CenterLeft(screenWidth, width));
    }

    public static ShellGeometry PlaceOnMonitor(ShellGeometry geometry, MonitorSnapshot monitor)
    {
        var left = monitor.LeftDip + geometry.Left;
        var minLeft = monitor.LeftDip;
        var maxLeft = monitor.LeftDip + Math.Max(0, monitor.WidthDip - geometry.Width);
        return geometry with
        {
            Left = Math.Clamp(left, minLeft, maxLeft),
            Top = monitor.TopDip,
            DpiScale = monitor.DpiScaleX
        };
    }

    private static double FitWidth(double width, double screenWidth) =>
        Math.Max(0, Math.Min(width, screenWidth));
}
