using System.Windows;
using System.Windows.Input;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace Winotch;

public partial class MainWindow
{
    private readonly ShelfService _shelf = new(new ShelfSettings());
    private ShelfFlyout? _shelfFlyout;

    private void InitializeShelf()
    {
        _shelf.Changed += Shelf_Changed;
    }

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

    private void Notch_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = _settings.Current.Shelf.Enabled ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void Notch_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!_settings.Current.Shelf.Enabled)
        {
            return;
        }

        if (_shelf.Stage(_shelf.ReadDrop(e.Data, DateTimeOffset.Now)))
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

    private void ShelfFlyout_Closed(object? sender, EventArgs e)
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

    private void Shelf_Changed(object? sender, EventArgs e)
    {
        if (_shelf.Items.Count == 0)
        {
            _ = CloseShelfAsync();
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
