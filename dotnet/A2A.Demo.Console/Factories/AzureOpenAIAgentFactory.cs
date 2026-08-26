using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace A2A.Demo.Client.Factories;

/// <summary>Settings for a locally hosted Azure OpenAI agent.</summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Azure AI Foundry v1 endpoint ("https://{resource}.services.ai.azure.com/openai/v1")
    /// or a classic Azure OpenAI resource URL ("https://{resource}.openai.azure.com/").
    /// </summary>
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>
    /// Model deployment name. No default on purpose: it comes from the AzureOpenAI
    /// section of appsettings.json (or appsettings.Development.json / user secrets).
    /// Left unset, the factory reports itself unconfigured instead of guessing a model.
    /// </summary>
    public string? Deployment { get; set; }

    public string AgentName { get; set; } = "LocalCoordinator";

    public string Instructions { get; set; } =
        "You are a helpful coordinator agent running locally. Be concise.";
}

/// <summary>
/// Builds an <see cref="AIAgent"/> that runs in this process against Azure OpenAI.
/// </summary>
/// <remarks>
/// The counterweight to <see cref="A2AAgentFactory"/>: same return type, same call
/// site, completely different execution model. This one owns the model, the prompt,
/// and the conversation state; the A2A one owns none of them.
/// </remarks>
public sealed class AzureOpenAIAgentFactory : IAgentFactory
{
    private readonly AzureOpenAIOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<AIAgent> _agent;

    public AzureOpenAIAgentFactory(IOptions<AzureOpenAIOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _agent = new Lazy<AIAgent>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Key => "azure-openai";

    public string DisplayName => string.IsNullOrWhiteSpace(_options.Deployment)
        ? "Local Azure OpenAI agent (no deployment configured)"
        : $"Local Azure OpenAI agent ({_options.Deployment})";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Endpoint)
        && !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.Deployment);

    public string? ConfigurationHint => IsConfigured
        ? null
        : "Set AzureOpenAI:Endpoint, AzureOpenAI:ApiKey and AzureOpenAI:Deployment in "
        + "appsettings.json (or appsettings.Development.json / user secrets).";

    public Task<AIAgent> CreateAgentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_agent.Value);

    /// <summary>
    /// Builds an agent with an explicit tool list. Used by the delegation demo to hand
    /// a remote A2A agent to a local one as a callable tool.
    /// </summary>
    public AIAgent CreateAgentWithTools(string name, string instructions, IList<AITool> tools) =>
        CreateChatClient().AsAIAgent(
            name: name,
            instructions: instructions,
            tools: tools,
            loggerFactory: _loggerFactory);

    private AIAgent Build() =>
        CreateChatClient().AsAIAgent(
            name: _options.AgentName,
            instructions: _options.Instructions,
            loggerFactory: _loggerFactory);

    private IChatClient CreateChatClient()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(ConfigurationHint);
        }

        // Azure AI Foundry and Azure OpenAI both serve an OpenAI-compatible "/openai/v1"
        // surface, so a single client covers both endpoint styles.
        string endpoint = _options.Endpoint!.TrimEnd('/');
        if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/openai/v1";
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(_options.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        return client.GetResponsesClient().AsIChatClient(_options.Deployment!);
    }
}
