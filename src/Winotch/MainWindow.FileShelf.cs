using System.IO;
using System.Windows;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;

namespace Winotch;

public partial class MainWindow
{
    private readonly FileShelfStore _fileShelfStore = new();
    private FileShelf _fileShelf = new();
    private ShellMode _currentShellMode = ShellMode.Mini;
    private CancellationTokenSource? _fileDropLeave;
    private bool _fileDropInside;
    private bool _fileDropPreviewVisible;

    private void InitializeFileShelf() => UpdateShelfMiniIndicator();

    private async Task LoadFileShelfAsync()
    {
        _fileShelf = await _fileShelfStore.LoadAsync();
        UpdateShelfMiniIndicator();
    }

    private void DisposeFileShelf()
    {
        _fileDropLeave?.Cancel();
        _fileDropLeave?.Dispose();
    }

    private void UpdateShelfMiniIndicator()
    {
        var visible = _settings.Current.Features.FileShelfEnabled &&
            _fileShelf.Count > 0 &&
            !_expanded &&
            !_compactToastVisible &&
            _currentShellMode == ShellMode.Mini;
        ShelfMiniIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ShelfMiniCountText.Text = _fileShelf.Count > 9 ? "9+" : _fileShelf.Count.ToString();
    }

    private void ApplyFileShelfEnabled(bool enabled)
    {
        AllowDrop = enabled;
        if (!enabled)
        {
            EndShelfDropPreview(collapseIfPointerAway: false);
        }

        UpdateShelfMiniIndicator();
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
            UpdateShelfMiniIndicator();
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
        if (!_settings.Current.Features.FileShelfEnabled)
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
