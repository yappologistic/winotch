using System.IO;
using System.Windows;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Winotch;

public partial class MainWindow
{
    private const int ShelfVisibleTileLimit = 10;
    private const string ShelfDragMarker = "WinotchShelfDrag";
    private readonly FileShelfStore _fileShelfStore = new();
    private readonly ShellFileIconService _fileShelfIcons = new();
    private FileShelf _fileShelf = new();
    private ShellMode _currentShellMode = ShellMode.Mini;
    private CancellationTokenSource? _fileDropLeave;
    private bool _draggingShelfOut;
    private bool _fileDropInside;
    private bool _fileDropPreviewVisible;
    private Point _shelfDragStart;
    private string[] _shelfDragPaths = [];

    private void InitializeFileShelf()
    {
        ShelfItemsControl.ItemsSource = Array.Empty<ShelfTileViewModel>();
        UpdateShelfView();
    }

    private async Task LoadFileShelfAsync()
    {
        _fileShelf = await _fileShelfStore.LoadAsync();
        UpdateShelfView();
    }

    private void DisposeFileShelf()
    {
        _fileDropLeave?.Cancel();
        _fileDropLeave?.Dispose();
    }

    private void UpdateShelfView()
    {
        var snapshot = _fileShelf.CreateSnapshot(ShelfVisibleTileLimit);
        var tiles = snapshot.Tiles
            .Select(tile => new ShelfTileViewModel(
                tile.FullPath,
                tile.DisplayName,
                tile.Exists,
                _fileShelfIcons.GetIcon(tile)))
            .ToArray();

        ShelfItemsControl.ItemsSource = tiles;
        ShelfEmptyText.Visibility = snapshot.HasItems ? Visibility.Collapsed : Visibility.Visible;
        ShelfOverflowBadge.Visibility = snapshot.OverflowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfOverflowText.Text = $"+{snapshot.OverflowCount}";
        ShelfSectionCountText.Text = snapshot.TotalCount.ToString();
        ShelfClearButton.IsEnabled = snapshot.HasItems;
        ShelfDragAllButton.IsEnabled = tiles.Any(tile => tile.Exists);
        UpdateShelfMiniIndicator();
    }

    private void UpdateShelfMiniIndicator()
    {
        var visible = _fileShelf.Count > 0 &&
            !_expanded &&
            !_compactToastVisible &&
            _currentShellMode == ShellMode.Mini;
        ShelfMiniIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ShelfMiniCountText.Text = _fileShelf.Count > 9 ? "9+" : _fileShelf.Count.ToString();
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryAcceptExternalFileDrop(e, out var paths))
        {
            return;
        }

        EndShelfDropPreview(collapseIfPointerAway: true);
        if (_fileShelf.Add(paths))
        {
            UpdateShelfView();
            await SaveFileShelfAsync();
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e) => Window_DragOver(sender, e);

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (TryAcceptExternalFileDrop(e, out _))
        {
            BeginShelfDropPreview();
        }
    }

    private async void Window_DragLeave(object sender, DragEventArgs e)
    {
        _fileDropInside = false;
        _fileDropLeave?.Cancel();
        _fileDropLeave?.Dispose();
        _fileDropLeave = new CancellationTokenSource();
        var token = _fileDropLeave.Token;

        try
        {
            await Task.Delay(180, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_fileDropInside)
        {
            EndShelfDropPreview(collapseIfPointerAway: true);
        }
    }

    private void BeginShelfDropPreview()
    {
        _fileDropInside = true;
        _fileDropLeave?.Cancel();
        _collapseTimer.Stop();

        SetExpanded(true);
        if (_fileDropPreviewVisible)
        {
            return;
        }

        _fileDropPreviewVisible = true;
        ShelfDropOverlay.Opacity = 0;
        ShellAnimator.Show(ShelfDropOverlay, _animationFrameRate);
    }

    private void EndShelfDropPreview(bool collapseIfPointerAway)
    {
        _fileDropInside = false;
        if (_fileDropPreviewVisible)
        {
            _fileDropPreviewVisible = false;
            ShellAnimator.Hide(ShelfDropOverlay);
        }

        if (collapseIfPointerAway && !IsMouseOver)
        {
            SetExpanded(false);
        }
    }

    private bool TryAcceptExternalFileDrop(DragEventArgs e, out string[] paths)
    {
        paths = [];
        if (_draggingShelfOut || e.Data.GetDataPresent(ShelfDragMarker))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return false;
        }

        paths = GetFileDropPaths(e.Data);
        if (paths.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return false;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        return true;
    }

    private static string[] GetFileDropPaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] paths
                ? paths
                : [];

    private async void ShelfRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !_fileShelf.Remove(path))
        {
            return;
        }

        UpdateShelfView();
        await SaveFileShelfAsync();
    }

    private async void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_fileShelf.Clear())
        {
            return;
        }

        UpdateShelfView();
        await SaveFileShelfAsync();
    }

    private void ShelfItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShelfTileViewModel { Exists: true } tile })
        {
            PrepareShelfDrag(e.GetPosition(this), [tile.FullPath]);
        }
        else
        {
            PrepareShelfDrag(e.GetPosition(this), []);
        }
    }

    private void ShelfDragAll_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        PrepareShelfDrag(e.GetPosition(this), ExistingShelfPaths());

    private void ShelfDragSource_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _shelfDragPaths.Length == 0)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _shelfDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _shelfDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartShelfDrag(sender as DependencyObject ?? this, _shelfDragPaths);
        _shelfDragPaths = [];
    }

    private string[] ExistingShelfPaths() => _fileShelf.Paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();

    private void PrepareShelfDrag(Point start, string[] paths)
    {
        _shelfDragStart = start;
        _shelfDragPaths = paths;
    }

    private void StartShelfDrag(DependencyObject source, string[] paths)
    {
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData(ShelfDragMarker, true);
        _draggingShelfOut = true;
        try
        {
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            _draggingShelfOut = false;
        }
    }

    private async Task SaveFileShelfAsync()
    {
        try
        {
            await _fileShelfStore.SaveAsync(_fileShelf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
