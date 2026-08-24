using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using Microsoft.Agents.AI;
using System.Diagnostics;

namespace A2A.Demo.Client.Demos;

/// <summary>
/// The simplest A2A interaction: ask, get an answer. No task, no polling.
/// </summary>
/// <remarks>
/// Talk slides 10 and 11 — request/response, and "Message: returned when the agent
/// can answer immediately". The <c>contextId</c> carried on the session is what turns
/// two separate calls into one conversation.
/// </remarks>
public sealed class RequestResponseDemo : IDemoScenario
{
    private readonly AgentFactoryProvider _factories;

    public RequestResponseDemo(AgentFactoryProvider factories) => _factories = factories;

    public string Key => "ask";

    public string Title => "Request / response — call the remote agent";

    public string Summary => "One message in, one message out, over A2A.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AIAgent agent = await _factories.CreateAsync("a2a", cancellationToken).ConfigureAwait(false);

        Ux.Heading("The agent, from the caller's point of view");
        Ux.Info($"Type        : {agent.GetType().Name}");
        Ux.Info($"Name        : {agent.Name}");
        Ux.Info($"Description : {agent.Description}");
        Ux.Step("It is an AIAgent like any other — the network hop is an implementation detail.");

        // A session carries the A2A contextId, which is how the server groups related
        // messages and tasks into a single conversation.
        AgentSession session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        string[] turns =
        [
            "What are your capabilities?",
            "Which of those would you use for a job that takes ten minutes?",
        ];

        foreach (string turn in turns)
        {
            Ux.Prompt(turn);

            var stopwatch = Stopwatch.StartNew();
            AgentResponse response = await agent
                .RunAsync(turn, session, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            Ux.Agent(response.Text);
            Ux.Step($"answered in {stopwatch.ElapsedMilliseconds} ms");
            Ux.Step(DescribeSession(session));
        }

        Ux.Success("Both turns shared one contextId — that is what made it a conversation.");
    }

    private static string DescribeSession(AgentSession session) =>
        session is Microsoft.Agents.AI.A2A.A2AAgentSession a2aSession
            ? $"contextId {a2aSession.ContextId ?? "(none)"} | taskId {a2aSession.TaskId ?? "(none)"} "
              + $"| task state {a2aSession.TaskState?.ToString() ?? "(no task — answered as a Message)"}"
            : $"session {session.GetType().Name}";
}
