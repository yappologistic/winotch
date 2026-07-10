namespace Winotch.CommandBar;

public sealed class CalculatorProvider : ICommandProvider
{
    public string Name => "Calculator";
    public int Priority => 70;

    public bool IsEnabled(CommandBarSettings settings) => settings.CalculatorEnabled;

    public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        if (!CalculatorEvaluator.TryEvaluate(query, out var value, out _))
        {
            return Task.FromResult<IReadOnlyList<CommandBarResult>>([]);
        }

        var formatted = CalculatorEvaluator.Format(value);
        return Task.FromResult<IReadOnlyList<CommandBarResult>>([
            new CommandBarResult(
                formatted,
                $"Calculator · {query}",
                Name,
                96,
                Priority,
                _ => ClipboardWriter.WriteTextAsync(formatted),
                null,
                "\uE8EF")
        ]);
    }
}
