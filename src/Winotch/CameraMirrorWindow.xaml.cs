using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WinSize = Windows.Foundation.Size;

namespace Winotch;

public sealed partial class CameraMirrorWindow : FluentWindow
{
    private const float PreviewCornerRadius = 18;
    private readonly CameraMirrorService _cameraMirror;
    private CompositionRoundedRectangleGeometry? _previewClipGeometry;
    private WriteableBitmap? _previewBitmap;
    private WinSize _frameSize;
    private bool _closing;

    public CameraMirrorWindow(CameraMirrorService cameraMirror)
    {
        InitializeComponent();
        _cameraMirror = cameraMirror;
        _cameraMirror.StateChanged += CameraMirror_StateChanged;
        _cameraMirror.FrameReady += CameraMirror_FrameReady;
        ApplyState(_cameraMirror.State);
    }

    public async Task CloseMirrorAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await _cameraMirror.CloseAsync();
        await AnimateOutAsync();
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnimateIn();

    private async void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated || _closing)
        {
            return;
        }

        if (await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseMirrorAsync();
        }
    }

    private async void EscapeKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CloseMirrorAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseMirrorAsync();

    private void HeaderDragArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        FlyoutDragHelper.DragFromHeader(this, e);

    private void MirrorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var mirrored = MirrorToggleButton.IsChecked == true;
        PreviewMirrorTransform.ScaleX = mirrored ? -1 : 1;
        MirrorToggleText.Text = mirrored ? "Mirror" : "Normal";
        ToolTipService.SetToolTip(
            MirrorToggleButton,
            mirrored ? "Show unmirrored preview" : "Show mirrored preview");
    }

    private void CameraMirror_StateChanged(CameraMirrorState state)
    {
        _ = DispatcherQueue.TryEnqueue(() => ApplyState(state));
    }

    private void CameraMirror_FrameReady(CameraMirrorFrame frame)
    {
        _ = DispatcherQueue.TryEnqueue(() => ApplyFrame(frame));
    }

    private void ApplyState(CameraMirrorState state)
    {
        MirrorMessageText.Text = state.Phase switch
        {
            CameraMirrorPhase.Opening => "Opening camera",
            CameraMirrorPhase.Error => state.Message,
            _ => ""
        };
        MirrorMessageText.Visibility = string.IsNullOrWhiteSpace(MirrorMessageText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (state.Phase == CameraMirrorPhase.Error)
        {
            PreviewImage.Source = null;
            _previewBitmap = null;
            _frameSize = default;
        }
    }

    private void ApplyFrame(CameraMirrorFrame frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.BgraPixels.Length == 0)
        {
            return;
        }

        if (_previewBitmap is null ||
            _previewBitmap.PixelWidth != frame.PixelWidth ||
            _previewBitmap.PixelHeight != frame.PixelHeight)
        {
            _previewBitmap = new WriteableBitmap(frame.PixelWidth, frame.PixelHeight);
            PreviewImage.Source = _previewBitmap;
        }

        // CameraMirrorService already normalizes frames to premultiplied BGRA8.
        // Copying directly into the WinUI pixel buffer avoids a second conversion.
        using (var pixelStream = _previewBitmap.PixelBuffer.AsStream())
        {
            pixelStream.Position = 0;
            pixelStream.Write(frame.BgraPixels, 0, frame.BgraPixels.Length);
        }

        _previewBitmap.Invalidate();
        _frameSize = new WinSize(frame.PixelWidth, frame.PixelHeight);
        ApplyPreviewLayout();
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var previewVisual = ElementCompositionPreview.GetElementVisual(PreviewViewport);
        _previewClipGeometry ??= previewVisual.Compositor.CreateRoundedRectangleGeometry();
        _previewClipGeometry.Size = new Vector2(
            (float)PreviewViewport.ActualWidth,
            (float)PreviewViewport.ActualHeight);
        _previewClipGeometry.CornerRadius = new Vector2(PreviewCornerRadius);
        previewVisual.Clip ??= previewVisual.Compositor.CreateGeometricClip(_previewClipGeometry);
        ApplyPreviewLayout();
    }

    private void ApplyPreviewLayout()
    {
        var placement = CameraMirrorLayout.Cover(
            _frameSize,
            new WinSize(PreviewViewport.ActualWidth, PreviewViewport.ActualHeight));
        if (placement.IsEmpty)
        {
            PreviewImage.Width = 0;
            PreviewImage.Height = 0;
            Canvas.SetLeft(PreviewImage, 0);
            Canvas.SetTop(PreviewImage, 0);
            return;
        }

        PreviewImage.Width = placement.Width;
        PreviewImage.Height = placement.Height;
        Canvas.SetLeft(PreviewImage, placement.X);
        Canvas.SetTop(PreviewImage, placement.Y);
    }

    private void AnimateIn()
    {
        var visual = PrepareFlyoutVisual();
        visual.Opacity = 0;
        visual.Scale = new Vector3(0.96f, 0.96f, 1);

        var compositor = visual.Compositor;
        var easing = CreateEaseOut(compositor);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = ShellAnimationTiming.FadeDuration;
        opacity.InsertKeyFrame(1, 1, easing);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.MotionDuration;
        scale.InsertKeyFrame(1, Vector3.One, easing);

        visual.StartAnimation(nameof(Visual.Opacity), opacity);
        visual.StartAnimation(nameof(Visual.Scale), scale);
    }

    private async Task AnimateOutAsync()
    {
        var visual = PrepareFlyoutVisual();
        var compositor = visual.Compositor;
        var easing = CreateEaseOut(compositor);
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = ShellAnimationTiming.FadeDuration;
        opacity.InsertKeyFrame(1, 0, easing);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.FadeDuration;
        scale.InsertKeyFrame(1, new Vector3(0.96f, 0.96f, 1), easing);

        visual.StartAnimation(nameof(Visual.Opacity), opacity);
        visual.StartAnimation(nameof(Visual.Scale), scale);
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    private Visual PrepareFlyoutVisual()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        visual.CenterPoint = new Vector3(
            (float)(FlyoutChrome.ActualWidth / 2),
            (float)(FlyoutChrome.ActualHeight / 2),
            0);
        return visual;
    }

    private static CompositionEasingFunction CreateEaseOut(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1),
            new Vector2(0.3f, 1));

    private async void Window_Closed(object sender, WindowEventArgs e)
    {
        _cameraMirror.StateChanged -= CameraMirror_StateChanged;
        _cameraMirror.FrameReady -= CameraMirror_FrameReady;
        if (!_closing)
        {
            _closing = true;
            await _cameraMirror.CloseAsync();
        }
    }
}
