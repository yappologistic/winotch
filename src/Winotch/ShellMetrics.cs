namespace Winotch;

public readonly record struct ShellGeometry(double Width, double ShellHeight, double WindowHeight, double Left);

public static class ShellMetrics
{
    public const double MiniWidth = 220;
    public const double MiniShellHeight = 36;
    public const double MiniWindowHeight = 42;
    public const double FullBarShellHeight = 32;
    public const double FullBarWindowHeight = 34;
    public const double ExpandedWidth = 840;
    public const double ExpandedShellHeight = 246;
    public const double ExpandedWindowHeight = 300;

    public static double CenterLeft(double screenWidth, double width) => (screenWidth - width) / 2;

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
}
