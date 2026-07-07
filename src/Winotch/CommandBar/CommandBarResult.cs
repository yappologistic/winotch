using System.Windows.Media;

namespace Winotch.CommandBar;

public sealed record CommandBarResult(
    string Title,
    string Subtitle,
    string ProviderName,
    double Score,
    int ProviderPriority,
    Func<CancellationToken, Task> ExecuteAsync,
    ImageSource? Icon = null,
    string Glyph = "\uE8D2")
{
    public static Func<CancellationToken, Task> Noop { get; } = _ => Task.CompletedTask;
    public bool HasIcon => Icon is not null;
}

