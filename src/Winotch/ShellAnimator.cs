using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Winotch;

/// <summary>
/// Native WinUI shell motion driven by Windows Composition. Layout is committed
/// to its destination immediately while the backing visual presents a smooth
/// transform from the old geometry. This keeps input and accessibility geometry
/// truthful without running dependent layout animations every frame.
/// </summary>
public static class ShellAnimator
{
    private const float HiddenRevealScale = 0.975f;
    private const float HiddenRevealTranslation = -4f;

    private static readonly ConditionalWeakTable<UIElement, ElementAnimationState> ElementStates = new();
    private static readonly ConditionalWeakTable<FluentWindow, ShellAnimationState> ShellStates = new();

    /// <summary>
    /// Animates the WinUI double properties used by the shell. Opacity is a
    /// compositor scalar; width and height are rendered as centered scale
    /// transitions after their final layout value is committed.
    /// </summary>
    public static void Animate(UIElement target, DependencyProperty property, double value, int frameRate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateFrameRate(frameRate);

        if (property == UIElement.OpacityProperty)
        {
            AnimateOpacity(target, value, ShellAnimationTiming.MotionDuration, frameRate, collapseWhenDone: false);
            return;
        }

        if (target is FrameworkElement element && property == FrameworkElement.WidthProperty)
        {
            AnimateDimension(element, value, Dimension.Width, frameRate);
            return;
        }

        if (target is FrameworkElement heightElement && property == FrameworkElement.HeightProperty)
        {
            AnimateDimension(heightElement, value, Dimension.Height, frameRate);
            return;
        }

        throw new ArgumentException("ShellAnimator supports Opacity, Width, and Height in WinUI 3.", nameof(property));
    }

    public static void Hide(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        StopElementAnimations(target, resetTransform: true);
        target.Opacity = 0;
        target.Visibility = Visibility.Collapsed;
    }

    public static void Hide(UIElement target, int frameRate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateFrameRate(frameRate);

        if (target.Visibility != Visibility.Visible)
        {
            Hide(target);
            return;
        }

        var state = ElementStates.GetOrCreateValue(target);
        var current = state.Opacity?.Current() ?? Math.Clamp(target.Opacity, 0, 1);
        if (current <= 0)
        {
            Hide(target);
            return;
        }

        AnimateOpacity(target, 0, ShellAnimationTiming.FadeDuration, frameRate, collapseWhenDone: true);
    }

    public static void Show(UIElement target, int frameRate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateFrameRate(frameRate);
        if (target.Visibility != Visibility.Visible)
        {
            // A collapsed element can retain an arbitrary XAML opacity. Reveal
            // it from transparent just as WinUI's built-in show transitions do.
            target.Opacity = 0;
        }

        target.Visibility = Visibility.Visible;
        AnimateOpacity(target, 1, ShellAnimationTiming.FadeDuration, frameRate, collapseWhenDone: false);
    }

