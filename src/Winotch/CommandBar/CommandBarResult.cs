namespace Winotch.CommandBar;

public sealed record CommandBarResult(
    string Title,
    string Subtitle,
    string ProviderName,
    double Score,
    int ProviderPriority,
    Func<CancellationToken, Task> ExecuteAsync)
{
    public static Func<CancellationToken, Task> Noop { get; } = _ => Task.CompletedTask;
}

