using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Winotch;

public sealed class CameraMirrorService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private CameraMirrorState _state = CameraMirrorState.Closed;
    private int _frameInFlight;

    public event Action<CameraMirrorState>? StateChanged;
    public event Action<CameraMirrorFrame>? FrameReady;

    public CameraMirrorState State => _state;

    public async Task<CameraMirrorState> OpenAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_state.Phase is CameraMirrorPhase.Opening or CameraMirrorPhase.Live)
            {
                return _state;
            }

            SetState(CameraMirrorLifecycle.BeginOpen(_state));
            try
            {
                var deviceId = await GetDefaultCameraIdAsync();
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    await StopCaptureAsync();
                    return Fail(CameraMirrorErrorKind.NoCamera);
                }

                _capture = new MediaCapture();
                _capture.Failed += Capture_Failed;
                await _capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = deviceId,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl
                });

                var source = SelectColorSource(_capture);
                if (source is null)
                {
                    await StopCaptureAsync();
                    return Fail(CameraMirrorErrorKind.NoCamera);
                }

                _reader = await _capture.CreateFrameReaderAsync(source);
                _reader.FrameArrived += Reader_FrameArrived;
                var status = await _reader.StartAsync();
                if (status != MediaFrameReaderStartStatus.Success)
                {
                    await StopCaptureAsync();
                    return Fail(ErrorFromStartStatus(status));
                }

                SetState(CameraMirrorLifecycle.MarkLive(_state));
                return _state;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is COMException || ex is InvalidOperationException)
            {
                await StopCaptureAsync();
                return Fail(CameraMirrorErrorKind.CameraInUse);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCaptureAsync();
            SetState(CameraMirrorLifecycle.Close());
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _capture?.Dispose();
        _gate.Dispose();
    }

    private async Task CloseWithErrorAsync(CameraMirrorErrorKind error)
    {
        await _gate.WaitAsync();
        try
        {
            SetState(CameraMirrorLifecycle.Fail(error));
            await StopCaptureAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Capture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
    {
        _ = CloseWithErrorAsync(CameraMirrorErrorKind.CameraInUse);
    }

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (_state.Phase != CameraMirrorPhase.Live || Interlocked.Exchange(ref _frameInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            using var frameReference = sender.TryAcquireLatestFrame();
            var bitmap = frameReference?.VideoMediaFrame?.SoftwareBitmap;
            var frame = bitmap is null ? null : CreateFrame(bitmap);
            if (frame is not null && _state.Phase == CameraMirrorPhase.Live)
            {
                FrameReady?.Invoke(frame);
            }
        }
        catch
        {
            _ = CloseWithErrorAsync(CameraMirrorErrorKind.CameraInUse);
        }
        finally
        {
            Interlocked.Exchange(ref _frameInFlight, 0);
        }
    }

    private async Task StopCaptureAsync()
    {
        var reader = _reader;
        _reader = null;
        if (reader is not null)
        {
            reader.FrameArrived -= Reader_FrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch
            {
            }

            reader.Dispose();
        }

        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.Failed -= Capture_Failed;
            capture.Dispose();
        }
    }

    private CameraMirrorState Fail(CameraMirrorErrorKind error)
    {
        SetState(CameraMirrorLifecycle.Fail(error));
        return _state;
    }

    private void SetState(CameraMirrorState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(_state);
    }

    private static MediaFrameSource? SelectColorSource(MediaCapture capture) =>
        capture.FrameSources.Values.FirstOrDefault(source =>
            source.Info.MediaStreamType == MediaStreamType.VideoPreview &&
            source.Info.SourceKind == MediaFrameSourceKind.Color) ??
        capture.FrameSources.Values.FirstOrDefault(source =>
            source.Info.SourceKind == MediaFrameSourceKind.Color);

    private static async Task<string?> GetDefaultCameraIdAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices.FirstOrDefault()?.Id;
    }

    private static CameraMirrorErrorKind ErrorFromStartStatus(MediaFrameReaderStartStatus status) => status switch
    {
        MediaFrameReaderStartStatus.DeviceNotAvailable => CameraMirrorErrorKind.NoCamera,
        _ => CameraMirrorErrorKind.CameraInUse
    };

    private static CameraMirrorFrame CreateFrame(SoftwareBitmap source)
    {
        var bitmap = source;
        var ownsBitmap = false;
        if (source.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            source.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            bitmap = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            ownsBitmap = true;
        }

        try
        {
            var length = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
            var buffer = new Windows.Storage.Streams.Buffer((uint)length) { Length = (uint)length };
            bitmap.CopyToBuffer(buffer);
            var bytes = new byte[length];
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return new CameraMirrorFrame(bitmap.PixelWidth, bitmap.PixelHeight, bytes);
        }
        finally
        {
            if (ownsBitmap)
            {
                bitmap.Dispose();
            }
        }
    }
}
