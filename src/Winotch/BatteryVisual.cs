using WinUIBrush = Microsoft.UI.Xaml.Media.Brush;
using WinUIColor = Windows.UI.Color;
using WinUISolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace Winotch;

public sealed record BatteryVisual(double FillWidth, WinUIBrush Brush)
{
    public const double IconFillWidth = 16;

    public static BatteryVisual FromPercent(int percent, bool isCharging = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var color = isCharging
            ? WinUIColor.FromArgb(255, 50, 215, 75)
            : clamped < 20
            ? WinUIColor.FromArgb(255, 255, 69, 58)
            : clamped < 50
                ? WinUIColor.FromArgb(255, 255, 204, 0)
                : WinUIColor.FromArgb(255, 246, 246, 244);

        return new BatteryVisual(FillWidthForPercent(clamped), new WinUISolidColorBrush(color));
    }

    public static double FillWidthForPercent(int percent, double maxFillWidth = IconFillWidth)
    {
        var width = double.IsFinite(maxFillWidth) && maxFillWidth > 0 ? maxFillWidth : 0;
        return width * Math.Clamp(percent, 0, 100) / 100;
    }

    public static BatteryFillAnimation ChargingFillAnimation(
        int percent,
        int? previousPercent = null,
        double maxFillWidth = IconFillWidth) =>
        new(
            previousPercent is null ? 0 : FillWidthForPercent(previousPercent.Value, maxFillWidth),
            FillWidthForPercent(percent, maxFillWidth),
            ShellAnimationTiming.ChargingFillDuration,
            ShellAnimationTiming.ChargingTintSweepDuration);
}

public sealed record BatteryFillAnimation(
    double FromWidth,
    double ToWidth,
    TimeSpan Duration,
    TimeSpan SweepDuration);
