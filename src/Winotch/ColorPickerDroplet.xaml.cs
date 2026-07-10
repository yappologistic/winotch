using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;

namespace Winotch;

/// <summary>
/// Native WinUI screen-color picker hosted in a Desktop Acrylic overlay.
/// The low-level hook is intentionally retained so a pixel can be sampled from
/// any desktop window, not only from Winotch's own client area.
/// </summary>
public sealed partial class ColorPickerDroplet : FluentWindow
{
    private readonly ColorPickerService _picker;
    private PickedColor _color = new(0, 0, 0);
    private bool _closing;
    private bool _picking;
    private bool _pickQueued;
    private IntPtr _mouseHook;
    private IntPtr _previousCursor;
    private LowLevelMouseProc? _mouseProc;

    public ColorPickerDroplet(ColorPickerService picker)
    {
        InitializeComponent();
        _picker = picker;
        ApplyColor(_color);
        Loaded += Window_Loaded;
        Activated += Window_Activated;
        Closed += Window_Closed;
    }

    public async Task CloseDropletAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        EndScreenPick();
        await AnimateOutAsync();
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateIn();
        PickButton.Focus(FocusState.Programmatic);
    }

    private async void Window_Activated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated || _closing || _picking)
        {
            return;
        }

        if (await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseDropletAsync();
        }
    }

    private async void FlyoutChrome_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        await CloseDropletAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseDropletAsync();

    private void PickButton_Click(object sender, RoutedEventArgs e) => BeginScreenPick();

    private void HeaderDragArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        FlyoutDragHelper.DragFromHeader(this, e);

    private void CopyHexButton_Click(object sender, RoutedEventArgs e) => CopyText(_color.Hex);

    private void CopyRgbButton_Click(object sender, RoutedEventArgs e) => CopyText(_color.RgbText);

    private static void CopyText(string text)
    {
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void ApplyColor(PickedColor color)
    {
        _color = color;
        HexText.Text = color.Hex;
        RgbText.Text = color.RgbText;
        ColorSwatch.Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
    }

    private void BeginScreenPick()
    {
        if (_picking)
        {
            return;
        }

        _picking = true;
        PickButton.Content = "Click color";
        _previousCursor = GetCursor();
        _mouseProc = ScreenPickHook;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
        {
            EndScreenPick();
            return;
        }

        SetCrossCursor();
    }

    private IntPtr ScreenPickHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WmMouseMove)
        {
            SetCrossCursor();
        }

        if (code >= 0 && wParam == WmLButtonDown && !_pickQueued)
        {
            _pickQueued = true;
            var hook = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.High,
                () => FinishScreenPick(hook.Point.X, hook.Point.Y));

            // Swallow the sampling click so the underlying application is not activated.
            return new IntPtr(1);
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void FinishScreenPick(int x, int y)
    {
        if (!_picking)
        {
            return;
        }

        ApplyColor(_picker.PickScreenPixel(x, y));
        EndScreenPick();
        Activate();
        PickButton.Focus(FocusState.Programmatic);
    }

    private void EndScreenPick()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_previousCursor != IntPtr.Zero)
        {
            _ = SetCursor(_previousCursor);
            _previousCursor = IntPtr.Zero;
        }

        _mouseProc = null;
        _picking = false;
        _pickQueued = false;
        if (PickButton is not null)
        {
            PickButton.Content = "Pick";
        }
    }

    private static void SetCrossCursor()
    {
        var cursor = LoadCursor(IntPtr.Zero, new IntPtr(IdcCross));
        if (cursor != IntPtr.Zero)
        {
            _ = SetCursor(cursor);
        }
    }

    private void AnimateIn()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        visual.CenterPoint = new Vector3(
            (float)(FlyoutChrome.ActualWidth / 2),
            0,
            0);
        visual.Opacity = 0;
        visual.Scale = new Vector3(0.96f, 0.96f, 1);

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1),
            new Vector2(0.3f, 1));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = ShellAnimationTiming.FadeDuration;
        fade.InsertKeyFrame(1, 1, easing);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.MotionDuration;
        scale.InsertKeyFrame(1, Vector3.One, easing);
        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Scale), scale);
    }

    private async Task AnimateOutAsync()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.7f, 0),
            new Vector2(0.84f, 0));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = ShellAnimationTiming.FadeDuration;
        fade.InsertKeyFrame(1, 0, easing);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = ShellAnimationTiming.FadeDuration;
        scale.InsertKeyFrame(1, new Vector3(0.96f, 0.96f, 1), easing);
        visual.StartAnimation(nameof(visual.Opacity), fade);
        visual.StartAnimation(nameof(visual.Scale), scale);
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    private void Window_Closed(object sender, WindowEventArgs e)
    {
        EndScreenPick();
        Loaded -= Window_Loaded;
        Activated -= Window_Activated;
        Closed -= Window_Closed;
    }

    private const int WhMouseLl = 14;
    private const int IdcCross = 32515;
    private static readonly IntPtr WmMouseMove = new(0x0200);
    private static readonly IntPtr WmLButtonDown = new(0x0201);

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookStruct
    {
        public readonly MousePoint Point;
        private readonly uint _mouseData;
        private readonly uint _flags;
        private readonly uint _time;
        private readonly IntPtr _extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MousePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCursor();

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
}
