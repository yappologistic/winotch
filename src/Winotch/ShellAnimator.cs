using System.Windows;
using System.Windows.Media.Animation;

namespace Winotch;

public static class ShellAnimator
{
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };

    public static void Animate(UIElement target, DependencyProperty property, double value, int frameRate)
    {
        var from = target.GetValue(property) is double current && !double.IsNaN(current) ? current : value;
        target.SetValue(property, value);
        var animation = new DoubleAnimation(from, value, MotionDuration)
        {
            EasingFunction = Easing
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Hide(UIElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 0;
        target.Visibility = Visibility.Collapsed;
    }

    public static void Show(UIElement target, int frameRate)
    {
        target.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation(1, FadeDuration)
        {
            EasingFunction = Easing
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        target.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateShell(Window window, FrameworkElement shell, ShellGeometry geometry, int frameRate)
    {
        Animate(window, Window.WidthProperty, geometry.Width, frameRate);
        Animate(window, Window.HeightProperty, geometry.WindowHeight, frameRate);
        Animate(window, Window.LeftProperty, geometry.Left, frameRate);
        Animate(window, Window.TopProperty, geometry.Top, frameRate);
        Animate(shell, FrameworkElement.WidthProperty, geometry.Width, frameRate);
        Animate(shell, FrameworkElement.HeightProperty, geometry.ShellHeight, frameRate);
    }

    public static void Clear(Window window, FrameworkElement shell, FrameworkElement detail)
    {
        var current = new ShellGeometry(
            Current(window.Width, window.ActualWidth),
            Current(shell.Height, shell.ActualHeight),
            Current(window.Height, window.ActualHeight),
            window.Left,
            window.Top);
        window.BeginAnimation(Window.WidthProperty, null);
        window.BeginAnimation(Window.HeightProperty, null);
        window.BeginAnimation(Window.LeftProperty, null);
        window.BeginAnimation(Window.TopProperty, null);
        shell.BeginAnimation(FrameworkElement.WidthProperty, null);
        shell.BeginAnimation(FrameworkElement.HeightProperty, null);
        detail.BeginAnimation(UIElement.OpacityProperty, null);
        ApplyShellGeometry(window, shell, current);
    }

    private static double Current(double propertyValue, double actualValue) =>
        actualValue > 0 ? actualValue : propertyValue;

    private static void ApplyShellGeometry(Window window, FrameworkElement shell, ShellGeometry geometry)
    {
        window.Top = 0;
        window.Width = geometry.Width;
        window.Height = geometry.WindowHeight;
        window.Left = geometry.Left;
        window.Top = geometry.Top;
        shell.Width = geometry.Width;
        shell.Height = geometry.ShellHeight;
    }
}
