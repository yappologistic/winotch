using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfSize = System.Windows.Size;

namespace Winotch;

public partial class CameraMirrorWindow : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private readonly CameraMirrorService _cameraMirror;
    private WriteableBitmap? _previewBitmap;
    private WpfSize _frameSize;
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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateIn();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing)
        {
            await CloseMirrorAsync();
        }
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        await CloseMirrorAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        await CloseMirrorAsync();
    }

    private void MirrorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var mirrored = MirrorToggleButton.IsChecked == true;
        PreviewMirrorTransform.ScaleX = mirrored ? -1 : 1;
        MirrorToggleText.Text = mirrored ? "Mirror" : "Normal";
        MirrorToggleButton.ToolTip = mirrored ? "Show unmirrored preview" : "Show mirrored preview";
    }

    private void CameraMirror_StateChanged(CameraMirrorState state)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyState(state));
    }

    private void CameraMirror_FrameReady(CameraMirrorFrame frame)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyFrame(frame));
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
            _previewBitmap = new WriteableBitmap(
                frame.PixelWidth,
                frame.PixelHeight,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);
            PreviewImage.Source = _previewBitmap;
        }

        _previewBitmap.WritePixels(
            new Int32Rect(0, 0, frame.PixelWidth, frame.PixelHeight),
            frame.BgraPixels,
            frame.Stride,
            0);
        _frameSize = new WpfSize(frame.PixelWidth, frame.PixelHeight);
        ApplyPreviewLayout();
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyPreviewLayout();
    }

    private void ApplyPreviewLayout()
    {
        var placement = CameraMirrorLayout.AspectFit(
            _frameSize,
            new WpfSize(PreviewViewport.ActualWidth, PreviewViewport.ActualHeight));
        PreviewImage.Width = placement.Width;
        PreviewImage.Height = placement.Height;
        Canvas.SetLeft(PreviewImage, placement.X);
        Canvas.SetTop(PreviewImage, placement.Y);
    }

    private void AnimateIn()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, MotionDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, MotionDuration) { EasingFunction = Easing });
    }

    private async Task AnimateOutAsync()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, FadeDuration) { EasingFunction = Easing });
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    protected override async void OnClosed(EventArgs e)
    {
        _cameraMirror.StateChanged -= CameraMirror_StateChanged;
        _cameraMirror.FrameReady -= CameraMirror_FrameReady;
        if (!_closing)
        {
            await _cameraMirror.CloseAsync();
        }

        base.OnClosed(e);
    }
}
