using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;

namespace Winotch;

public partial class ColorPickerDroplet : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private readonly ColorPickerService _picker;
    private PickedColor _color = new(0, 0, 0);
    private bool _closing;

    public ColorPickerDroplet(ColorPickerService picker)
    {
        InitializeComponent();
        _picker = picker;
        ApplyColor(_color);
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
        if (!_closing)
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

    private void PickButton_Click(object sender, RoutedEventArgs e)
    {
        var position = Forms.Cursor.Position;
        ApplyColor(_picker.PickScreenPixel(position.X, position.Y));
    }

    private void CopyHexButton_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(_color.Hex);

    private void CopyRgbButton_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(_color.RgbText);

    private void ApplyColor(PickedColor color)
    {
        _color = color;
        HexText.Text = color.Hex;
        RgbText.Text = color.RgbText;
        ColorSwatch.Background = new SolidColorBrush(WpfColor.FromRgb(color.R, color.G, color.B));
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
