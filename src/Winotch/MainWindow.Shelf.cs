using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Winotch;

public partial class MainWindow
{
    private readonly ShelfService _shelf = new(new ShelfSettings());
    private ShelfFlyout? _shelfFlyout;

    private void ApplyShelfSettings(ShelfSettings settings)
    {
        _shelf.ApplySettings(settings);
        ShelfButton.Visibility = settings.Enabled ? Visibility.Visible : Visibility.Collapsed;
        NotchShell.AllowDrop = settings.Enabled;
        if (!settings.Enabled)
        {
            _ = CloseShelfAsync();
        }
    }

    private void Notch_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _settings.Current.Shelf.Enabled
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void Notch_Drop(object sender, DragEventArgs e)
    {
        if (!_settings.Current.Shelf.Enabled)
        {
            return;
        }

        var item = await _shelf.ReadDropAsync(e.DataView, DateTimeOffset.Now);
        if (_shelf.Stage(item))
        {
            ShowShelfFlyout();
        }

        e.Handled = true;
    }

    private async void ShelfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shelfFlyout is not null)
        {
            await CloseShelfAsync();
            return;
        }

        ShowShelfFlyout();
    }

    private void ShowShelfFlyout()
    {
        if (_shelfFlyout is null)
        {
            _shelfFlyout = new ShelfFlyout(_shelf) { Owner = this };
            _shelfFlyout.Closed += ShelfFlyout_Closed;
            PositionShelf();
            _shelfFlyout.Show();
        }

        _shelfFlyout.Activate();
    }

    private async Task CloseShelfAsync()
    {
        var window = _shelfFlyout;
        if (window is not null)
        {
            await window.CloseShelfAsync();
        }
    }

    private void ShelfFlyout_Closed(object sender, WindowEventArgs e)
    {
        if (sender is ShelfFlyout window)
        {
            window.Closed -= ShelfFlyout_Closed;
        }

        if (ReferenceEquals(_shelfFlyout, sender))
        {
            _shelfFlyout = null;
        }
    }

    private void PositionShelf()
    {
        if (_shelfFlyout is null || _shelfFlyout.HasManualPosition)
        {
            return;
        }

        PositionFlyoutBelowNotch(_shelfFlyout);
    }
}
