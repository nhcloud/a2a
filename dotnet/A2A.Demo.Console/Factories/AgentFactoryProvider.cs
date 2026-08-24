using Microsoft.Agents.AI;

namespace A2A.Demo.Client.Factories;

/// <summary>
/// Resolves an <see cref="IAgentFactory"/> by key. Keeps the demos free of any
/// knowledge about which backend they are talking to.
/// </summary>
public sealed class AgentFactoryProvider
{
    private readonly IReadOnlyDictionary<string, IAgentFactory> _factories;

    public AgentFactoryProvider(IEnumerable<IAgentFactory> factories)
    {
        _factories = factories.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentFactory> All => [.. _factories.Values];

    public IAgentFactory Get(string key) =>
        _factories.TryGetValue(key, out IAgentFactory? factory)
            ? factory
            : throw new KeyNotFoundException(
                $"No agent factory registered for '{key}'. Known keys: {string.Join(", ", _factories.Keys)}.");

    public Task<AIAgent> CreateAsync(string key, CancellationToken cancellationToken = default) =>
        Get(key).CreateAgentAsync(cancellationToken);
}
