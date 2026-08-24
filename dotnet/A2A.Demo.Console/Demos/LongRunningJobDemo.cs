using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using System.Diagnostics;

namespace A2A.Demo.Client.Demos;

/// <summary>
/// Long-running work: the agent cannot answer inside one round trip, so it hands back
/// a Task and the caller comes back for the result.
/// </summary>
/// <remarks>
/// <para>Talk slides 11 and 19 — Task lifecycle, and background work with a
/// continuation token.</para>
/// <para>Three beats, in order:</para>
/// <list type="number">
///   <item>Start the job in the background and get a continuation token back immediately.</item>
///   <item>Poll with that token, watching the state machine advance.</item>
///   <item>Drop to the raw protocol to show the Task and its artifacts on the wire.</item>
/// </list>
/// </remarks>
public sealed class LongRunningJobDemo : IDemoScenario
{
    private const string Job = "Research the market for AI agent interoperability tooling.";

    private readonly AgentFactoryProvider _factories;
    private readonly A2AAgentFactory _a2a;

    public LongRunningJobDemo(AgentFactoryProvider factories, A2AAgentFactory a2a)
    {
        _factories = factories;
        _a2a = a2a;
    }

    public string Key => "job";

    public string Title => "Long-running job — start, walk away, come back";

    public string Summary => "Background start, continuation-token polling, and the Task on the wire.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AIAgent agent = await _factories.CreateAsync("a2a", cancellationToken).ConfigureAwait(false);
        AgentSession session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // ── 1. Start ────────────────────────────────────────────────────────────
        Ux.Heading("1. Start the job without waiting for it");
        Ux.Prompt(Job);
        Ux.Step("AllowBackgroundResponses = true — tell the agent we will come back for this.");

        var stopwatch = Stopwatch.StartNew();
        AgentResponse response = await agent.RunAsync(
            Job,
            session,
            new AgentRunOptions { AllowBackgroundResponses = true },
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (response.ContinuationToken is null)
        {
            Ux.Warn("The agent answered immediately — no task was created, so there is nothing to poll.");
            Ux.Agent(response.Text);
            return;
        }

        var a2aSession = session as A2AAgentSession;
        Ux.Success($"Returned in {stopwatch.ElapsedMilliseconds} ms with a continuation token.");
        Ux.Info($"contextId : {a2aSession?.ContextId}");
        Ux.Info($"taskId    : {a2aSession?.TaskId}");
        Ux.Info($"state     : {a2aSession?.TaskState}");
        Ux.Step("The work is running on the remote agent. This process is free.");

        // ── 2. Poll ─────────────────────────────────────────────────────────────
        Ux.Heading("2. Poll with the continuation token");

        var total = Stopwatch.StartNew();
        int poll = 0;

        while (response.ContinuationToken is not null && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            poll++;

            response = await agent.RunAsync(
                [],
                session,
                new AgentRunOptions
                {
                    ContinuationToken = response.ContinuationToken,
                    AllowBackgroundResponses = true,
                },
                cancellationToken).ConfigureAwait(false);

            Ux.Info($"poll {poll,2} | t+{total.Elapsed.TotalSeconds,5:F1}s | state {a2aSession?.TaskState,-10} "
                  + $"| {response.Text.Length,5} chars so far"
                  + (response.ContinuationToken is null ? " | done" : string.Empty));
        }

        total.Stop();
        Ux.Success($"Task reached {a2aSession?.TaskState} after {poll} polls "
                 + $"({total.Elapsed.TotalSeconds:F1}s).");

        Ux.Heading("The finished artifact");
        Ux.Agent(response.Text);

        // ── 3. The wire ─────────────────────────────────────────────────────────
        Ux.Heading("3. What that looked like on the protocol");
        await ShowRawTaskAsync(a2aSession?.TaskId, cancellationToken).ConfigureAwait(false);

        Ux.Step("The Task outlived the request that created it. That is the whole point:");
        Ux.Step("a taskId is a durable handle another process, or a later run, can pick up.");
    }

    /// <summary>
    /// Drops out of the Agent Framework abstraction and reads the task straight from
    /// the A2A endpoint, so the audience sees the Task object the protocol defines.
    /// </summary>
    private async Task ShowRawTaskAsync(string? taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            Ux.Warn("No task id on the session — nothing to fetch.");
            return;
        }

        AgentCard card = _a2a.ResolvedCard ?? await _a2a.ResolveCardAsync(cancellationToken).ConfigureAwait(false);
        IA2AClient client = _a2a.CreateProtocolClient(card);

        AgentTask task = await client
            .GetTaskAsync(new GetTaskRequest { Id = taskId }, cancellationToken)
            .ConfigureAwait(false);

        Ux.Wire($"tasks/get → id {task.Id}");
        Ux.Wire($"            contextId {task.ContextId}");
        Ux.Wire($"            state     {task.Status?.State}");
        Ux.Wire($"            updated   {task.Status?.Timestamp:O}");
        Ux.Wire($"            history   {task.History?.Count ?? 0} messages");
        Ux.Wire($"            artifacts {task.Artifacts?.Count ?? 0}");

        foreach (Artifact artifact in task.Artifacts ?? [])
        {
            int characters = (artifact.Parts ?? []).Sum(p => p.Text?.Length ?? 0);
            Ux.Wire($"              • {artifact.ArtifactId} \"{artifact.Name}\" "
                  + $"— {artifact.Parts?.Count ?? 0} parts, {characters} chars");
        }
    }
}
