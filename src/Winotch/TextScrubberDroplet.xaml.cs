using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Winotch;

/// <summary>
/// Native WinUI text utility. Text remains local: clipboard access only occurs
/// for the explicit initial load, Paste, and Copy actions.
/// </summary>
public sealed partial class TextScrubberDroplet : FluentWindow
{
    private bool _closing;
    private bool _loadedClipboard;
    private TextScrubCase _selectedCase = TextScrubCase.Preserve;

    public TextScrubberDroplet()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        Activated += Window_Activated;
        Closed += Window_Closed;
        RefreshCaseSelection();
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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateIn();
        if (!_loadedClipboard)
        {
            _loadedClipboard = true;
            await PasteClipboardTextAsync();
        }

        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectionStart = InputBox.Text.Length;
    }

    private async void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated || _closing)
        {
            return;
        }

        if (await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseDropletAsync();
        }
    }

    private async void FlyoutChrome_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        await CloseDropletAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseDropletAsync();

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshOutput();

    private void OptionChanged(object sender, RoutedEventArgs e) => RefreshOutput();

    private void CaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            Enum.TryParse<TextScrubCase>(tag, out var scrubCase))
        {
            _selectedCase = scrubCase;
            RefreshCaseSelection();
            RefreshOutput();
        }
    }

    private void HeaderDragArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        FlyoutDragHelper.DragFromHeader(this, e);

    private async void PasteButton_Click(object sender, RoutedEventArgs e) => await PasteClipboardTextAsync();

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        package.SetText(OutputBox.Text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private async Task PasteClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            InputBox.Text = await content.GetTextAsync();
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            // Clipboard ownership can change between GetContent and GetTextAsync.
            // Keeping the current input is the least surprising response.
        }
    }

    private void RefreshOutput()
    {
        if (OutputBox is null || InputBox is null || TrimToggle is null ||
            LineBreakToggle is null || CountText is null)
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

    private void RefreshCaseSelection()
    {
        if (PreserveCaseButton is null)
        {
            return;
        }

        SetCaseButtonState(PreserveCaseButton, TextScrubCase.Preserve);
        SetCaseButtonState(UpperCaseButton, TextScrubCase.Upper);
        SetCaseButtonState(LowerCaseButton, TextScrubCase.Lower);
        SetCaseButtonState(TitleCaseButton, TextScrubCase.Title);
        SetCaseButtonState(SentenceCaseButton, TextScrubCase.Sentence);
    }

    private void SetCaseButtonState(Button button, TextScrubCase scrubCase)
    {
        var selected = _selectedCase == scrubCase;
        button.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            selected ? "NotchPanelPressed" : "NotchPanel"];
        button.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            selected ? "NotchAccent" : "NotchStroke"];
        AutomationProperties.SetName(button, $"{button.Content} case{(selected ? ", selected" : string.Empty)}");
    }

    private void AnimateIn()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        visual.CenterPoint = new Vector3(
            (float)(FlyoutChrome.ActualWidth / 2),
            0,
            0);
        visual.Opacity = 0;
        visual.Scale = new Vector3(0.96f, 0.96f, 1);

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1),
            new Vector2(0.3f, 1));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = ShellAnimationTiming.FadeDuration;
        fade.InsertKeyFrame(1, 1, easing);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.MotionDuration;
        scale.InsertKeyFrame(1, Vector3.One, easing);
        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Scale), scale);
    }

    private async Task AnimateOutAsync()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.7f, 0),
            new Vector2(0.84f, 0));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = ShellAnimationTiming.FadeDuration;
        fade.InsertKeyFrame(1, 0, easing);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.FadeDuration;
        scale.InsertKeyFrame(1, new Vector3(0.96f, 0.96f, 1), easing);
        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Scale), scale);
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    private void Window_Closed(object sender, WindowEventArgs e)
    {
        Loaded -= Window_Loaded;
        Activated -= Window_Activated;
        Closed -= Window_Closed;
    }
}
