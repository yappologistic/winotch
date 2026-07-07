using System.Windows;
using System.Windows.Input;

namespace Winotch;

internal static class FlyoutDragHelper
{
    public static void DragFromHeader(Window window, MouseButtonEventArgs e, Action? markManualPosition = null)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            markManualPosition?.Invoke();
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // WPF throws if the drag is interrupted before the mouse capture starts.
        }
    }
}
