using WinRect = Windows.Foundation.Rect;
using WinSize = Windows.Foundation.Size;

namespace Winotch;

public enum CameraMirrorPhase
{
    Closed,
    Opening,
    Live,
    Error
}

public enum CameraMirrorErrorKind
{
    None,
    NoCamera,
    AccessDenied,
    CameraInUse
}

public readonly record struct CameraMirrorState(CameraMirrorPhase Phase, CameraMirrorErrorKind Error)
{
    public static CameraMirrorState Closed => new(CameraMirrorPhase.Closed, CameraMirrorErrorKind.None);
    public static CameraMirrorState Opening => new(CameraMirrorPhase.Opening, CameraMirrorErrorKind.None);
    public static CameraMirrorState Live => new(CameraMirrorPhase.Live, CameraMirrorErrorKind.None);

    public string Message => Error switch
    {
        CameraMirrorErrorKind.NoCamera => "No camera available",
        CameraMirrorErrorKind.AccessDenied => "Camera access denied",
        CameraMirrorErrorKind.CameraInUse => "Camera is in use",
        _ => ""
    };
}

public static class CameraMirrorLifecycle
{
    public static CameraMirrorState BeginOpen(CameraMirrorState state) =>
        state.Phase == CameraMirrorPhase.Live || state.Phase == CameraMirrorPhase.Opening
            ? state
            : CameraMirrorState.Opening;

    public static CameraMirrorState MarkLive(CameraMirrorState state) =>
        state.Phase == CameraMirrorPhase.Opening ? CameraMirrorState.Live : state;

    public static CameraMirrorState Fail(CameraMirrorErrorKind error) =>
        new(CameraMirrorPhase.Error, error);

    public static CameraMirrorState Close() => CameraMirrorState.Closed;

    public static bool SuppressesCameraAlerts(CameraMirrorState state) =>
        state.Phase is CameraMirrorPhase.Opening or CameraMirrorPhase.Live;
}

public static class CameraMirrorLayout
{
    public static WinRect Cover(WinSize source, WinSize bounds)
    {
        if (source.Width <= 0 || source.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        var scale = Math.Max(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new WinRect(
            (bounds.Width - width) / 2,
            (bounds.Height - height) / 2,
            width,
            height);
    }
}

public sealed record CameraMirrorFrame(int PixelWidth, int PixelHeight, byte[] BgraPixels)
{
    public int Stride => PixelWidth * 4;
}
