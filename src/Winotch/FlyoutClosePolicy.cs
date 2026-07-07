using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Winotch;

internal static class FlyoutClosePolicy
{
    private static readonly TimeSpan DeactivationSettleDelay = TimeSpan.FromMilliseconds(200);

    public static async Task<bool> ShouldCloseAfterDeactivationAsync(Window flyout)
    {
        var deactivatedByMouseClick = IsAnyMouseButtonPressed();
        if (!deactivatedByMouseClick)
        {
            // Notch collapse and owner geometry changes can deactivate owned flyouts without an outside click.
            return false;
        }

        await Task.Delay(DeactivationSettleDelay);

        if (!flyout.IsVisible || flyout.IsActive || flyout.Owner?.IsActive == true)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return true;
        }

        GetWindowThreadProcessId(foreground, out var processId);
        return processId != Environment.ProcessId;
    }

    private static bool IsAnyMouseButtonPressed() =>
        Mouse.LeftButton == MouseButtonState.Pressed ||
        Mouse.RightButton == MouseButtonState.Pressed ||
        Mouse.MiddleButton == MouseButtonState.Pressed ||
        Mouse.XButton1 == MouseButtonState.Pressed ||
        Mouse.XButton2 == MouseButtonState.Pressed;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}
