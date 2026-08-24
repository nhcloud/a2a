namespace A2A.Demo.HostedAgent.Configuration;

/// <summary>
/// Everything the gateway needs to describe itself in its Agent Card, bound from
/// the "A2AAgent" configuration section.
/// </summary>
/// <remarks>
/// The Agent Card is the A2A "business card" (talk slide 9): identity, endpoint,
/// skills and auth requirements. Keeping it in configuration makes the point that
/// discovery metadata is data, not code.
/// </remarks>
public sealed class DemoAgentOptions
{
    public const string SectionName = "A2AAgent";

    public string Name { get; set; } = "Contoso Research Agent";

    public string Description { get; set; } =
        "A demo agent exposed over the Agent2Agent (A2A) protocol.";

    public string Version { get; set; } = "1.0.0";

    public string? DocumentationUrl { get; set; }

    /// <summary>
    /// Public base URL advertised in the Agent Card's supported interfaces. Must be
    /// reachable by callers — change it when hosting behind a tunnel or in Azure.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5401";

    public string[] DefaultInputModes { get; set; } = ["text"];

    public string[] DefaultOutputModes { get; set; } = ["text"];

    /// <summary>Whether the agent advertises SSE streaming support.</summary>
    public bool Streaming { get; set; } = true;

    public List<DemoSkillOptions> Skills { get; set; } = [];
}

/// <summary>A single advertised A2A skill.</summary>
public sealed class DemoSkillOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public List<string> Examples { get; set; } = [];
}

/// <summary>Azure OpenAI settings for the Agent Framework agent behind the protocol.</summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Either an Azure AI Foundry v1 endpoint ("https://{resource}.services.ai.azure.com/openai/v1")
    /// or a classic Azure OpenAI resource URL ("https://{resource}.openai.azure.com/").
    /// Leave blank to run the demo fully offline against the scripted agent.
    /// </summary>
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string Deployment { get; set; } = "gpt-4o-mini";
}
