using Microsoft.Agents.AI;

namespace A2A.Demo.Client.Factories;

/// <summary>
/// Produces a Microsoft Agent Framework <see cref="AIAgent"/> from some backing source.
/// </summary>
/// <remarks>
/// This is the whole point of the demo. The caller asks a factory for an
/// <see cref="AIAgent"/> and gets back the same abstraction whether the agent runs
/// in this process against Azure OpenAI, or in someone else's datacenter behind the
/// A2A protocol. Swapping one for the other is a configuration change, not a rewrite
/// — which is exactly the interoperability claim A2A makes.
/// </remarks>
public interface IAgentFactory
{
    /// <summary>Short key used to select this factory, e.g. "a2a" or "azure-openai".</summary>
    string Key { get; }

    /// <summary>Human-readable label for the console menu.</summary>
    string DisplayName { get; }

    /// <summary>
    /// False when required configuration is missing, so the console can explain what
    /// to fill in rather than throwing at demo time.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Explains what is missing when <see cref="IsConfigured"/> is false.</summary>
    string? ConfigurationHint { get; }

    /// <summary>Creates the agent. Implementations cache where creation is expensive.</summary>
    Task<AIAgent> CreateAgentAsync(CancellationToken cancellationToken = default);
}
