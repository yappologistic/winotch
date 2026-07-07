using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Winotch;

public enum MediaState
{
    None,
    Paused,
    Playing
}

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Source,
    byte[]? Thumbnail,
    MediaState State,
    bool CanPrevious,
    bool CanPlay,
    bool CanPause,
    bool CanNext)
{
    public static readonly MediaSnapshot Empty = new("", "", "", null, MediaState.None, false, false, false, false);

    public bool HasMedia =>
        State != MediaState.None ||
        CanPrevious ||
        CanPlay ||
        CanPause ||
        CanNext ||
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Artist) ||
        !string.IsNullOrWhiteSpace(Source);
    public bool IsPlaying => State == MediaState.Playing;
    public TimeSpan? Position { get; init; }
    public TimeSpan? Duration { get; init; }
    public double TimelineProgress => Position is { } position && Duration is { TotalMilliseconds: > 0 } duration
        ? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1)
        : 0;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Now playing" : Title.Trim();
    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? DisplaySource : Artist.Trim();
    public string DisplaySource => FormatSource(Source);
    public string Signature => $"{DisplayTitle}\u001f{DisplayArtist}\u001f{State}";

    public static string FormatSource(string source)
    {
        var value = source.Trim();
        if (value.Length == 0)
        {
            return "Unknown artist";
        }

        value = value.Split('!')[0].Split('_')[0];
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = Path.GetFileNameWithoutExtension(value);
        }

        value = value.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? value;
        if (value.EndsWith("Win", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^3];
        }

        return Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
    }
}

public sealed class MediaChangeTracker
{
    private string? _lastPoppedSignature;

    public bool ShouldPop(MediaSnapshot snapshot)
    {
        if (!snapshot.HasMedia || !snapshot.IsPlaying)
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(_lastPoppedSignature, snapshot.Signature))
        {
            return false;
        }

        _lastPoppedSignature = snapshot.Signature;
        return true;
    }
}

public sealed class MediaService
{
    private const ulong MaxThumbnailBytes = 2 * 1024 * 1024;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _watchedSession;

    public event EventHandler? MediaChanged;

    public async Task<MediaSnapshot> ReadAsync()
    {
        var session = await GetSessionAsync();
        Watch(session);
        if (session is null)
        {
            return MediaSnapshot.Empty;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var controls = playback.Controls;

            var snapshot = new MediaSnapshot(
                properties.Title,
                properties.Artist,
                session.SourceAppUserModelId,
                await ReadThumbnailAsync(properties.Thumbnail),
                ToState(playback.PlaybackStatus),
                controls.IsPreviousEnabled,
                controls.IsPlayEnabled,
                controls.IsPauseEnabled,
                controls.IsNextEnabled);
            var timeline = session.GetTimelineProperties();
            return snapshot with
            {
                Position = timeline.Position,
                Duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : null
            };
        }
        catch
        {
            return MediaSnapshot.Empty;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = await GetSessionAsync();
        if (session is null)
        {
            return;
        }

        if (ToState(session.GetPlaybackInfo().PlaybackStatus) == MediaState.Playing)
        {
            await session.TryPauseAsync();
        }
        else
        {
            await session.TryPlayAsync();
        }
    }

    public async Task PreviousAsync()
    {
        var session = await GetSessionAsync();
        if (session is not null)
        {
            await session.TrySkipPreviousAsync();
        }
    }

    public async Task NextAsync()
    {
        var session = await GetSessionAsync();
        if (session is not null)
        {
            await session.TrySkipNextAsync();
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        var manager = await GetManagerAsync();
        return manager?.GetCurrentSession();
    }

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            return _manager;
        }
        catch
        {
            return null;
        }
    }

    private void Watch(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_watchedSession, session))
        {
            return;
        }

        if (_watchedSession is not null)
        {
            _watchedSession.MediaPropertiesChanged -= OnMediaChanged;
            _watchedSession.PlaybackInfoChanged -= OnMediaChanged;
        }

        _watchedSession = session;
        if (_watchedSession is not null)
        {
            _watchedSession.MediaPropertiesChanged += OnMediaChanged;
            _watchedSession.PlaybackInfoChanged += OnMediaChanged;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Watch(sender.GetCurrentSession());
        MediaChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        MediaChanged?.Invoke(this, EventArgs.Empty);
    }

    private static MediaState ToState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaState.Paused,
        _ => MediaState.None
    };

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var size = (uint)Math.Min(stream.Size, MaxThumbnailBytes);
            if (size == 0)
            {
                return null;
            }

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}

public static class MediaArtwork
{
    public static ImageSource? FromBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
