namespace Winotch.CommandBar;

public interface ICommandProvider
{
    string Name { get; }
    int Priority { get; }
    bool IsEnabled(CommandBarSettings settings);
    Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken);
}

