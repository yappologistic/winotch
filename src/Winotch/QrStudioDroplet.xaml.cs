using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfClipboard = System.Windows.Clipboard;

namespace Winotch;

public partial class QrStudioDroplet : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private BitmapSource? _latestQr;
    private bool _closing;

    public QrStudioDroplet()
    {
        InitializeComponent();
        QrInputBox.Text = WpfClipboard.ContainsText() ? WpfClipboard.GetText() : string.Empty;
        RefreshQr();
    }

    public async Task CloseDropletAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await AnimateOutAsync();
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnimateIn();

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing && await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseDropletAsync();
        }
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CloseDropletAsync();
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseDropletAsync();

    private void QrInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshQr();

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (WpfClipboard.ContainsText())
        {
            QrInputBox.Text = WpfClipboard.GetText();
        }
    }

    private void CopyPngButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestQr is not null)
        {
            WpfClipboard.SetImage(_latestQr);
        }
    }

    private void RefreshQr()
    {
        try
        {
            var qr = QrCodeEncoder.EncodeText(QrInputBox.Text.Trim());
            _latestQr = QrCodeEncoder.Render(qr, scale: 6);
            QrImage.Source = _latestQr;
            QrErrorText.Visibility = Visibility.Collapsed;
        }
        catch (ArgumentException)
        {
            _latestQr = null;
            QrImage.Source = null;
            QrErrorText.Text = "Short text only in v1";
            QrErrorText.Visibility = Visibility.Visible;
        }
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
}
