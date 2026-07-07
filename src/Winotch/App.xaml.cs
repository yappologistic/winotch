using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Winotch;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\Winotch.SingleInstance";
    private static readonly TimeSpan ScrollBarVisibleDuration = TimeSpan.FromMilliseconds(900);
    private static readonly DependencyProperty ScrollBarHideTimerProperty =
        DependencyProperty.RegisterAttached(
            "ScrollBarHideTimer",
            typeof(DispatcherTimer),
            typeof(App),
            new PropertyMetadata(null));
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryAcquireSingleInstance(SingleInstanceMutexName, out _singleInstanceMutex))
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    public static bool TryAcquireSingleInstance(string mutexName, out Mutex? mutex)
    {
        mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        mutex.Dispose();
        mutex = null;
        return false;
    }

    private void AutoHideScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            SetScrollBarsOpacity(viewer, 0);
        }
    }

    private void AutoHideScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || (e.VerticalChange == 0 && e.HorizontalChange == 0))
        {
            return;
        }

        SetScrollBarsOpacity(viewer, 1);
        var timer = (DispatcherTimer?)viewer.GetValue(ScrollBarHideTimerProperty);
        if (timer is null)
        {
            timer = new DispatcherTimer { Interval = ScrollBarVisibleDuration };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SetScrollBarsOpacity(viewer, 0);
            };
            viewer.SetValue(ScrollBarHideTimerProperty, timer);
        }

        timer.Stop();
        timer.Start();
    }

    private static void SetScrollBarsOpacity(DependencyObject root, double opacity)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is WpfScrollBar scrollBar)
            {
                scrollBar.Opacity = opacity;
            }

            SetScrollBarsOpacity(child, opacity);
        }
    }
}
