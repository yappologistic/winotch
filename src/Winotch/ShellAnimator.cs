using System.Windows;
using System.Windows.Media.Animation;

namespace Winotch;

public static class ShellAnimator
{
    private static readonly DependencyProperty OpacityAnimationGenerationProperty =
        DependencyProperty.RegisterAttached(
            "OpacityAnimationGeneration",
            typeof(int),
            typeof(ShellAnimator),
            new PropertyMetadata(0));

    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);

    public static void Animate(UIElement target, DependencyProperty property, double value, int frameRate)
    {
        var from = target.GetValue(property) is double current && !double.IsNaN(current) ? current : value;
        var animation = new DoubleAnimation(from, value, MotionDuration)
        {
            EasingFunction = ShellAnimationTiming.CreateEasing(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Hide(UIElement target)
    {
        _ = NextOpacityAnimationGeneration(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 0;
        target.Visibility = Visibility.Collapsed;
    }

    public static void Hide(UIElement target, int frameRate)
    {
        var from = CurrentOpacity(target);
        var generation = NextOpacityAnimationGeneration(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = from;
        if (target.Visibility != Visibility.Visible || from <= 0)
        {
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
            return;
        }

        var animation = new DoubleAnimation(from, 0, FadeDuration)
        {
            EasingFunction = ShellAnimationTiming.CreateEasing(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (!IsCurrentOpacityAnimation(target, generation))
            {
                return;
            }

            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        target.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Show(UIElement target, int frameRate)
    {
        var from = target.Visibility == Visibility.Visible ? CurrentOpacity(target) : 0;
        var generation = NextOpacityAnimationGeneration(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = from;
        target.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation(from, 1, FadeDuration)
        {
            EasingFunction = ShellAnimationTiming.CreateEasing(),
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (!IsCurrentOpacityAnimation(target, generation))
            {
                return;
            }

            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 1;
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        target.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static double CurrentOpacity(UIElement target)
    {
        var opacity = target.Opacity;
        if (double.IsNaN(opacity))
        {
            return 0;
        }

        return Math.Clamp(opacity, 0, 1);
    }

    private static int NextOpacityAnimationGeneration(UIElement target)
    {
        var current = (int)target.GetValue(OpacityAnimationGenerationProperty);
        var next = current == int.MaxValue ? 1 : current + 1;
        target.SetValue(OpacityAnimationGenerationProperty, next);
        return next;
    }

    private static bool IsCurrentOpacityAnimation(UIElement target, int generation) =>
        (int)target.GetValue(OpacityAnimationGenerationProperty) == generation;

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
