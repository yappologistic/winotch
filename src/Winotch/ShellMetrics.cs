namespace Winotch;

public readonly record struct ShellGeometry(
    double Width,
    double ShellHeight,
    double WindowHeight,
    double Left,
    double Top = 0);

public static class ShellMetrics
{
    public const double MiniWidth = 244;
    public const double MiniShellHeight = 44;
    public const double MiniWindowHeight = 52;
    public const double FullBarShellHeight = 32;
    public const double FullBarWindowHeight = 34;
    public const double LiveStripWidth = 520;
    public const double LiveStripShellHeight = 58;
    public const double LiveStripWindowHeight = 66;
    public const double MediaToastWidth = 520;
    public const double MediaToastShellHeight = 68;
    public const double MediaToastWindowHeight = 76;
    public const double ExpandedWidth = 840;
    public const double ExpandedShellHeight = 520;
    public const double ExpandedWindowHeight = 580;
    public const double CommandWidth = 720;
    public const double CommandShellHeight = 300;
    public const double CommandWindowHeight = 316;

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

    public static ShellGeometry Command(double screenWidth)
    {
        var width = FitWidth(CommandWidth, screenWidth);
        return new ShellGeometry(width, CommandShellHeight, CommandWindowHeight, CenterLeft(screenWidth, width));
    }

    public static ShellGeometry PlaceOnMonitor(ShellGeometry geometry, MonitorSnapshot monitor)
    {
        var left = monitor.LeftDip + geometry.Left;
        var minLeft = monitor.LeftDip;
        var maxLeft = monitor.LeftDip + Math.Max(0, monitor.WidthDip - geometry.Width);
        return geometry with { Left = Math.Clamp(left, minLeft, maxLeft), Top = monitor.TopDip };
    }

    private static double FitWidth(double width, double screenWidth) =>
        Math.Max(0, Math.Min(width, screenWidth));
}
