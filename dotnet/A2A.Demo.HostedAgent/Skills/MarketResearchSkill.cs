using A2A.Demo.HostedAgent.Agents;
using Microsoft.Agents.AI;

namespace A2A.Demo.HostedAgent.Skills;

/// <summary>
/// The long-running half of the demo: work that cannot finish inside one HTTP
/// round trip, so the agent returns a <see cref="AgentTask"/> the caller can poll,
/// stream, or come back to later.
/// </summary>
/// <remarks>
/// Talk slide 11 — "Task: returned for long-running work; has an ID and a trackable
/// lifecycle; artifacts are returned or updated as work progresses." The lifecycle
/// emitted here is submitted → working (repeatedly, with progress) → completed,
/// with one artifact streamed in sections.
/// </remarks>
public sealed class MarketResearchSkill : IA2ASkill
{
    public const string SkillId = "market-research";

    private static readonly string[] TriggerWords =
        ["research", "report", "analysis", "analyse", "analyze", "deep dive", "market"];

    /// <summary>The sections the report is assembled from, one per progress step.</summary>
    private static readonly (string Heading, string Prompt)[] Sections =
    [
        ("Executive summary", "Write a three-sentence executive summary for a market research brief on: {0}"),
        ("Market landscape", "List four bullet points describing the current market landscape for: {0}"),
        ("Key risks", "List three concise risks and one mitigation each for: {0}"),
        ("Recommendation", "Give a single-paragraph recommendation with a clear next step for: {0}"),
    ];

    private readonly HostedAgentFactory _agentFactory;
    private readonly ILogger<MarketResearchSkill> _logger;
    private readonly TimeSpan _stepDelay;

    public MarketResearchSkill(
        HostedAgentFactory agentFactory,
        IConfiguration configuration,
        ILogger<MarketResearchSkill> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;

        // Padding so the "long-running" story is visible on stage even when the
        // model answers in under a second.
        _stepDelay = TimeSpan.FromSeconds(
            configuration.GetValue("Demo:LongRunningStepSeconds", 3.0));
    }

    public string Id => SkillId;

    public bool CanHandle(RequestContext context)
    {
        if (context.RequestedSkillId() is { } requested)
        {
            return string.Equals(requested, SkillId, StringComparison.OrdinalIgnoreCase);
        }

        string text = context.UserText ?? string.Empty;
        return TriggerWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        string topic = context.UserText ?? "an unspecified topic";
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

        _logger.LogInformation(
            "market-research | task {TaskId} | context {ContextId} | {Topic}",
            updater.TaskId, updater.ContextId, topic);

        try
        {
            // 1. Acknowledge. The caller gets a task id back straight away and can
            //    disconnect here — the work carries on server-side.
            await updater.SubmitAsync(cancellationToken).ConfigureAwait(false);

            await updater.StartWorkAsync(
                A2AMessageFactory.AgentText(
                    $"Starting research on \"{topic}\". {Sections.Length} sections to produce.",
                    updater.ContextId,
                    updater.TaskId),
                cancellationToken).ConfigureAwait(false);

            // 2. Do the work in steps, reporting progress and streaming the artifact
            //    as it is written rather than hoarding it until the end.
            AgentSession session = await _agentFactory.Agent
                .CreateSessionAsync(cancellationToken).ConfigureAwait(false);

            const string ArtifactId = "market-research-report";

            for (int i = 0; i < Sections.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string heading, string promptTemplate) = Sections[i];

                await updater.StartWorkAsync(
                    A2AMessageFactory.AgentText(
                        $"Step {i + 1} of {Sections.Length}: {heading}",
                        updater.ContextId,
                        updater.TaskId),
                    cancellationToken).ConfigureAwait(false);

                await Task.Delay(_stepDelay, cancellationToken).ConfigureAwait(false);

                AgentResponse section = await _agentFactory.Agent
                    .RunAsync(string.Format(promptTemplate, topic), session, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                bool isLast = i == Sections.Length - 1;

                await updater.AddArtifactAsync(
                    [Part.FromText($"## {heading}{Environment.NewLine}{section.Text}{Environment.NewLine}{Environment.NewLine}")],
                    artifactId: ArtifactId,
                    name: "Market research report",
                    description: $"Research brief on \"{topic}\".",
                    // append: true after the first chunk, so the client assembles one
                    // artifact rather than collecting four unrelated ones.
                    append: i > 0,
                    lastChunk: isLast,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // 3. Close the lifecycle. Terminal state — polling clients stop here.
            await updater.CompleteAsync(
                A2AMessageFactory.AgentText(
                    $"Research complete. The full report is attached as artifact \"{ArtifactId}\".",
                    updater.ContextId,
                    updater.TaskId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("market-research | task {TaskId} canceled", updater.TaskId);
            await updater.CancelAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "market-research | task {TaskId} failed", updater.TaskId);
            await updater.FailAsync(
                A2AMessageFactory.AgentText($"Research failed: {ex.Message}", updater.ContextId, updater.TaskId),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
