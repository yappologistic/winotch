using System.Windows.Media;

namespace Winotch;

public sealed record BatteryVisual(double FillWidth, System.Windows.Media.Brush Brush)
{
    public const double IconFillWidth = 16;

    public static BatteryVisual FromPercent(int percent, bool isCharging = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var color = isCharging
            ? System.Windows.Media.Color.FromRgb(50, 215, 75)
            : clamped < 20
            ? System.Windows.Media.Color.FromRgb(255, 69, 58)
            : clamped < 50
                ? System.Windows.Media.Color.FromRgb(255, 204, 0)
                : System.Windows.Media.Color.FromRgb(246, 246, 244);

        return new BatteryVisual(FillWidthForPercent(clamped), new SolidColorBrush(color));
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
