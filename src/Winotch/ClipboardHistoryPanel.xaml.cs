using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Winotch;

public partial class ClipboardHistoryPanel : System.Windows.Controls.UserControl
{
    private static readonly TimeSpan CopiedStateDuration = TimeSpan.FromMilliseconds(900);
    private readonly ObservableCollection<ClipboardHistoryRow> _rows = [];
    private CancellationTokenSource? _copiedState;

    public ClipboardHistoryPanel()
    {
        InitializeComponent();
        ClipboardList.ItemsSource = _rows;
        RefreshEmptyState();
    }

    public event EventHandler<ClipboardHistoryEntry>? CopyRequested;
    public event EventHandler<Guid>? DeleteRequested;
    public event EventHandler? ClearRequested;

    public void SetItems(IReadOnlyList<ClipboardHistoryEntry> items)
    {
        _rows.Clear();
        var now = DateTimeOffset.Now;
        foreach (var item in items)
        {
            _rows.Add(new ClipboardHistoryRow(item, now));
        }

        RefreshEmptyState();
    }

    public void RefreshTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var row in _rows)
        {
            row.UpdateTime(now);
        }
    }

    public void ShowCopied(Guid id)
    {
        _copiedState?.Cancel();
        _copiedState?.Dispose();
        _copiedState = new CancellationTokenSource();
        foreach (var row in _rows)
        {
            row.SetCopied(row.Entry.Id == id);
        }

        _ = ClearCopiedStateAsync(_copiedState.Token);
    }

    private async Task ClearCopiedStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CopiedStateDuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        foreach (var row in _rows)
        {
            row.SetCopied(false);
        }
    }

    private void RefreshEmptyState()
    {
        var hasRows = _rows.Count > 0;
        ClipboardList.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyClipboardText.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        ClearClipboardButton.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClipboardItem_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ClipboardHistoryEntry entry)
        {
            CopyRequested?.Invoke(this, entry);
            e.Handled = true;
        }
    }

    private void DeleteClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ClipboardHistoryEntry entry)
        {
            DeleteRequested?.Invoke(this, entry.Id);
            e.Handled = true;
        }
    }

    private void ClearClipboard_Click(object sender, RoutedEventArgs e)
    {
        ClearRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

public sealed class ClipboardHistoryRow : INotifyPropertyChanged
{
    private string _timeText;
    private bool _copied;

    public ClipboardHistoryRow(ClipboardHistoryEntry entry, DateTimeOffset now)
    {
        Entry = entry;
        Thumbnail = ClipboardThumbnail.ToBitmapSource(entry.ThumbnailPng);
        _timeText = ClipboardHistoryFormatting.RelativeTime(entry.CapturedAt, now);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ClipboardHistoryEntry Entry { get; }
    public string Glyph => Entry.Kind switch
    {
        ClipboardHistoryKind.Text => "\uE8D2",
        ClipboardHistoryKind.Link => "\uE71B",
        ClipboardHistoryKind.Image => "\uE91B",
        ClipboardHistoryKind.Files => "\uE8A5",
        _ => "\uE8D2"
    };
    public string Preview => Entry.Preview;
    public ImageSource? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public string ConfirmationGlyph => _copied ? "\uE8FB" : "";

    public string TimeText
    {
        get => _timeText;
        private set
        {
            if (StringComparer.Ordinal.Equals(_timeText, value))
            {
                return;
            }

            _timeText = value;
            OnPropertyChanged();
        }
    }

    public void UpdateTime(DateTimeOffset now) =>
        TimeText = ClipboardHistoryFormatting.RelativeTime(Entry.CapturedAt, now);

    public void SetCopied(bool copied)
    {
        if (_copied == copied)
        {
            return;
        }

        _copied = copied;
        OnPropertyChanged(nameof(ConfirmationGlyph));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
