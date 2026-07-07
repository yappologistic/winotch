namespace Winotch.CommandBar;

public sealed record QuickCommandAction(string Phrase, string Description, Func<CancellationToken, Task> ExecuteAsync);

public sealed class QuickCommandProvider : ICommandProvider
{
    private readonly IReadOnlyList<QuickCommandAction> _commands;

    public QuickCommandProvider(IEnumerable<QuickCommandAction> commands)
    {
        _commands = commands.ToArray();
    }

    public string Name => "Quick Commands";
    public int Priority => 80;

    public bool IsEnabled(CommandBarSettings settings) => settings.QuickCommandsEnabled;

    public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = _commands
            .Select(command => (command, score: CommandMatch.Score(query, command.Phrase)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .Take(5)
            .Select(match => new CommandBarResult(
                match.command.Phrase,
                match.command.Description,
                Name,
                CommandMatch.Rank(match.score, Priority),
                Priority,
                match.command.ExecuteAsync))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommandBarResult>>(results);
    }
}

