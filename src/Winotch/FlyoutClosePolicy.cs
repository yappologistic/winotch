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

        if (!flyout.IsVisible || flyout.IsActive || flyout.Owner?.IsActive == true)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return true;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId != Environment.ProcessId;
    }

    private static bool IsAnyMouseButtonPressed() =>
        IsPressed(0x01) || IsPressed(0x02) || IsPressed(0x04) || IsPressed(0x05) || IsPressed(0x06);

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
}
