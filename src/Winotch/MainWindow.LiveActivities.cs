using System.Windows;
using System.Windows.Controls;

namespace Winotch;

public partial class MainWindow
{
    private readonly LiveActivityService _liveActivities = new();
    private MediaSnapshot _latestLiveMedia = MediaSnapshot.Empty;
    private PriorityStatusSnapshot? _latestLivePriority;
    private LiveActivity _currentLiveActivity = LiveActivity.None;

    private void InitializeLiveActivities()
    {
        LiveStrip.PauseResumeRequested += LiveStrip_PauseResumeRequested;
        LiveStrip.CancelRequested += LiveStrip_CancelRequested;
        LiveStrip.SetActivity(LiveActivity.None);
    }

    private void ApplyLiveActivityInputs(PriorityStatusSnapshot priorityStatus, MediaSnapshot media)
    {
        _latestLivePriority = priorityStatus;
        _latestLiveMedia = media;
        RefreshLiveActivity(DateTimeOffset.UtcNow, forceShell: false);
    }

    private void RefreshLiveActivityTimer(DateTimeOffset nowUtc)
    {
        RefreshLiveActivity(nowUtc, forceShell: false);
    }

    private void ApplyLiveActivitySettings(LiveActivitySettings settings)
    {
        RefreshLiveActivity(DateTimeOffset.UtcNow, forceShell: true);
    }

    private ShellMode DesiredShellModeForLiveActivity(ShellMode foregroundMode) =>
        foregroundMode == ShellMode.Mini && _currentLiveActivity.Kind != LiveActivityKind.None
            ? ShellMode.Live
            : foregroundMode;

    private void StartTransientLiveTimer_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.Current.LiveActivities.TransientTimerEnabled ||
            sender is not System.Windows.Controls.Button { Tag: string minutesText } ||
            !int.TryParse(minutesText, out var minutes))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _liveActivities.StartTimer(TimeSpan.FromMinutes(minutes), now);
        SetExpanded(false);
        RefreshLiveActivity(now, forceShell: true);
    }

    private void LiveStrip_PauseResumeRequested(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (_currentLiveActivity.TimerPaused)
        {
            _liveActivities.ResumeTimer(now);
        }
        else
        {
            _liveActivities.PauseTimer(now);
        }

        RefreshLiveActivity(now, forceShell: false);
    }

    private void LiveStrip_CancelRequested(object? sender, EventArgs e)
    {
        _liveActivities.CancelTimer();
        RefreshLiveActivity(DateTimeOffset.UtcNow, forceShell: true);
    }

    private void RefreshLiveActivity(DateTimeOffset nowUtc, bool forceShell)
    {
        var privacy = new PrivacyActivitySnapshot(
            _latestLivePriority?.CameraActive == true && !ShouldSuppressCameraAlert,
            _latestLivePriority?.MicrophoneActive == true,
            ScreenShareActive: false);
        var activity = _liveActivities.Update(new LiveActivityInput(
            _settings.Current.LiveActivities,
            privacy,
            _latestLiveMedia,
            nowUtc));
        var previousMode = _currentLiveActivity.ShellMode;
        _currentLiveActivity = activity;
        LiveStrip.SetActivity(activity);

        if (forceShell || previousMode != activity.ShellMode)
        {
            ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true, force: true);
        }
    }
}
