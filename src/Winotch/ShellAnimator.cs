using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Winotch;

public static class ShellAnimator
{
    private static readonly TimeSpan MotionDuration = TimeSpan.FromMilliseconds(380);
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly IEasingFunction Easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
    private static Action? _stopShellAnimation;

    public static void Animate(UIElement target, DependencyProperty property, double value, int frameRate)
    {
        var animation = new DoubleAnimation(value, new Duration(MotionDuration))
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

    public static void AnimateShell(Window window, FrameworkElement shell, ShellGeometry geometry)
    {
        _stopShellAnimation?.Invoke();
        window.Top = 0;
        var start = new ShellGeometry(
            Current(window.Width, window.ActualWidth),
            Current(shell.Height, shell.ActualHeight),
            Current(window.Height, window.ActualHeight),
            window.Left);
        var stopwatch = Stopwatch.StartNew();

        void RenderFrame(object? sender, EventArgs e)
        {
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / MotionDuration.TotalMilliseconds, 0, 1);
            var eased = EaseOut(progress);
            ApplyShellGeometry(window, shell, Lerp(start, geometry, eased));
            if (progress < 1)
            {
                return;
            }

            CompositionTarget.Rendering -= RenderFrame;
            _stopShellAnimation = null;
        }

        _stopShellAnimation = () =>
        {
            CompositionTarget.Rendering -= RenderFrame;
            _stopShellAnimation = null;
        };
        CompositionTarget.Rendering += RenderFrame;
    }

    public static void Clear(Window window, FrameworkElement shell, FrameworkElement detail)
    {
        _stopShellAnimation?.Invoke();
        var current = new ShellGeometry(
            Current(window.Width, window.ActualWidth),
            Current(shell.Height, shell.ActualHeight),
            Current(window.Height, window.ActualHeight),
            window.Left);
        window.BeginAnimation(Window.WidthProperty, null);
        window.BeginAnimation(Window.HeightProperty, null);
        window.BeginAnimation(Window.LeftProperty, null);
        shell.BeginAnimation(FrameworkElement.WidthProperty, null);
        shell.BeginAnimation(FrameworkElement.HeightProperty, null);
        detail.BeginAnimation(UIElement.OpacityProperty, null);
        ApplyShellGeometry(window, shell, current);
    }

    public static double EaseOut(double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);
        return 1 - Math.Pow(1 - clamped, 4);
    }

    private static ShellGeometry Lerp(ShellGeometry start, ShellGeometry end, double progress) => new(
        Lerp(start.Width, end.Width, progress),
        Lerp(start.ShellHeight, end.ShellHeight, progress),
        Lerp(start.WindowHeight, end.WindowHeight, progress),
        Lerp(start.Left, end.Left, progress));

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * progress);

    private static double Current(double propertyValue, double actualValue) =>
        actualValue > 0 ? actualValue : propertyValue;

    private static void ApplyShellGeometry(Window window, FrameworkElement shell, ShellGeometry geometry)
    {
        window.Top = 0;
        window.Width = geometry.Width;
        window.Height = geometry.WindowHeight;
        window.Left = geometry.Left;
        shell.Width = geometry.Width;
        shell.Height = geometry.ShellHeight;
    }
}
