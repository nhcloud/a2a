using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace A2A.Demo.HostedAgent.Agents;

/// <summary>
/// Builds the Microsoft Agent Framework <see cref="AIAgent"/> that sits behind the
/// A2A protocol surface.
/// </summary>
/// <remarks>
/// This is the "opaque" half of the A2A trust model (talk slide 6): callers see an
/// Agent Card and a JSON-RPC endpoint. Whether the work is done by Azure OpenAI, a
/// scripted stub, or a 200-person department is nobody else's business.
/// </remarks>
public sealed class HostedAgentFactory
{
    private const string Instructions =
        """
        You are the Contoso Research Agent, reachable over the A2A protocol.
        Answer clearly and concisely. Prefer short paragraphs and bullet points.
        When asked what you can do, describe your two skills: quick answers and
        long-running market research reports.
        """;

    private readonly AzureOpenAIOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<AIAgent> _agent;

    public HostedAgentFactory(IOptions<AzureOpenAIOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _agent = new Lazy<AIAgent>(Create, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>True when a real model is wired up rather than the offline stub.</summary>
    public bool IsModelBacked => !string.IsNullOrWhiteSpace(_options.Endpoint)
        && !string.IsNullOrWhiteSpace(_options.ApiKey);

    public AIAgent Agent => _agent.Value;

    private AIAgent Create()
    {
        IChatClient chatClient = IsModelBacked
            ? CreateAzureOpenAIChatClient()
            : new ScriptedChatClient();

        return chatClient.AsAIAgent(
            name: "ContosoResearchAgent",
            instructions: Instructions,
            loggerFactory: _loggerFactory);
    }

    private IChatClient CreateAzureOpenAIChatClient()
    {
        // Azure AI Foundry and Azure OpenAI both expose an OpenAI-compatible "/openai/v1"
        // surface, so one client covers both. Point Endpoint at that path.
        string endpoint = _options.Endpoint!.TrimEnd('/');
        if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/openai/v1";
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(_options.ApiKey!),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        return client.GetChatClient(_options.Deployment).AsIChatClient();
    }
}

/// <summary>
/// A deterministic offline <see cref="IChatClient"/> so the demo runs on conference
/// Wi-Fi with no keys, no quota, and no surprises. Swap in Azure OpenAI by filling in
/// the AzureOpenAI section of appsettings.json.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string reply = Compose(messages);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (string word in Compose(messages).Split(' '))
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string Compose(IEnumerable<ChatMessage> messages)
    {
        string prompt = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        if (prompt.Contains("capabilit", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("what can you", StringComparison.OrdinalIgnoreCase))
        {
            return "I expose two A2A skills: 'quick-answer' for immediate question and "
                 + "answer turns, and 'market-research' for long-running report generation "
                 + "that returns a Task you can poll or stream.";
        }

        return $"[offline demo agent] You asked: \"{prompt}\". "
             + "Configure AzureOpenAI in appsettings.json to route this through a real model.";
    }
}
