using System.Windows;
using System.Windows.Media.Animation;

namespace Winotch;

public static class ShellAnimator
{
    private static readonly Duration MotionDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly IEasingFunction Easing = new QuinticEase { EasingMode = EasingMode.EaseOut };

    public static void Animate(UIElement target, DependencyProperty property, double value, int frameRate)
    {
        var animation = new DoubleAnimation(value, MotionDuration)
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
        window.Top = 0;
        Animate(window, Window.WidthProperty, geometry.Width, frameRate);
        Animate(window, Window.HeightProperty, geometry.WindowHeight, frameRate);
        Animate(window, Window.LeftProperty, geometry.Left, frameRate);
        Animate(shell, FrameworkElement.WidthProperty, geometry.Width, frameRate);
        Animate(shell, FrameworkElement.HeightProperty, geometry.ShellHeight, frameRate);
    }

    public static void Clear(Window window, FrameworkElement shell, FrameworkElement detail)
    {
        window.BeginAnimation(Window.WidthProperty, null);
        window.BeginAnimation(Window.HeightProperty, null);
        window.BeginAnimation(Window.LeftProperty, null);
        shell.BeginAnimation(FrameworkElement.WidthProperty, null);
        shell.BeginAnimation(FrameworkElement.HeightProperty, null);
        detail.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
