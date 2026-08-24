namespace A2A.Demo.HostedAgent.Agents;

/// <summary>
/// The A2A server's entry point. <c>message/send</c> and <c>message/stream</c> land in
/// <see cref="ExecuteAsync"/>; <c>tasks/cancel</c> lands in <see cref="CancelAsync"/>.
/// </summary>
/// <remarks>
/// The handler itself does nothing clever — it picks a skill and gets out of the way.
/// Everything protocol-shaped (task persistence, SSE fan-out, JSON-RPC framing) is
/// handled by the A2A server the handler is registered with.
/// </remarks>
public sealed class DemoAgentHandler : IAgentHandler
{
    private readonly IReadOnlyList<IA2ASkill> _skills;
    private readonly ILogger<DemoAgentHandler> _logger;

    public DemoAgentHandler(IEnumerable<IA2ASkill> skills, ILogger<DemoAgentHandler> logger)
    {
        // Registration order is priority order: the long-running skill gets first refusal.
        _skills = skills.ToList();
        _logger = logger;
    }

    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        IA2ASkill? skill = _skills.FirstOrDefault(s => s.CanHandle(context));

        if (skill is null)
        {
            var responder = new MessageResponder(eventQueue, context.ContextId);
            await responder.ReplyAsync(
                "No skill on this agent can handle that request.", cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "Routing to skill {SkillId} (streaming: {Streaming}, continuation: {IsContinuation})",
            skill.Id, context.StreamingResponse, context.IsContinuation);

        await skill.ExecuteAsync(context, eventQueue, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        // The server cancels the token passed to ExecuteAsync; the running skill sees
        // it and moves its task to the canceled state. Nothing left to do here.
        _logger.LogInformation("Cancel requested for task {TaskId}", context.TaskId);
        eventQueue.Complete();
        return Task.CompletedTask;
    }
}
