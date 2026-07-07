using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;

namespace Winotch;

public partial class ColorPickerDroplet : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private readonly ColorPickerService _picker;
    private PickedColor _color = new(0, 0, 0);
    private bool _closing;
    private bool _picking;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _mouseProc;

    public ColorPickerDroplet(ColorPickerService picker)
    {
        InitializeComponent();
        _picker = picker;
        ApplyColor(_color);
    }

    public async Task CloseDropletAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await AnimateOutAsync();
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnimateIn();

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing && !_picking && await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseDropletAsync();
        }
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CloseDropletAsync();
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseDropletAsync();

    private void PickButton_Click(object sender, RoutedEventArgs e)
    {
        BeginScreenPick();
    }

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => FlyoutDragHelper.DragFromHeader(this, e);

    private void CopyHexButton_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(_color.Hex);

    private void CopyRgbButton_Click(object sender, RoutedEventArgs e) => WpfClipboard.SetText(_color.RgbText);

    private void ApplyColor(PickedColor color)
    {
        _color = color;
        HexText.Text = color.Hex;
        RgbText.Text = color.RgbText;
        ColorSwatch.Background = new SolidColorBrush(WpfColor.FromRgb(color.R, color.G, color.B));
    }

    private void BeginScreenPick()
    {
        if (_picking)
        {
            return;
        }

        _picking = true;
        PickButton.Content = "Click color";
        Cursor = System.Windows.Input.Cursors.Cross;
        _mouseProc = ScreenPickHook;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, IntPtr.Zero, 0);
    }

    private IntPtr ScreenPickHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WmLButtonDown)
        {
            var hook = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            Dispatcher.BeginInvoke(() => FinishScreenPick(hook.Point.X, hook.Point.Y), DispatcherPriority.Send);
            return new IntPtr(1);
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void FinishScreenPick(int x, int y)
    {
        ApplyColor(_picker.PickScreenPixel(x, y));
        EndScreenPick();
        Activate();
    }

    private void EndScreenPick()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _mouseProc = null;
        _picking = false;
        PickButton.Content = "Pick";
        Cursor = null;
    }

    private void AnimateIn()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, MotionDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, MotionDuration) { EasingFunction = Easing });
    }

    private async Task AnimateOutAsync()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, FadeDuration) { EasingFunction = Easing });
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, FadeDuration) { EasingFunction = Easing });
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    protected override void OnClosed(EventArgs e)
    {
        EndScreenPick();
        base.OnClosed(e);
    }

    private const int WhMouseLl = 14;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
}
