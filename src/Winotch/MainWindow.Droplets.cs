using System.Windows;

namespace Winotch;

public partial class MainWindow
{
    private readonly ColorPickerService _colorPicker = new();
    private ColorPickerDroplet? _colorPickerDroplet;
    private QrStudioDroplet? _qrStudioDroplet;
    private TextScrubberDroplet? _textScrubberDroplet;

    private void ApplyDropletSettings(DropletSettings settings)
    {
        ColorPickerButton.Visibility = settings.ColorPickerEnabled ? Visibility.Visible : Visibility.Collapsed;
        QrStudioButton.Visibility = settings.QrStudioEnabled ? Visibility.Visible : Visibility.Collapsed;
        TextScrubberButton.Visibility = settings.TextScrubberEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!settings.ColorPickerEnabled)
        {
            _ = CloseColorPickerAsync();
        }

        if (!settings.QrStudioEnabled)
        {
            _ = CloseQrStudioAsync();
        }

        if (!settings.TextScrubberEnabled)
        {
            _ = CloseTextScrubberAsync();
        }
    }

    private async void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        if (_colorPickerDroplet is not null)
        {
            await CloseColorPickerAsync();
            return;
        }

        _colorPickerDroplet = new ColorPickerDroplet(_colorPicker) { Owner = this };
        _colorPickerDroplet.Closed += ColorPickerDroplet_Closed;
        PositionFlyoutBelowNotch(_colorPickerDroplet);
        _colorPickerDroplet.Show();
        _colorPickerDroplet.Activate();
    }

    private async void QrStudio_Click(object sender, RoutedEventArgs e)
    {
        if (_qrStudioDroplet is not null)
        {
            await CloseQrStudioAsync();
            return;
        }

        _qrStudioDroplet = new QrStudioDroplet { Owner = this };
        _qrStudioDroplet.Closed += QrStudioDroplet_Closed;
        PositionFlyoutBelowNotch(_qrStudioDroplet);
        _qrStudioDroplet.Show();
        _qrStudioDroplet.Activate();
    }

    private async void TextScrubber_Click(object sender, RoutedEventArgs e)
    {
        if (_textScrubberDroplet is not null)
        {
            await CloseTextScrubberAsync();
            return;
        }

        _textScrubberDroplet = new TextScrubberDroplet { Owner = this };
        _textScrubberDroplet.Closed += TextScrubberDroplet_Closed;
        PositionFlyoutBelowNotch(_textScrubberDroplet);
        _textScrubberDroplet.Show();
        _textScrubberDroplet.Activate();
    }

    private async Task CloseDropletsAsync()
    {
        await CloseColorPickerAsync();
        await CloseQrStudioAsync();
        await CloseTextScrubberAsync();
    }

    private async Task CloseColorPickerAsync()
    {
        if (_colorPickerDroplet is not null)
        {
            await _colorPickerDroplet.CloseDropletAsync();
        }
    }

    private async Task CloseQrStudioAsync()
    {
        if (_qrStudioDroplet is not null)
        {
            await _qrStudioDroplet.CloseDropletAsync();
        }
    }

    private async Task CloseTextScrubberAsync()
    {
        if (_textScrubberDroplet is not null)
        {
            await _textScrubberDroplet.CloseDropletAsync();
        }
    }

    private void ColorPickerDroplet_Closed(object? sender, EventArgs e)
    {
        if (sender is ColorPickerDroplet window)
        {
            window.Closed -= ColorPickerDroplet_Closed;
        }

        if (ReferenceEquals(_colorPickerDroplet, sender))
        {
            _colorPickerDroplet = null;
        }
    }

    private void QrStudioDroplet_Closed(object? sender, EventArgs e)
    {
        if (sender is QrStudioDroplet window)
        {
            window.Closed -= QrStudioDroplet_Closed;
        }

        if (ReferenceEquals(_qrStudioDroplet, sender))
        {
            _qrStudioDroplet = null;
        }
    }

    private void TextScrubberDroplet_Closed(object? sender, EventArgs e)
    {
        if (sender is TextScrubberDroplet window)
        {
            window.Closed -= TextScrubberDroplet_Closed;
        }

        if (ReferenceEquals(_textScrubberDroplet, sender))
        {
            _textScrubberDroplet = null;
        }
    }

    private void PositionDroplets()
    {
        if (_colorPickerDroplet is not null)
        {
            PositionFlyoutBelowNotch(_colorPickerDroplet);
        }

        if (_qrStudioDroplet is not null)
        {
            PositionFlyoutBelowNotch(_qrStudioDroplet);
        }

        if (_textScrubberDroplet is not null)
        {
            PositionFlyoutBelowNotch(_textScrubberDroplet);
        }
    }
}
