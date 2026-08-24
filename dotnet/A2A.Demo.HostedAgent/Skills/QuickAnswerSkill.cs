using A2A.Demo.HostedAgent.Agents;
using Microsoft.Agents.AI;
using System.Collections.Concurrent;

namespace A2A.Demo.HostedAgent.Skills;

/// <summary>
/// The request/response half of the demo: the agent can answer right now, so it
/// replies with a <see cref="Message"/> and no task is ever created.
/// </summary>
/// <remarks>
/// Talk slide 11 — "Message: returned when the agent can answer immediately."
/// This is the cheapest possible A2A interaction and should stay the default.
/// </remarks>
public sealed class QuickAnswerSkill : IA2ASkill
{
    public const string SkillId = "quick-answer";

    private readonly HostedAgentFactory _agentFactory;
    private readonly ILogger<QuickAnswerSkill> _logger;

    // One Agent Framework session per A2A contextId, so multi-turn conversations
    // keep their history. contextId is A2A's conversation grouping key (slide 11).
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    public QuickAnswerSkill(HostedAgentFactory agentFactory, ILogger<QuickAnswerSkill> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    public string Id => SkillId;

    /// <summary>Fallback skill — takes anything the long-running skill declined.</summary>
    public bool CanHandle(RequestContext context) => true;

    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        string userText = context.UserText ?? string.Empty;
        _logger.LogInformation(
            "quick-answer | context {ContextId} | {UserText}", context.ContextId, userText);

        AgentSession session = await GetSessionAsync(context.ContextId, cancellationToken)
            .ConfigureAwait(false);

        AgentResponse response = await _agentFactory.Agent
            .RunAsync(userText, session, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var responder = new MessageResponder(eventQueue, context.ContextId);
        await responder.ReplyAsync(response.Text, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentSession> GetSessionAsync(string? contextId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contextId))
        {
            return await _agentFactory.Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_sessions.TryGetValue(contextId, out AgentSession? existing))
        {
            return existing;
        }

        AgentSession created = await _agentFactory.Agent.CreateSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        return _sessions.GetOrAdd(contextId, created);
    }
}
