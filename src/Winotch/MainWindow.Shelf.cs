using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Winotch;

public partial class MainWindow
{
    private readonly ShelfService _shelf = new(new ShelfSettings());
    private ShelfFlyout? _shelfFlyout;
    private NativeDropTarget? _nativeShelfDropTarget;

    private void InitializeNativeShelfDropTarget()
    {
        _nativeShelfDropTarget ??= NativeDropTarget.Attach(this, HandleNativeShelfDropAsync);
        if (_nativeShelfDropTarget is null)
        {
            Debug.WriteLine("The native notch drop target could not be initialized.");
            Title = "Winotch - shelf drop unavailable";
        }
    }

    private async Task HandleNativeShelfDropAsync(NativeDropPayload payload)
    {
        if (_settings.Current.Shelf.Enabled &&
            await _shelf.StageNativeDropAsync(payload, DateTimeOffset.Now))
        {
            ShowShelfFlyout();
        }
    }

    private void ApplyShelfSettings(ShelfSettings settings)
    {
        _shelf.ApplySettings(settings);
        ShelfButton.Visibility = settings.Enabled ? Visibility.Visible : Visibility.Collapsed;
        ShellHost.AllowDrop = settings.Enabled;
        SetMouseTransparent(_currentShellMode == ShellMode.FullBar && !settings.Enabled);
        if (!settings.Enabled)
        {
            _ = CloseShelfAsync();
        }
    }

    private void Notch_DragOver(object sender, DragEventArgs e)
    {
        ConfigureNotchDrag(e);
    }

    private void Notch_DragEnter(object sender, DragEventArgs e) => ConfigureNotchDrag(e);

    private void Notch_DragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private async void Notch_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_settings.Current.Shelf.Enabled)
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            if (await _shelf.StageDropAsync(e.DataView, DateTimeOffset.Now))
            {
                ShowShelfFlyout();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            Debug.WriteLine($"Unable to add dropped content to the shelf: {ex}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ConfigureNotchDrag(DragEventArgs e)
    {
        e.AcceptedOperation = _settings.Current.Shelf.Enabled &&
            ShelfService.SupportsDropFormats(e.DataView.AvailableFormats)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        if (e.AcceptedOperation == DataPackageOperation.Copy)
        {
            e.DragUIOverride.Caption = "Add to Shelf";
            e.DragUIOverride.IsCaptionVisible = true;
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
            _shelfFlyout = new ShelfFlyout(_shelf);
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

    private async Task RunShelfSmokeTestAsync()
    {
        await Task.Delay(400);
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        package.SetText("Shelf smoke-test item");
        _ = await _shelf.StageDropAsync(package.GetView(), DateTimeOffset.Now);
        ShowShelfFlyout();

        await Task.Delay(700);
        SetNotchPaused(true);
        await Task.Delay(700);
        SetNotchPaused(false);
        await Task.Delay(700);
        ExitFromTray();
    }
}
