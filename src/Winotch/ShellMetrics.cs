namespace Winotch;

public readonly record struct ShellGeometry(double Width, double ShellHeight, double WindowHeight, double Left);

public static class ShellMetrics
{
    public const double MiniWidth = 244;
    public const double MiniShellHeight = 44;
    public const double MiniWindowHeight = 52;
    public const double FullBarShellHeight = 32;
    public const double FullBarWindowHeight = 34;
    public const double MediaToastWidth = 520;
    public const double MediaToastShellHeight = 68;
    public const double MediaToastWindowHeight = 76;
    public const double ExpandedWidth = 840;
    public const double ExpandedShellHeight = 430;
    public const double ExpandedWindowHeight = 484;

    public static double CenterLeft(double screenWidth, double width) => (screenWidth - width) / 2;

    public static double ToDeviceIndependentWidth(double physicalScreenWidth, double dpiScale) =>
        physicalScreenWidth / (dpiScale > 0 ? dpiScale : 1);

    public static ShellGeometry ForMode(bool isFullBar, double screenWidth)
    {
        var width = isFullBar ? screenWidth : MiniWidth;
        return new ShellGeometry(
            width,
            isFullBar ? FullBarShellHeight : MiniShellHeight,
            isFullBar ? FullBarWindowHeight : MiniWindowHeight,
            isFullBar ? 0 : CenterLeft(screenWidth, width));
    }

    public static ShellGeometry Expanded(double screenWidth) => new(
        ExpandedWidth,
        ExpandedShellHeight,
        ExpandedWindowHeight,
        CenterLeft(screenWidth, ExpandedWidth));

    public static ShellGeometry MediaToast(double screenWidth) => new(
        MediaToastWidth,
        MediaToastShellHeight,
        MediaToastWindowHeight,
        CenterLeft(screenWidth, MediaToastWidth));
}
