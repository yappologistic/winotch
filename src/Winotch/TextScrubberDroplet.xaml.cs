using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfClipboard = System.Windows.Clipboard;

namespace Winotch;

public partial class TextScrubberDroplet : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private bool _closing;
    private TextScrubCase _selectedCase = TextScrubCase.Preserve;

    public TextScrubberDroplet()
    {
        InitializeComponent();
        InputBox.Text = WpfClipboard.ContainsText() ? WpfClipboard.GetText() : string.Empty;
        RefreshOutput();
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

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshOutput();

    private void OptionChanged(object sender, RoutedEventArgs e) => RefreshOutput();

    private void CaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string tag } && Enum.TryParse<TextScrubCase>(tag, out var scrubCase))
        {
            _selectedCase = scrubCase;
            RefreshOutput();
        }
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => FlyoutDragHelper.DragFromHeader(this, e);

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (WpfClipboard.ContainsText())
        {
            InputBox.Text = WpfClipboard.GetText();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(OutputBox.Text);

    private void RefreshOutput()
    {
        if (OutputBox is null)
        {
            return;
        }

        var result = TextScrubberService.Scrub(
            InputBox.Text,
            new TextScrubOptions(
                TrimWhitespace: TrimToggle.IsChecked == true,
                RemoveLineBreaks: LineBreakToggle.IsChecked == true,
                Case: _selectedCase));
        OutputBox.Text = result.Text;
        CountText.Text = $"{result.CharacterCount} chars";
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
