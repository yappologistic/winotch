using System.Threading;
using Microsoft.UI.Xaml;

namespace Winotch;

/// <summary>
/// WinUI 3 application lifetime. Winotch remains a single-process, local-only
/// desktop utility; closing through the tray releases the named mutex.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Winotch.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryAcquireSingleInstance(SingleInstanceMutexName, out _singleInstanceMutex))
        {
            Exit();
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => ReleaseSingleInstance();
        _mainWindow.ShowWithoutActivation();
    }

    internal void RequestExit()
    {
        _mainWindow?.Close();
        ReleaseSingleInstance();
        Exit();
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

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The mutex may already have been released by an explicit tray exit.
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }
}
