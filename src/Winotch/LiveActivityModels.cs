namespace Winotch;

public enum LiveActivityKind
{
    None,
    ActivityDots,
    NowPlaying,
    Timer,
    Call
}

public enum LiveActivityDotKind
{
    Camera,
    Microphone,
    ScreenShare
}

public sealed record PrivacyActivitySnapshot(
    bool CameraActive,
    bool MicrophoneActive,
    bool ScreenShareActive);

public sealed record LiveActivityDot(LiveActivityDotKind Kind, string ColorHex, string Label);

public sealed record LiveActivityInput(
    LiveActivitySettings Settings,
    PrivacyActivitySnapshot Privacy,
    MediaSnapshot Media,
    DateTimeOffset NowUtc);

public sealed record LiveActivity(
    LiveActivityKind Kind,
    ShellMode ShellMode,
    string Title,
    string Subtitle,
    string TimeText,
    double Progress,
    TimeSpan Remaining,
    bool TimerPaused,
    byte[]? Thumbnail,
    IReadOnlyList<LiveActivityDot> Dots,
    int StackedActivityCount)
{
    public static readonly LiveActivity None = new(
        LiveActivityKind.None,
        ShellMode.Mini,
        "",
        "",
        "",
        1,
        TimeSpan.Zero,
        TimerPaused: false,
        Thumbnail: null,
        Dots: [],
        StackedActivityCount: 0);
}

public static class LiveActivityDots
{
    public const string CameraColor = "#FF9F0A";
    public const string MicrophoneColor = "#FF453A";
    public const string ScreenShareColor = "#BF5AF2";

    public static IReadOnlyList<LiveActivityDot> FromPrivacy(PrivacyActivitySnapshot privacy)
    {
        var dots = new List<LiveActivityDot>(capacity: 3);
        if (privacy.CameraActive)
        {
            dots.Add(new LiveActivityDot(LiveActivityDotKind.Camera, CameraColor, "Camera"));
        }

        if (privacy.MicrophoneActive)
        {
            dots.Add(new LiveActivityDot(LiveActivityDotKind.Microphone, MicrophoneColor, "Microphone"));
        }

        if (privacy.ScreenShareActive)
        {
            dots.Add(new LiveActivityDot(LiveActivityDotKind.ScreenShare, ScreenShareColor, "Screen share"));
        }

        return dots;
    }
}

public sealed record LiveCallWindow(string ProcessName, string Title);

public sealed record LiveCallSnapshot(bool IsActive);
