using System.Windows.Media.Animation;

namespace Winotch;

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

    public static IEasingFunction CreateEasing() => new QuarticEase { EasingMode = EasingMode.EaseOut };
}
