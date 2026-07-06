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
    private static readonly DependencyProperty ShellAnimationGenerationProperty =
        DependencyProperty.RegisterAttached(
            "ShellAnimationGeneration",
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
        var current = CurrentShellGeometry(window, shell);
        if (ShouldAnimateWindowDirectly(current, geometry))
        {
            AnimateWindowShellDirectly(window, shell, current, geometry, frameRate);
            return;
        }

        var generation = NextShellAnimationGeneration(window);
        var hostLeft = Math.Min(Math.Min(current.Left, geometry.Left), window.Left);
        var hostTop = Math.Min(Math.Min(current.Top, geometry.Top), window.Top);
        var hostRight = Math.Max(
            Math.Max(current.Left + current.Width, geometry.Left + geometry.Width),
            window.Left + Current(window.Width, window.ActualWidth));
        var hostBottom = Math.Max(
            Math.Max(current.Top + current.WindowHeight, geometry.Top + geometry.WindowHeight),
            window.Top + Current(window.Height, window.ActualHeight));
        var host = new ShellGeometry(
            hostRight - hostLeft,
            hostBottom - hostTop,
            hostBottom - hostTop,
            hostLeft,
            hostTop);
        var fromMargin = new Thickness(current.Left - hostLeft, current.Top - hostTop, 0, 0);
        var toMargin = new Thickness(geometry.Left - hostLeft, geometry.Top - hostTop, 0, 0);

        StopShellAnimations(window, shell);
        ApplyShellGeometry(window, shell, current, host);
        window.UpdateLayout();

        BeginShellAnimation(shell, FrameworkElement.WidthProperty, current.Width, geometry.Width, frameRate);
        BeginShellAnimation(shell, FrameworkElement.HeightProperty, current.ShellHeight, geometry.ShellHeight, frameRate);
        var marginAnimation = new ThicknessAnimation(fromMargin, toMargin, MotionDuration)
        {
            EasingFunction = ShellAnimationTiming.CreateEasing(),
            FillBehavior = FillBehavior.HoldEnd
        };
        marginAnimation.Completed += (_, _) =>
        {
            if (!IsCurrentShellAnimation(window, generation))
            {
                return;
            }

            StopShellAnimations(window, shell);
            ApplyShellGeometry(window, shell, geometry, host);
        };
        Timeline.SetDesiredFrameRate(marginAnimation, frameRate);
        shell.BeginAnimation(FrameworkElement.MarginProperty, marginAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void SetShellGeometry(
        Window window,
        FrameworkElement shell,
        ShellGeometry shellGeometry,
        ShellGeometry hostGeometry)
    {
        _ = NextShellAnimationGeneration(window);
        StopShellAnimations(window, shell);
        ApplyShellGeometry(window, shell, shellGeometry, hostGeometry);
    }

    public static void Clear(Window window, FrameworkElement shell, FrameworkElement detail)
    {
        var current = CurrentShellGeometry(window, shell);
        var host = CurrentHostGeometry(window);
        _ = NextShellAnimationGeneration(window);
        StopShellAnimations(window, shell);
        detail.BeginAnimation(UIElement.OpacityProperty, null);
        ApplyShellGeometry(window, shell, current, host);
    }

    private static void AnimateWindowShellDirectly(
        Window window,
        FrameworkElement shell,
        ShellGeometry current,
        ShellGeometry geometry,
        int frameRate)
    {
        _ = NextShellAnimationGeneration(window);
        StopShellAnimations(window, shell);
        ApplyShellGeometry(window, shell, current);
        Animate(window, Window.WidthProperty, geometry.Width, frameRate);
        Animate(window, Window.HeightProperty, geometry.WindowHeight, frameRate);
        Animate(window, Window.LeftProperty, geometry.Left, frameRate);
        Animate(window, Window.TopProperty, geometry.Top, frameRate);
        Animate(shell, FrameworkElement.WidthProperty, geometry.Width, frameRate);
        Animate(shell, FrameworkElement.HeightProperty, geometry.ShellHeight, frameRate);
    }

    private static bool ShouldAnimateWindowDirectly(ShellGeometry current, ShellGeometry target) =>
        Math.Abs(current.Left - target.Left) > Math.Max(current.Width, target.Width);

    private static void BeginShellAnimation(
        FrameworkElement shell,
        DependencyProperty property,
        double from,
        double value,
        int frameRate)
    {
        var animation = new DoubleAnimation(from, value, MotionDuration)
        {
            EasingFunction = ShellAnimationTiming.CreateEasing(),
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, frameRate);
        shell.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static ShellGeometry CurrentShellGeometry(Window window, FrameworkElement shell)
    {
        var shellLeft = window.Left;
        var shellTop = window.Top;
        if (shell.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
        {
            shellLeft += shell.Margin.Left;
        }

        if (shell.VerticalAlignment == VerticalAlignment.Top)
        {
            shellTop += shell.Margin.Top;
        }

        return new ShellGeometry(
            Current(shell.Width, shell.ActualWidth),
            Current(shell.Height, shell.ActualHeight),
            Current(window.Height, window.ActualHeight),
            shellLeft,
            shellTop);
    }

    private static ShellGeometry CurrentHostGeometry(Window window) =>
        new(
            Current(window.Width, window.ActualWidth),
            Current(window.Height, window.ActualHeight),
            Current(window.Height, window.ActualHeight),
            window.Left,
            window.Top);

    private static void StopShellAnimations(Window window, FrameworkElement shell)
    {
        window.BeginAnimation(Window.WidthProperty, null);
        window.BeginAnimation(Window.HeightProperty, null);
        window.BeginAnimation(Window.LeftProperty, null);
        window.BeginAnimation(Window.TopProperty, null);
        shell.BeginAnimation(FrameworkElement.WidthProperty, null);
        shell.BeginAnimation(FrameworkElement.HeightProperty, null);
        shell.BeginAnimation(FrameworkElement.MarginProperty, null);
    }

    private static double Current(double propertyValue, double actualValue) =>
        actualValue > 0 ? actualValue : propertyValue;

    private static int NextShellAnimationGeneration(Window window)
    {
        var current = (int)window.GetValue(ShellAnimationGenerationProperty);
        var next = current == int.MaxValue ? 1 : current + 1;
        window.SetValue(ShellAnimationGenerationProperty, next);
        return next;
    }

    private static bool IsCurrentShellAnimation(Window window, int generation) =>
        (int)window.GetValue(ShellAnimationGenerationProperty) == generation;

    private static void ApplyShellGeometry(Window window, FrameworkElement shell, ShellGeometry geometry)
    {
        ApplyShellGeometry(window, shell, geometry, geometry);
    }

    private static void ApplyShellGeometry(
        Window window,
        FrameworkElement shell,
        ShellGeometry shellGeometry,
        ShellGeometry hostGeometry)
    {
        window.Top = 0;
        window.Width = hostGeometry.Width;
        window.Height = hostGeometry.WindowHeight;
        window.Left = hostGeometry.Left;
        window.Top = hostGeometry.Top;
        shell.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        shell.VerticalAlignment = VerticalAlignment.Top;
        shell.Margin = new Thickness(
            shellGeometry.Left - hostGeometry.Left,
            shellGeometry.Top - hostGeometry.Top,
            0,
            0);
        shell.Width = shellGeometry.Width;
        shell.Height = shellGeometry.ShellHeight;
    }
}
