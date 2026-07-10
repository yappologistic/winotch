using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Winotch;

public partial class LiveStripPanel : UserControl
{
    private long _artworkRequest;

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

        var artworkRequest = ++_artworkRequest;
        PrepareArtwork(activity);
        _ = ApplyArtworkAsync(activity, artworkRequest);
        ApplyDots(activity.Dots, activity.StackedActivityCount);
    }

    private void PrepareArtwork(LiveActivity activity)
    {
        ArtworkImage.Source = null;
        ArtworkImage.Visibility = Visibility.Collapsed;
        ArtworkFallback.Visibility = Visibility.Visible;
        ArtworkFallback.Text = activity.Kind switch
        {
            LiveActivityKind.Call => "\uE720",
            LiveActivityKind.Timer => "\uE916",
            LiveActivityKind.ActivityDots => "\uE7BA",
            _ => "\uE121"
        };
    }

    private async Task ApplyArtworkAsync(LiveActivity activity, long artworkRequest)
    {
        if (activity.Kind != LiveActivityKind.NowPlaying)
        {
            return;
        }

        var source = await MediaArtwork.FromBytesAsync(activity.Thumbnail);
        if (artworkRequest != _artworkRequest)
        {
            return;
        }

        ArtworkImage.Source = source;
        ArtworkImage.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        ArtworkFallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyDots(IReadOnlyList<LiveActivityDot> dots, int stackedActivityCount)
    {
        DotsPanel.Children.Clear();
        foreach (var dot in dots.Take(3))
        {
            var ellipse = new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = BrushFromHex(dot.ColorHex)
            };
            ToolTipService.SetToolTip(ellipse, dot.Label);
            DotsPanel.Children.Add(ellipse);
        }

        if (stackedActivityCount > dots.Count)
        {
            DotsPanel.Children.Add(new TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"+{stackedActivityCount - dots.Count}"),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["NotchMutedText"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        DotsPanel.Visibility = DotsPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush BrushFromHex(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        var hasAlpha = value.Length == 8;
        var alpha = hasAlpha ? (byte)(rgba >> 24) : (byte)255;
        var red = (byte)(rgba >> 16);
        var green = (byte)(rgba >> 8);
        var blue = (byte)rgba;
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
