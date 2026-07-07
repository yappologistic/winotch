using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace Winotch;

public sealed record ShelfRow(ShelfItem Item, string Glyph, string Preview, BitmapSource? Thumbnail)
{
    public bool HasThumbnail => Thumbnail is not null;
}

public partial class ShelfFlyout : Window
{
    private static readonly Duration FadeDuration = new(ShellAnimationTiming.FadeDuration);
    private static readonly Duration MotionDuration = new(ShellAnimationTiming.MotionDuration);
    private static readonly IEasingFunction Easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private readonly ShelfService _shelf;
    private bool _closing;

    public ShelfFlyout(ShelfService shelf)
    {
        InitializeComponent();
        _shelf = shelf;
        _shelf.Changed += Shelf_Changed;
        Refresh();
    }

    public async Task CloseShelfAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        await AnimateOutAsync();
        Close();
    }

    public bool HasManualPosition { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e) => AnimateIn();

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closing && await FlyoutClosePolicy.ShouldCloseAfterDeactivationAsync(this))
        {
            await CloseShelfAsync();
        }
    }

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CloseShelfAsync();
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseShelfAsync();

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

    private void HeaderDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        HasManualPosition = true;
        DragMove();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: ShelfItem item })
        {
            CopyToClipboard(item);
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ShelfItem item })
        {
            return;
        }

        var target = item.Kind == ShelfItemKind.Files ? item.FilePaths.FirstOrDefault() : item.Kind == ShelfItemKind.Link ? item.Text : null;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: ShelfItem item })
        {
            _shelf.Remove(item.Id);
        }
    }

    private void ShelfRow_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement { DataContext: ShelfRow row })
        {
            DragDrop.DoDragDrop(this, ToDataObject(row.Item), WpfDragDropEffects.Copy);
        }
    }

    private void Shelf_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var rows = _shelf.Items.Select(item => new ShelfRow(item, GlyphFor(item.Kind), item.Preview, ClipboardThumbnail.ToBitmapSource(item.ThumbnailPng))).ToArray();
        ShelfList.ItemsSource = rows;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void CopyToClipboard(ShelfItem item) => WpfClipboard.SetDataObject(ToDataObject(item), copy: true);

    private static System.Windows.DataObject ToDataObject(ShelfItem item)
    {
        var data = new System.Windows.DataObject();
        data.SetData(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);
        data.SetData(ClipboardPrivacyPolicy.CanIncludeInClipboardHistory, BitConverter.GetBytes(0));
        switch (item.Kind)
        {
            case ShelfItemKind.Text:
            case ShelfItemKind.Link:
                data.SetText(item.Text ?? item.Preview);
                break;
            case ShelfItemKind.Files:
                var files = new StringCollection();
                files.AddRange(item.FilePaths.ToArray());
                data.SetFileDropList(files);
                break;
            case ShelfItemKind.Image:
                var image = ClipboardThumbnail.ToBitmapSource(item.ThumbnailPng);
                if (image is not null)
                {
                    data.SetImage(image);
                }

                break;
        }

        return data;
    }

    private static string GlyphFor(ShelfItemKind kind) => kind switch
    {
        ShelfItemKind.Link => "\uE71B",
        ShelfItemKind.Files => "\uE8B7",
        ShelfItemKind.Image => "\uEB9F",
        _ => "\uE8D2"
    };

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
        _shelf.Changed -= Shelf_Changed;
        base.OnClosed(e);
    }
}
