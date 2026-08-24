using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace A2A.Demo.Client.Factories;

/// <summary>Settings for reaching a remote A2A agent.</summary>
public sealed class A2AOptions
{
    public const string SectionName = "A2A";

    /// <summary>Base URL of the remote agent, e.g. "http://localhost:5401".</summary>
    public string BaseUrl { get; set; } = "http://localhost:5401";

    /// <summary>
    /// Discovery path for the Agent Card. The A2A well-known location is the default;
    /// override only for agents that publish theirs somewhere else.
    /// </summary>
    public string AgentCardPath { get; set; } = "/.well-known/agent-card.json";

    /// <summary>Optional bearer token when the remote agent requires Entra ID auth.</summary>
    public string? BearerToken { get; set; }

    /// <summary>How long to let a single A2A call run before giving up.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Wraps a remote A2A endpoint as a local <see cref="AIAgent"/>.
/// </summary>
/// <remarks>
/// The flow is card-first, per the A2A discovery model: fetch the Agent Card, then
/// build a client from what the card advertises (endpoint, protocol binding). Nothing
/// about the remote agent's framework, model, or prompts is known here — and that is
/// the design, not a limitation.
/// </remarks>
public sealed class A2AAgentFactory : IAgentFactory, IDisposable
{
    private readonly A2AOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;
    private AgentCard? _card;

    public A2AAgentFactory(IOptions<A2AOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
        };

        if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.BearerToken);
        }
    }

    public string Key => "a2a";

    public string DisplayName => $"Remote A2A agent ({_options.BaseUrl})";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public string? ConfigurationHint => IsConfigured
        ? null
        : "Set A2A:BaseUrl in appsettings.json to the URL of a running A2A agent.";

    /// <summary>The Agent Card fetched during creation. Null until the agent is built.</summary>
    public AgentCard? ResolvedCard => _card;

    /// <summary>The raw protocol client, for the parts of the demo that show the wire.</summary>
    public IA2AClient CreateProtocolClient(AgentCard card) =>
        A2AClientFactory.Create(card, _httpClient);

    /// <summary>Fetches the Agent Card without building an agent.</summary>
    public async Task<AgentCard> ResolveCardAsync(CancellationToken cancellationToken = default)
    {
        var resolver = new A2ACardResolver(
            new Uri(_options.BaseUrl),
            _httpClient,
            _options.AgentCardPath,
            _loggerFactory.CreateLogger<A2ACardResolver>());

        _card = await resolver.GetAgentCardAsync(cancellationToken).ConfigureAwait(false);
        return _card;
    }

    public async Task<AIAgent> CreateAgentAsync(CancellationToken cancellationToken = default)
    {
        if (_agent is not null)
        {
            return _agent;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_agent is not null)
            {
                return _agent;
            }

            AgentCard card = _card ?? await ResolveCardAsync(cancellationToken).ConfigureAwait(false);

            // AsAIAgent picks the client binding from the card's supported interfaces and
            // hands back a plain AIAgent. From here the remote agent is indistinguishable
            // from a local one at the call site.
            _agent = card.AsAIAgent(
                agentOptions: new Microsoft.Agents.AI.A2A.A2AAgentOptions
                {
                    Name = card.Name,
                    Description = card.Description,
                },
                httpClient: _httpClient,
                loggerFactory: _loggerFactory);

            return _agent;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }
}
