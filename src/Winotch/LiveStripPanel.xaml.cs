using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Winotch;

public partial class LiveStripPanel : System.Windows.Controls.UserControl
{
    private const double RingSize = 28;
    private const double RingRadius = 12.5;

    public LiveStripPanel()
    {
        InitializeComponent();
        SetActivity(LiveActivity.None);
    }

    public event EventHandler? PauseResumeRequested;
    public event EventHandler? CancelRequested;

    public void SetActivity(LiveActivity activity)
    {
        TitleText.Text = activity.Title;
        SubtitleText.Text = activity.Subtitle;
        TimeText.Text = activity.TimeText;
        ActivityProgress.Value = activity.Progress * 100;
        ActivityProgress.Visibility = activity.Kind is LiveActivityKind.NowPlaying or LiveActivityKind.Timer
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyArtwork(activity);
        ApplyDots(activity.Dots, activity.StackedActivityCount);
        ApplyTimer(activity);
    }

    private void ApplyArtwork(LiveActivity activity)
    {
        ArtworkImage.Source = activity.Kind == LiveActivityKind.NowPlaying
            ? MediaArtwork.FromBytes(activity.Thumbnail)
            : null;
        ArtworkImage.Visibility = ArtworkImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        ArtworkFallback.Visibility = ArtworkImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
        ArtworkFallback.Text = activity.Kind switch
        {
            LiveActivityKind.Call => "\uE720",
            LiveActivityKind.Timer => "\uE916",
            LiveActivityKind.ActivityDots => "\uE7BA",
            _ => "\uE121"
        };
    }

    private void ApplyDots(IReadOnlyList<LiveActivityDot> dots, int stackedActivityCount)
    {
        DotsPanel.Children.Clear();
        foreach (var dot in dots.Take(3))
        {
            DotsPanel.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = BrushFromHex(dot.ColorHex),
                ToolTip = dot.Label
            });
        }

        if (stackedActivityCount > dots.Count)
        {
            DotsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"+{stackedActivityCount - dots.Count}"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("NotchMutedText"),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        DotsPanel.Visibility = DotsPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyTimer(LiveActivity activity)
    {
        var timerActive = activity.Kind == LiveActivityKind.Timer;
        TimerRing.Visibility = timerActive ? Visibility.Visible : Visibility.Collapsed;
        TimerActions.Visibility = timerActive ? Visibility.Visible : Visibility.Collapsed;
        PauseResumeIcon.Text = activity.TimerPaused ? "\uE768" : "\uE769";
        if (timerActive)
        {
            SetTimerRingProgress(activity.Progress);
        }
    }

    private void SetTimerRingProgress(double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);
        if (clamped <= 0)
        {
            TimerRingPath.Data = Geometry.Empty;
            return;
        }

        var angle = 360 * clamped - 90;
        var radians = angle * Math.PI / 180;
        var center = new System.Windows.Point(RingSize / 2, RingSize / 2);
        var start = new System.Windows.Point(center.X, center.Y - RingRadius);
        var end = new System.Windows.Point(
            center.X + RingRadius * Math.Cos(radians),
            center.Y + RingRadius * Math.Sin(radians));
        var largeArc = clamped > 0.5;
        TimerRingPath.Data = new PathGeometry([
            new PathFigure(start, [
                new ArcSegment(end, new System.Windows.Size(RingRadius, RingRadius), 0, largeArc, SweepDirection.Clockwise, isStroked: true)
            ], closed: false)
        ]);
    }

    private static System.Windows.Media.Brush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        PauseResumeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
