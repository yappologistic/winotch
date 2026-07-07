using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Winotch;

public partial class LiveStripPanel : System.Windows.Controls.UserControl
{
    public LiveStripPanel()
    {
        InitializeComponent();
        SetActivity(LiveActivity.None);
    }

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

    private static System.Windows.Media.Brush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
