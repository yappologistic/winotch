using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Input;

namespace Winotch;

/// <summary>
/// Starts the native Windows caption-drag loop for borderless WinUI flyouts.
/// </summary>
internal static class FlyoutDragHelper
{
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    public static void DragFromHeader(
        FluentWindow window,
        PointerRoutedEventArgs e,
        Action? markManualPosition = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(e);

        var point = e.GetCurrentPoint(null);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        markManualPosition?.Invoke();
        e.Handled = true;
        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
}
