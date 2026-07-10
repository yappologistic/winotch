using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>
/// Owns a Win32 window subclass and keeps its native callback alive for the
/// lifetime of the registration. This replaces WPF's HwndSource hook for
/// messages that WinUI 3 does not surface through XAML events.
/// </summary>
internal sealed class WindowMessageSink : IDisposable
{
    internal delegate bool MessageHandler(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        out IntPtr result);

    private const uint WmNcDestroy = 0x0082;
    private static long s_nextSubclassId;

    private readonly IntPtr _windowHandle;
    private readonly UIntPtr _subclassId;
    private readonly SubclassProc _subclassProc;
    private readonly MessageHandler _handler;
    private GCHandle _selfHandle;
    private bool _attached;

    public WindowMessageSink(IntPtr windowHandle, MessageHandler handler)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid HWND is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _subclassId = new UIntPtr(unchecked((ulong)Interlocked.Increment(ref s_nextSubclassId)));
        _subclassProc = WndProc;
        _selfHandle = GCHandle.Alloc(this);
        var referenceData = new UIntPtr(
            unchecked((ulong)GCHandle.ToIntPtr(_selfHandle).ToInt64()));
        _attached = SetWindowSubclass(_windowHandle, _subclassProc, _subclassId, referenceData);
        if (!_attached)
        {
            _selfHandle.Free();
            throw new InvalidOperationException(
                $"Unable to subclass window 0x{_windowHandle.ToInt64():X}.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    private IntPtr WndProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        var handled = false;
        var result = IntPtr.Zero;
        try
        {
            handled = _handler(windowHandle, message, wParam, lParam, out result);
        }
        catch (Exception exception)
        {
            // Exceptions cannot cross the native callback boundary. Report the
            // failure and let the normal window procedure handle the message.
            Debug.WriteLine($"Winotch window-message handler failed: {exception}");
        }

        if (message == WmNcDestroy)
        {
            Detach(windowIsBeingDestroyed: true);
        }

        return handled && message != WmNcDestroy
            ? result
            : DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void Detach(bool windowIsBeingDestroyed = false)
    {
        if (!_attached)
        {
            return;
        }

        if (!RemoveWindowSubclass(_windowHandle, _subclassProc, _subclassId) &&
            !windowIsBeingDestroyed)
        {
            // Keeping the GCHandle alive is safer than allowing the CLR callback
            // to be collected while native code may still reference it.
            return;
        }

        _attached = false;
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);
}
