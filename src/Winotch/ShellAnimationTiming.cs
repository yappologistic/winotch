using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media.Animation;

namespace Winotch;

/// <summary>
/// Central timing and easing tokens for shell motion. Keeping these values in
/// one place makes compositor and XAML animations finish on the same frame.
/// </summary>
public static class ShellAnimationTiming
{
    public const int MotionMilliseconds = 360;
    public const int FadeMilliseconds = 180;
    public const int DetailRevealDelayMilliseconds = 55;
    public const int CollapseGuardMilliseconds = 650;
    public const int MediaToastMilliseconds = 3800;
    public const int ForegroundPollMilliseconds = 200;
    public const int ChargingFillMilliseconds = 520;
    public const int ChargingTintSweepMilliseconds = 440;

    public static TimeSpan MotionDuration => TimeSpan.FromMilliseconds(MotionMilliseconds);
    public static TimeSpan FadeDuration => TimeSpan.FromMilliseconds(FadeMilliseconds);
    public static TimeSpan DetailRevealDelay => TimeSpan.FromMilliseconds(DetailRevealDelayMilliseconds);
    public static TimeSpan DetailRevealCompletionDelay => MotionDuration - DetailRevealDelay;
    public static TimeSpan CollapseGuard => TimeSpan.FromMilliseconds(CollapseGuardMilliseconds);
    public static TimeSpan MediaToastDuration => TimeSpan.FromMilliseconds(MediaToastMilliseconds);
    public static TimeSpan ForegroundPollInterval => TimeSpan.FromMilliseconds(ForegroundPollMilliseconds);
    public static TimeSpan ChargingFillDuration => TimeSpan.FromMilliseconds(ChargingFillMilliseconds);
    public static TimeSpan ChargingTintSweepDuration => TimeSpan.FromMilliseconds(ChargingTintSweepMilliseconds);

    // Retained for WinUI storyboard call sites such as the charging flourish.
    public static EasingFunctionBase CreateEasing() => new QuarticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Fluent's fast-out/slow-in curve for compositor-driven shell motion.
    /// </summary>
    public static CompositionEasingFunction CreateCompositionEasing(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1));

    /// <summary>
    /// Evaluates the same cubic Bezier used by <see cref="CreateCompositionEasing"/>.
    /// This lets an interrupted transition restart from its visible in-flight
    /// value rather than from its previous logical endpoint.
    /// </summary>
    internal static double EaseCompositionProgress(double progress)
    {
        var x = Math.Clamp(progress, 0, 1);
        if (x is 0 or 1)
        {
            return x;
        }

        // Invert x(t) with a bounded binary search. A fixed iteration count is
        // deterministic and more robust than Newton iteration near the ends.
        var low = 0d;
        var high = 1d;
        for (var index = 0; index < 14; index++)
        {
            var parameter = (low + high) / 2;
            if (Cubic(parameter, 0.1, 0.2) < x)
            {
                low = parameter;
            }
            else
            {
                high = parameter;
            }
        }

        return Cubic((low + high) / 2, 0.9, 1);
    }

    private static double Cubic(double progress, double firstControl, double secondControl)
    {
        var inverse = 1 - progress;
        return (3 * inverse * inverse * progress * firstControl) +
               (3 * inverse * progress * progress * secondControl) +
               (progress * progress * progress);
    }
}
