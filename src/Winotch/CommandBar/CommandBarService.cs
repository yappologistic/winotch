namespace Winotch.CommandBar;

public sealed class CommandBarService
{
    private readonly IReadOnlyList<ICommandProvider> _providers;
    private readonly Func<CommandBarSettings> _settings;

    public CommandBarService(IEnumerable<ICommandProvider> providers, Func<CommandBarSettings> settings)
    {
        _providers = providers.ToArray();
        _settings = settings;
    }

    public async Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var settings = _settings();
        if (!settings.Enabled)
        {
            return [];
        }

        var results = new List<CommandBarResult>();
        foreach (var provider in _providers.Where(provider => provider.IsEnabled(settings)))
        {
            try
            {
                results.AddRange(await provider.QueryAsync(query.Trim(), cancellationToken));
            }
            catch
            {
                // Provider failures must not break the command surface; the next provider can still answer.
            }
        }

        return results
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.ProviderPriority)
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToList();
    }
}

