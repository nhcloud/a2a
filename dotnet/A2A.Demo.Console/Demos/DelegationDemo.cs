using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace A2A.Demo.Client.Demos;

/// <summary>
/// Agents as tools: a local Azure OpenAI agent decides, on its own, to hand work to
/// a remote A2A agent.
/// </summary>
/// <remarks>
/// <para>Talk slides 15 and 21 — one request, many agents; and the "agents as tools"
/// pattern.</para>
/// <para>The local agent has no idea it is making a network call. It sees a tool.
/// The remote agent has no idea it is being orchestrated. It sees an A2A message.
/// Neither side knows anything about the other's model, framework, or prompts — the
/// contract is the Agent Card and nothing else.</para>
/// <para>Needs Azure OpenAI configured; the remote agent alone is not enough here,
/// because something has to do the deciding.</para>
/// </remarks>
public sealed class DelegationDemo : IDemoScenario
{
    private readonly AgentFactoryProvider _factories;
    private readonly AzureOpenAIAgentFactory _azureOpenAI;

    public DelegationDemo(AgentFactoryProvider factories, AzureOpenAIAgentFactory azureOpenAI)
    {
        _factories = factories;
        _azureOpenAI = azureOpenAI;
    }

    public string Key => "delegate";

    public string Title => "Delegation — a local agent calls the remote one as a tool";

    public string Summary => "Azure OpenAI agent orchestrates; the A2A agent executes.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_azureOpenAI.IsConfigured)
        {
            Ux.Warn("This demo needs a local model to do the orchestrating.");
            Ux.Info(_azureOpenAI.ConfigurationHint!);
            return;
        }

        // The remote agent, reached over A2A.
        AIAgent remote = await _factories.CreateAsync("a2a", cancellationToken).ConfigureAwait(false);

        // One line turns it into a tool the local agent can call.
        AIFunction remoteAsTool = remote.AsAIFunction(new AIFunctionFactoryOptions
        {
            Name = "nashua_research_agent",
            Description = "Delegates a question or a research request to the Nashua Research Agent, "
                        + "a specialist agent reachable over A2A. Use it for anything involving market "
                        + "research, competitive analysis, or reports.",
        });

        Ux.Heading("Wiring");
        Ux.Info($"Local agent  : {_azureOpenAI.DisplayName}");
        Ux.Info($"Remote agent : {remote.Name} (over A2A)");
        Ux.Step($"Exposed to the local agent as tool \"{remoteAsTool.Name}\".");

        AIAgent coordinator = _azureOpenAI.CreateAgentWithTools(
            name: "Coordinator",
            instructions:
                """
                You are a coordinator. You have no research ability of your own.
                Whenever the user asks anything that needs research, market knowledge,
                or a report, call the nashua_research_agent tool and relay what it
                returns. Say which agent produced the answer.
                """,
            tools: [remoteAsTool]);

        AgentSession session = await coordinator.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        const string Request = "I need to understand the market for AI agent interoperability tooling. "
                             + "Get me the key points.";
        Ux.Prompt(Request);

        AgentResponse response = await coordinator
            .RunAsync(Request, session, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Ux.Agent(response.Text);

        int toolCalls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Count();

        Ux.Step($"{toolCalls} delegated call(s) crossed the A2A boundary during that turn.");
        Ux.Success("Swap the remote agent for a partner's compatible agent and this code does not change.");
    }
}