    /// <summary>
    /// Morphs the top-attached shell inside a stable union host. A compositor
    /// clip reveals or conceals the final surface without scaling its text,
    /// controls, or system-backdrop pixels.
    /// </summary>
    public static void AnimateShell(
        FluentWindow window,
        FrameworkElement shell,
        ShellGeometry geometry,
        int frameRate)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shell);
        ValidateFrameRate(frameRate);

        var state = ShellStates.GetOrCreateValue(window);
        var current = state.Transition?.Current() ?? CurrentShellGeometry(window, shell);
        var generation = state.NextGeneration();
        state.StopTimer();
        StopShellVisual(shell);

        var crossesMonitors = CrossesMonitorBoundary(current, geometry);
        var currentHost = CurrentHostGeometry(window);
        var host = crossesMonitors
            ? geometry with { ShellHeight = geometry.WindowHeight }
            : UnionHost(current, geometry, currentHost);

        // Moving a real HWND between monitors is committed atomically so a
        // transition never spans two DPI coordinate systems.
        if (crossesMonitors)
        {
            ApplyShellGeometry(window, shell, geometry, geometry);
            state.Transition = null;
            return;
        }

        var expanding = geometry.Width >= current.Width && geometry.ShellHeight >= current.ShellHeight;
        var displayedGeometry = expanding ? geometry : current;
        ApplyShellGeometry(window, shell, displayedGeometry, host);
        shell.UpdateLayout();

        var visual = ElementCompositionPreview.GetElementVisual(shell);
        var compositor = visual.Compositor;
        var easing = ShellAnimationTiming.CreateCompositionEasing(compositor);
        var clip = compositor.CreateInsetClip();
        var horizontalDifference = Math.Max(0, Math.Abs(geometry.Width - current.Width) / 2);
        var verticalDifference = Math.Max(0, Math.Abs(geometry.ShellHeight - current.ShellHeight));
        var fromHorizontalInset = expanding ? horizontalDifference : 0;
        var toHorizontalInset = expanding ? 0 : horizontalDifference;
        var fromBottomInset = expanding ? verticalDifference : 0;
        var toBottomInset = expanding ? 0 : verticalDifference;
        clip.LeftInset = (float)fromHorizontalInset;
        clip.RightInset = (float)fromHorizontalInset;
        clip.BottomInset = (float)fromBottomInset;
        visual.Clip = clip;

        var leftInset = CreateInsetAnimation(compositor, fromHorizontalInset, toHorizontalInset, easing);
        var rightInset = CreateInsetAnimation(compositor, fromHorizontalInset, toHorizontalInset, easing);
        var bottomInset = CreateInsetAnimation(compositor, fromBottomInset, toBottomInset, easing);
        var transition = new GeometryTransition(current, geometry, Stopwatch.GetTimestamp());
        StartVisibleRegionAnimation(window, shell, host, transition, state, generation, frameRate);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            if (state.Generation != generation)
            {
                return;
            }

            state.StopTimer();
            StopShellVisual(shell);
            ApplyShellGeometry(window, shell, geometry, geometry);
            state.Transition = null;
        };
        clip.StartAnimation(nameof(InsetClip.LeftInset), leftInset);
        clip.StartAnimation(nameof(InsetClip.RightInset), rightInset);
        clip.StartAnimation(nameof(InsetClip.BottomInset), bottomInset);
        batch.End();

        state.Transition = transition;
    }

    private static ScalarKeyFrameAnimation CreateInsetAnimation(
        Compositor compositor,
        double from,
        double to,
        CompositionEasingFunction easing)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = ShellAnimationTiming.MotionDuration;
        animation.InsertKeyFrame(0, (float)from);
        animation.InsertKeyFrame(1, (float)to, easing);
        return animation;
    }

    private static void StartVisibleRegionAnimation(
        FluentWindow window,
        FrameworkElement shell,
        ShellGeometry host,
        GeometryTransition transition,
        ShellAnimationState state,
        int generation,
        int frameRate)
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, 1000d / frameRate))
        };
        timer.Tick += (_, _) =>
        {
            if (state.Generation != generation)
            {
                timer.Stop();
                return;
            }

            if (transition.HasCompleted)
            {
                timer.Stop();
                StopShellVisual(shell);
                ApplyShellGeometry(window, shell, transition.To, transition.To);
                state.Timer = null;
                state.Transition = null;
                return;
            }

            window.ApplyVisibleShellRegion(transition.Current(), host);
        };
        state.Timer = timer;
        window.ApplyVisibleShellRegion(transition.Current(), host);
        timer.Start();
    }

    public static void SetShellGeometry(
        FluentWindow window,
        FrameworkElement shell,
        ShellGeometry shellGeometry,
        ShellGeometry hostGeometry)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shell);
        var state = ShellStates.GetOrCreateValue(window);
        state.NextGeneration();
        state.StopTimer();
        state.Transition = null;
        StopShellVisual(shell);
        ApplyShellGeometry(window, shell, shellGeometry, hostGeometry);
    }

    public static void Clear(FluentWindow window, FrameworkElement shell, FrameworkElement detail)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(detail);

        var state = ShellStates.GetOrCreateValue(window);
        var current = state.Transition?.Current() ?? CurrentShellGeometry(window, shell);
        var host = CurrentHostGeometry(window);
        state.NextGeneration();
        state.StopTimer();
        state.Transition = null;
        StopShellVisual(shell);
        StopElementAnimations(detail, resetTransform: false);
        ApplyShellGeometry(window, shell, current, host);
    }

    private static void AnimateOpacity(
        UIElement target,
        double value,
        TimeSpan duration,
        int frameRate,
        bool collapseWhenDone)
    {
        var to = Math.Clamp(value, 0, 1);
        var state = ElementStates.GetOrCreateValue(target);
        var from = state.Opacity?.Current() ?? Math.Clamp(target.Opacity, 0, 1);
        var revealFrom = state.Reveal?.Current() ?? (from > 0 ? 1d : 0d);
        var revealTo = to > 0 ? 1d : 0d;
        var generation = state.NextGeneration();

        var visual = ElementCompositionPreview.GetElementVisual(target);
        StopRevealVisual(visual);
        target.Opacity = to;

        var width = target is FrameworkElement element ? element.ActualWidth : 0;
        var height = target is FrameworkElement heightElement ? heightElement.ActualHeight : 0;
        visual.CenterPoint = new Vector3((float)(width / 2), (float)(height / 2), 0);
        var baseOffset = visual.Offset;

        var compositor = visual.Compositor;
        var easing = ShellAnimationTiming.CreateCompositionEasing(compositor);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = duration;
        opacity.InsertKeyFrame(0, (float)from);
        opacity.InsertKeyFrame(1, (float)to, easing);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = duration;
        scale.InsertKeyFrame(0, RevealScale(revealFrom));
        scale.InsertKeyFrame(1, RevealScale(revealTo), easing);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = duration;
        offset.InsertKeyFrame(0, RevealOffset(baseOffset, revealFrom));
        offset.InsertKeyFrame(1, RevealOffset(baseOffset, revealTo), easing);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            if (state.Generation != generation)
            {
                return;
            }

            StopRevealVisual(visual);
            target.Opacity = to;
            visual.Scale = Vector3.One;
            state.Opacity = null;
            state.Reveal = null;
            if (collapseWhenDone && to <= 0)
            {
                target.Visibility = Visibility.Collapsed;
            }
        };
        visual.StartAnimation(nameof(Visual.Opacity), opacity);
        visual.StartAnimation(nameof(Visual.Scale), scale);
        visual.StartAnimation(nameof(Visual.Offset), offset);
        batch.End();

        var started = Stopwatch.GetTimestamp();
        state.Opacity = new ScalarTransition(from, to, duration, started);
        state.Reveal = new ScalarTransition(revealFrom, revealTo, duration, started);

        // Composition uses the display refresh cadence. Retaining frameRate in
        // this API keeps settings/call sites stable and validation catches bad
        // persisted values without reverting to dependent XAML animations.
        _ = frameRate;
    }

    private static void AnimateDimension(
        FrameworkElement element,
        double value,
        Dimension dimension,
        int frameRate)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var state = ElementStates.GetOrCreateValue(element);
        var previous = dimension == Dimension.Width
            ? state.Width?.Current() ?? Current(element.Width, element.ActualWidth)
            : state.Height?.Current() ?? Current(element.Height, element.ActualHeight);
        var generation = state.NextDimensionGeneration(dimension);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var property = dimension == Dimension.Width ? "Scale.X" : "Scale.Y";
        visual.StopAnimation(property);

        element.SetValue(
            dimension == Dimension.Width ? FrameworkElement.WidthProperty : FrameworkElement.HeightProperty,
            value);
        element.UpdateLayout();
        visual.CenterPoint = new Vector3(
            (float)(Positive(element.ActualWidth) / 2),
            (float)(Positive(element.ActualHeight) / 2),
            0);

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = ShellAnimationTiming.MotionDuration;
        animation.InsertKeyFrame(0, (float)(Positive(previous) / Positive(value)));
        animation.InsertKeyFrame(1, 1, ShellAnimationTiming.CreateCompositionEasing(visual.Compositor));
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            if (!state.IsCurrentDimensionGeneration(dimension, generation))
            {
                return;
            }

            visual.StopAnimation(property);
            if (dimension == Dimension.Width)
            {
                state.Width = null;
            }
            else
            {
                state.Height = null;
            }
        };
        visual.StartAnimation(property, animation);
        batch.End();

        var transition = new ScalarTransition(previous, value, ShellAnimationTiming.MotionDuration, Stopwatch.GetTimestamp());
        if (dimension == Dimension.Width)
        {
            state.Width = transition;
        }
        else
        {
            state.Height = transition;
        }

        _ = frameRate;
    }

    internal static ShellGeometry UnionHost(
        ShellGeometry current,
        ShellGeometry target,
        ShellGeometry currentHost)
    {
        var hostLeft = Math.Min(Math.Min(current.Left, target.Left), currentHost.Left);
        var hostTop = Math.Min(Math.Min(current.Top, target.Top), currentHost.Top);
        var hostRight = Math.Max(
            Math.Max(current.Left + current.Width, target.Left + target.Width),
            currentHost.Left + currentHost.Width);
        var hostBottom = Math.Max(
            Math.Max(current.Top + current.WindowHeight, target.Top + target.WindowHeight),
            currentHost.Top + currentHost.WindowHeight);
        return new ShellGeometry(
            hostRight - hostLeft,
            hostBottom - hostTop,
            hostBottom - hostTop,
            hostLeft,
            hostTop,
            target.DpiScale);
    }

    internal static bool CrossesMonitorBoundary(ShellGeometry current, ShellGeometry target)
    {
        var currentScale = current.DpiScale > 0 ? current.DpiScale : 1d;
        var targetScale = target.DpiScale > 0 ? target.DpiScale : currentScale;
        if (Math.Abs(currentScale - targetScale) > 0.001)
        {
            return true;
        }

        var physicalDelta = Math.Abs((current.Left * currentScale) - (target.Left * targetScale));
        var widestPhysicalState = Math.Max(current.Width * currentScale, target.Width * targetScale);
        return physicalDelta >= widestPhysicalState;
    }

    private static ShellGeometry CurrentShellGeometry(FluentWindow window, FrameworkElement shell)
    {
        var left = window.Left;
        var top = window.Top;
        if (shell.HorizontalAlignment == HorizontalAlignment.Left)
        {
            left += shell.Margin.Left;
        }
        else if (shell.HorizontalAlignment == HorizontalAlignment.Center)
        {
            left += (window.Width - Current(shell.Width, shell.ActualWidth)) / 2;
        }

        if (shell.VerticalAlignment == VerticalAlignment.Top)
        {
            top += shell.Margin.Top;
        }

        return new ShellGeometry(
            Current(shell.Width, shell.ActualWidth),
            Current(shell.Height, shell.ActualHeight),
            window.Height,
            left,
            top,
            window.RasterizationScale);
    }

    private static ShellGeometry CurrentHostGeometry(FluentWindow window) =>
        new(window.Width, window.Height, window.Height, window.Left, window.Top, window.RasterizationScale);

    private static void ApplyShellGeometry(
        FluentWindow window,
        FrameworkElement shell,
        ShellGeometry shellGeometry,
        ShellGeometry hostGeometry)
    {
        // Set the visible HRGN before enlarging the native union host. Without
        // this ordering, WM_SIZE can briefly restore the full opaque host and
        // expose a black rectangle around the acrylic shell.
        window.PrepareVisibleShellRegion(shellGeometry, hostGeometry);
        if (hostGeometry.DpiScale > 0)
        {
            window.MoveAndResizeAtScale(
                hostGeometry.Left,
                hostGeometry.Top,
                hostGeometry.Width,
                hostGeometry.WindowHeight,
                hostGeometry.DpiScale);
        }
        else
        {
            window.MoveAndResize(
                hostGeometry.Left,
                hostGeometry.Top,
                hostGeometry.Width,
                hostGeometry.WindowHeight);
        }
        shell.HorizontalAlignment = HorizontalAlignment.Left;
        shell.VerticalAlignment = VerticalAlignment.Top;
        shell.Margin = new Thickness(
            shellGeometry.Left - hostGeometry.Left,
            shellGeometry.Top - hostGeometry.Top,
            0,
            0);
        shell.Width = shellGeometry.Width;
        shell.Height = shellGeometry.ShellHeight;
    }

    private static void StopElementAnimations(UIElement target, bool resetTransform)
    {
        var state = ElementStates.GetOrCreateValue(target);
        state.NextGeneration();
        state.NextDimensionGeneration(Dimension.Width);
        state.NextDimensionGeneration(Dimension.Height);
        state.Opacity = null;
        state.Reveal = null;
        state.Width = null;
        state.Height = null;

        var visual = ElementCompositionPreview.GetElementVisual(target);
        StopRevealVisual(visual);
        visual.StopAnimation("Scale.X");
        visual.StopAnimation("Scale.Y");
        if (resetTransform)
        {
            visual.Scale = Vector3.One;
        }
    }

    private static void StopRevealVisual(Visual visual)
    {
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Offset));
    }

    private static void StopShellVisual(FrameworkElement shell)
    {
        var visual = ElementCompositionPreview.GetElementVisual(shell);
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Offset));
        visual.Scale = Vector3.One;
        if (visual.Clip is InsetClip clip)
        {
            clip.StopAnimation(nameof(InsetClip.LeftInset));
            clip.StopAnimation(nameof(InsetClip.RightInset));
            clip.StopAnimation(nameof(InsetClip.BottomInset));
        }

        visual.Clip = null;
    }

    private static Vector3 RevealScale(double progress)
    {
        var scale = HiddenRevealScale + ((1 - HiddenRevealScale) * (float)progress);
        return new Vector3(scale, scale, 1);
    }

    private static Vector3 RevealOffset(Vector3 baseOffset, double progress) =>
        baseOffset + new Vector3(0, HiddenRevealTranslation * (float)(1 - progress), 0);

    private static double Current(double propertyValue, double actualValue) =>
        actualValue > 0 ? actualValue : double.IsFinite(propertyValue) ? propertyValue : 0;

    private static double Positive(double value) => Math.Max(0.001, value);

    private static void ValidateFrameRate(int frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }
    }

    private enum Dimension
    {
        Width,
        Height
    }

    private sealed class ElementAnimationState
    {
        public int Generation { get; private set; }
        public int WidthGeneration { get; private set; }
        public int HeightGeneration { get; private set; }
        public ScalarTransition? Opacity { get; set; }
        public ScalarTransition? Reveal { get; set; }
        public ScalarTransition? Width { get; set; }
        public ScalarTransition? Height { get; set; }

        public int NextGeneration() => Generation = Next(Generation);

        public int NextDimensionGeneration(Dimension dimension)
        {
            if (dimension == Dimension.Width)
            {
                return WidthGeneration = Next(WidthGeneration);
            }

            return HeightGeneration = Next(HeightGeneration);
        }

        public bool IsCurrentDimensionGeneration(Dimension dimension, int generation) =>
            (dimension == Dimension.Width ? WidthGeneration : HeightGeneration) == generation;
    }

    private sealed class ShellAnimationState
    {
        public int Generation { get; private set; }
        public GeometryTransition? Transition { get; set; }
        public DispatcherTimer? Timer { get; set; }

        public int NextGeneration() => Generation = Next(Generation);

        public void StopTimer()
        {
            Timer?.Stop();
            Timer = null;
        }
    }

    private sealed record ScalarTransition(double From, double To, TimeSpan Duration, long Started)
    {
        public double Current() => Lerp(From, To, Progress(Duration, Started));
    }

    private sealed record GeometryTransition(ShellGeometry From, ShellGeometry To, long Started)
    {
        public bool HasCompleted => Stopwatch.GetElapsedTime(Started) >= ShellAnimationTiming.MotionDuration;

        public ShellGeometry Current()
        {
            var progress = Progress(ShellAnimationTiming.MotionDuration, Started);
            return new ShellGeometry(
                Lerp(From.Width, To.Width, progress),
                Lerp(From.ShellHeight, To.ShellHeight, progress),
                Lerp(From.WindowHeight, To.WindowHeight, progress),
                Lerp(From.Left, To.Left, progress),
                Lerp(From.Top, To.Top, progress),
                To.DpiScale > 0 ? To.DpiScale : From.DpiScale);
        }
    }

    private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;

    private static double Progress(TimeSpan duration, long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return ShellAnimationTiming.EaseCompositionProgress(elapsed / duration.TotalMilliseconds);
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);
}
