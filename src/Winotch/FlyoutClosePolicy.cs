using System.Runtime.InteropServices;

namespace Winotch;

internal static class FlyoutClosePolicy
{
    private static readonly TimeSpan DeactivationSettleDelay = TimeSpan.FromMilliseconds(200);

    public static async Task<bool> ShouldCloseAfterDeactivationAsync(FluentWindow flyout)
    {
        if (!IsAnyMouseButtonPressed())
        {
            // A notch morph or owner geometry update can briefly deactivate an
            // owned surface without the user dismissing it.
            return false;
        }

        await Task.Delay(DeactivationSettleDelay);

        if (!flyout.IsVisible || flyout.IsActive)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return true;
        }

        var flyoutHandle = WinRT.Interop.WindowNative.GetWindowHandle(flyout);
        return foreground != flyoutHandle && !IsOwnedBy(foreground, flyoutHandle);
    }

    private static bool IsOwnedBy(IntPtr window, IntPtr potentialOwner)
    {
        if (potentialOwner == IntPtr.Zero)
        {
            return false;
        }

        for (var owner = GetWindow(window, GwOwner); owner != IntPtr.Zero; owner = GetWindow(owner, GwOwner))
        {
            if (owner == potentialOwner)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnyMouseButtonPressed() =>
        IsPressed(0x01) || IsPressed(0x02) || IsPressed(0x04) || IsPressed(0x05) || IsPressed(0x06);

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const uint GwOwner = 4;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
}
