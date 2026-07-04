using System.IO;
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
    byte[]? Thumbnail,
    MediaState State,
    bool CanPrevious,
    bool CanPlay,
    bool CanPause,
    bool CanNext)
{
    public static readonly MediaSnapshot Empty = new("", "", null, MediaState.None, false, false, false, false);

    public bool HasMedia => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);
    public bool IsPlaying => State == MediaState.Playing;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Unknown title" : Title.Trim();
    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist.Trim();
    public string Signature => $"{DisplayTitle}\u001f{DisplayArtist}";
}

public sealed class MediaChangeTracker
{
    private string? _lastSignature;
    private bool _wasPlaying;

    public bool ShouldPop(MediaSnapshot snapshot)
    {
        var shouldPop = snapshot.HasMedia && snapshot.IsPlaying && (snapshot.Signature != _lastSignature || !_wasPlaying);
        _lastSignature = snapshot.HasMedia ? snapshot.Signature : null;
        _wasPlaying = snapshot.IsPlaying;
        return shouldPop;
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

            return new MediaSnapshot(
                properties.Title,
                properties.Artist,
                await ReadThumbnailAsync(properties.Thumbnail),
                ToState(playback.PlaybackStatus),
                controls.IsPreviousEnabled,
                controls.IsPlayEnabled,
                controls.IsPauseEnabled,
                controls.IsNextEnabled);
        }
        catch
        {
            return MediaSnapshot.Empty;
        }
    }

    public async Task TogglePlayPauseAsync(MediaSnapshot current)
    {
        var session = await GetSessionAsync();
        if (session is null)
        {
            return;
        }

        if (current.IsPlaying)
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
