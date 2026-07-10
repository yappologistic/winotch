using Microsoft.UI.Xaml;

namespace Winotch;

public partial class MainWindow
{
    private void ApplyShelfAndDropletSettings(WinotchSettings settings)
    {
        ApplyShelfSettings(settings.Shelf);
        ApplyDropletSettings(settings.Droplets);
    }

    private async Task CloseShelfAndDropletsAsync()
    {
        await CloseShelfAsync();
        await CloseDropletsAsync();
    }

    private void PositionShelfAndDroplets()
    {
        PositionShelf();
        PositionDroplets();
    }

    private void PositionFlyoutBelowNotch(FluentWindow flyout)
    {
        var left = Left + (Width - flyout.Width) / 2;
        var shellHeight = NotchShell.ActualHeight > 0 ? NotchShell.ActualHeight : NotchShell.Height;
        var top = Top + shellHeight + 8;
        var monitor = CurrentMonitor();
        var minLeft = monitor.WorkAreaLeftDip + 8;
        var maxLeft = monitor.WorkAreaRightDip - flyout.Width - 8;
        var minTop = monitor.WorkAreaTopDip + 8;
        var maxTop = monitor.WorkAreaBottomDip - flyout.Height - 8;
        flyout.MoveToAtScale(
            ClampToRange(left, minLeft, maxLeft),
            ClampToRange(top, minTop, maxTop),
            monitor.DpiScaleX);
    }
}
