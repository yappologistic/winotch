using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace Winotch.Tests;

public class CameraMirrorTests
{
    [Fact]
    public void LifecycleMovesFromClosedToOpeningToLive()
    {
        var state = CameraMirrorState.Closed;

        state = CameraMirrorLifecycle.BeginOpen(state);
        state = CameraMirrorLifecycle.MarkLive(state);

        Assert.Equal(CameraMirrorPhase.Live, state.Phase);
        Assert.Equal(CameraMirrorErrorKind.None, state.Error);
    }

    [Fact]
    public void LifecycleAllowsRapidCloseAndReopen()
    {
        var state = CameraMirrorLifecycle.BeginOpen(CameraMirrorState.Closed);

        state = CameraMirrorLifecycle.Close();
        state = CameraMirrorLifecycle.BeginOpen(state);

        Assert.Equal(CameraMirrorPhase.Opening, state.Phase);
        Assert.Equal(CameraMirrorErrorKind.None, state.Error);
    }

    [Theory]
    [InlineData(CameraMirrorErrorKind.NoCamera, "No camera available")]
    [InlineData(CameraMirrorErrorKind.CameraInUse, "Camera is in use")]
    [InlineData(CameraMirrorErrorKind.AccessDenied, "Camera access denied")]
    public void LifecycleStoresQuietErrorMessage(CameraMirrorErrorKind error, string expected)
    {
        var state = CameraMirrorLifecycle.Fail(error);

        Assert.Equal(CameraMirrorPhase.Error, state.Phase);
        Assert.Equal(expected, state.Message);
    }

    [Theory]
    [InlineData(CameraMirrorPhase.Closed, false)]
    [InlineData(CameraMirrorPhase.Opening, true)]
    [InlineData(CameraMirrorPhase.Live, true)]
    [InlineData(CameraMirrorPhase.Error, false)]
    public void LifecycleSuppressesCameraAlertsOnlyWhilePreviewCanOwnCamera(
        CameraMirrorPhase phase,
        bool expected)
    {
        var state = new CameraMirrorState(phase, CameraMirrorErrorKind.None);

        Assert.Equal(expected, CameraMirrorLifecycle.SuppressesCameraAlerts(state));
    }

    [Theory]
    [InlineData(1920, 1080, 320, 240, 426.67, 240, -53.33, 0)]
    [InlineData(1280, 720, 240, 320, 568.89, 320, -164.44, 0)]
    [InlineData(1080, 1920, 320, 240, 320, 568.89, 0, -164.44)]
    public void CoverFillsViewportWithoutLetterboxing(
        double sourceWidth,
        double sourceHeight,
        double boundsWidth,
        double boundsHeight,
        double expectedWidth,
        double expectedHeight,
        double expectedX,
        double expectedY)
    {
        var placement = CameraMirrorLayout.Cover(
            new WpfSize(sourceWidth, sourceHeight),
            new WpfSize(boundsWidth, boundsHeight));

        Assert.Equal(expectedWidth, placement.Width, precision: 2);
        Assert.Equal(expectedHeight, placement.Height, precision: 2);
        Assert.Equal(expectedX, placement.X, precision: 2);
        Assert.Equal(expectedY, placement.Y, precision: 2);
    }

    [Fact]
    public void CoverReturnsEmptyForUnavailableDimensions()
    {
        var placement = CameraMirrorLayout.Cover(
            new WpfSize(0, 1080),
            new WpfSize(320, 240));

        Assert.Equal(WpfRect.Empty, placement);
    }

    [Fact]
    public void PriorityTrackerSuppressesCameraAlertWhileMirrorIsLive()
    {
        var tracker = new PriorityStatusTracker();
        var status = Status(camera: true);

        Assert.Null(tracker.Next(status, suppressCameraAlert: true));
        Assert.Null(tracker.Next(status));
    }

    [Fact]
    public void PriorityTrackerKeepsOtherAlertsWhenCameraAlertIsSuppressed()
    {
        var tracker = new PriorityStatusTracker();
        var status = Status(percent: 12, microphone: true, camera: true);

        Assert.Equal("Microphone active", tracker.Next(status, suppressCameraAlert: true)?.Title);
        Assert.Equal("Low battery", tracker.Next(status, suppressCameraAlert: true)?.Title);
        Assert.Null(tracker.Next(status, suppressCameraAlert: true));
    }

    private static PriorityStatusSnapshot Status(
        int percent = 80,
        bool charging = false,
        string? wifi = "TELUS1255",
        string? bluetooth = null,
        bool microphone = false,
        bool camera = false) =>
        new(
            new BatteryInfo(percent, charging),
            new WifiStatus(wifi, wifi is null ? null : "99%"),
            bluetooth,
            microphone,
            camera);
}
