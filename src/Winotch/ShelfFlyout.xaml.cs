using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Winotch;

public sealed record ShelfRow(
    ShelfItem Item,
    string Glyph,
    string Preview,
    ImageSource? Icon,
    BitmapImage? Thumbnail)
{
    public bool CanOpen => Item.Kind is ShelfItemKind.Files or ShelfItemKind.Link;
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PreviewVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThumbnailVisibility => Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// Native WinUI shelf surface. The window uses Desktop Acrylic while the content
/// layers and controls consume the shared Winotch Fluent design tokens.
/// </summary>
public sealed partial class ShelfFlyout : FluentWindow
{
    private readonly ShelfService _shelf;
    private InMemoryRandomAccessStream? _activeDragImageStream;
    private int _refreshVersion;
    private bool _closing;

    public ShelfFlyout(ShelfService shelf)
    {
        ArgumentNullException.ThrowIfNull(shelf);

        InitializeComponent();
        _shelf = shelf;
        _shelf.Changed += Shelf_Changed;

        Width = 430;
        Height = 310;
        ShowInTaskbar = false;
        Topmost = true;
        BottomCornerRadius = 24;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        _ = RefreshAsync();
    }

    public bool HasManualPosition { get; private set; }

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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FlyoutChrome.Focus(FocusState.Programmatic);
        AnimateIn();
    }

    private void Shelf_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = ShelfService.SupportsDropFormats(e.DataView.AvailableFormats)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        if (e.AcceptedOperation == DataPackageOperation.Copy)
        {
            e.DragUIOverride.Caption = "Add to Shelf";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        e.Handled = true;
    }

    private async void Shelf_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            _ = await _shelf.StageDropAsync(e.DataView, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            Debug.WriteLine($"Unable to add dropped content to the shelf: {ex}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void FlyoutChrome_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            await CloseShelfAsync();
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await CloseShelfAsync();

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

    private void HeaderDragArea_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        FlyoutDragHelper.DragFromHeader(this, e, () => HasManualPosition = true);

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShelfItem item })
        {
            return;
        }

        await CopyToClipboardAsync(item);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShelfItem item })
        {
            return;
        }

        foreach (var target in ShelfLaunchTargets.For(item))
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ShelfItem item })
        {
            _shelf.Remove(item.Id);
        }
    }

    private async void ShelfRow_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: ShelfRow row })
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            _activeDragImageStream?.Dispose();
            _activeDragImageStream = null;
            args.AllowedOperations = DataPackageOperation.Copy;
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            _activeDragImageStream = await PopulateDataPackageAsync(args.Data, row.Item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"Unable to begin shelf drag: {ex}");
            args.Cancel = true;
            _activeDragImageStream?.Dispose();
            _activeDragImageStream = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShelfRow_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        _activeDragImageStream?.Dispose();
        _activeDragImageStream = null;
    }

    private void Shelf_Changed(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = RefreshAsync();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => _ = RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var items = _shelf.Items;
        var rows = await Task.WhenAll(items.Select(CreateRowAsync));
        if (_closing || version != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        ShelfList.ItemsSource = rows;
        var isEmpty = rows.Length == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        ShelfList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = !isEmpty;
        ItemCountText.Text = isEmpty ? "No staged items" : $"{rows.Length} staged item{(rows.Length == 1 ? string.Empty : "s")}";
    }

    private static async Task<ShelfRow> CreateRowAsync(ShelfItem item)
    {
        var iconTask = item.Kind == ShelfItemKind.Files
            ? ShellIconService.LoadSmallIconAsync(item.FilePaths.FirstOrDefault())
            : Task.FromResult<ImageSource?>(null);
        var thumbnailTask = ClipboardThumbnail.ToBitmapSourceAsync(item.ThumbnailPng);
        await Task.WhenAll(iconTask, thumbnailTask);

        return new ShelfRow(
            item,
            GlyphFor(item.Kind),
            item.Preview,
            await iconTask,
            await thumbnailTask);
    }

    private static async Task CopyToClipboardAsync(ShelfItem item)
    {
        var data = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };

        InMemoryRandomAccessStream? imageStream = null;
        try
        {
            imageStream = await PopulateDataPackageAsync(data, item);
            var options = new ClipboardContentOptions
            {
                IsAllowedInHistory = false,
                IsRoamable = false
            };

            if (!Clipboard.SetContentWithOptions(data, options))
            {
                throw new InvalidOperationException("The clipboard is currently unavailable.");
            }

            // Flush detaches the clipboard payload before the temporary image stream is released.
            Clipboard.Flush();
        }
        finally
        {
            imageStream?.Dispose();
        }
    }

    private static async Task<InMemoryRandomAccessStream?> PopulateDataPackageAsync(
        DataPackage data,
        ShelfItem item)
    {
        // Both markers preserve the existing in-process exclusion and the standard
        // Windows privacy intent for apps that inspect clipboard history eligibility.
        data.SetData(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);
        data.SetData(
            ClipboardPrivacyPolicy.CanIncludeInClipboardHistory,
            CryptographicBuffer.CreateFromByteArray(BitConverter.GetBytes(0)));

        switch (item.Kind)
        {
            case ShelfItemKind.Text:
            case ShelfItemKind.Link:
                data.SetText(item.Text ?? item.Preview);
                return null;
            case ShelfItemKind.Files:
                data.SetStorageItems(await ResolveStorageItemsAsync(item.FilePaths));
                return null;
            case ShelfItemKind.Image:
                var stream = await ClipboardThumbnail.ToRandomAccessStreamAsync(
                    item.ThumbnailPng
                    ?? throw new InvalidOperationException("The staged image thumbnail is missing."));
                data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                return stream;
            default:
                throw new InvalidOperationException("Unsupported shelf item.");
        }
    }

    private static async Task<IReadOnlyList<IStorageItem>> ResolveStorageItemsAsync(
        IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                items.Add(await StorageFile.GetFileFromPathAsync(path));
            }
            else if (Directory.Exists(path))
            {
                items.Add(await StorageFolder.GetFolderFromPathAsync(path));
            }
            else
            {
                throw new FileNotFoundException("A shelf item no longer exists.", path);
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("A file shelf item must contain at least one path.");
        }

        return items;
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
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        visual.CenterPoint = new Vector3(
            (float)(FlyoutChrome.ActualWidth / 2),
            (float)(FlyoutChrome.ActualHeight / 2),
            0);
        visual.Opacity = 0;
        visual.Scale = new Vector3(0.96f, 0.96f, 1);

        var easing = CreateFluentEasing(visual.Compositor);
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 1, easing);
        opacity.Duration = ShellAnimationTiming.FadeDuration;

        var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1, Vector3.One, easing);
        scale.Duration = ShellAnimationTiming.MotionDuration;
        visual.StartAnimation(nameof(visual.Opacity), opacity);
        visual.StartAnimation(nameof(visual.Scale), scale);
    }

    private async Task AnimateOutAsync()
    {
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutChrome);
        var easing = CreateFluentEasing(visual.Compositor);
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 0, easing);
        opacity.Duration = ShellAnimationTiming.FadeDuration;

        var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1, new Vector3(0.96f, 0.96f, 1), easing);
        scale.Duration = ShellAnimationTiming.FadeDuration;
        visual.StartAnimation(nameof(visual.Opacity), opacity);
        visual.StartAnimation(nameof(visual.Scale), scale);
        await Task.Delay(ShellAnimationTiming.FadeDuration);
    }

    private static CompositionEasingFunction CreateFluentEasing(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1));

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _shelf.Changed -= Shelf_Changed;
        _activeDragImageStream?.Dispose();
        _activeDragImageStream = null;
    }
}
