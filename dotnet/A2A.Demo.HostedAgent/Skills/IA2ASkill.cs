namespace A2A.Demo.HostedAgent.Skills;

/// <summary>
/// One advertised A2A skill. The skill decides how to answer: a plain
/// <see cref="Message"/> for immediate work, or a full <see cref="AgentTask"/>
/// lifecycle for anything long-running.
/// </summary>
public interface IA2ASkill
{
    /// <summary>Matches the skill id advertised in the Agent Card.</summary>
    string Id { get; }

    /// <summary>Whether this skill should take the incoming request.</summary>
    bool CanHandle(RequestContext context);

    /// <summary>
    /// Runs the skill, writing protocol events onto <paramref name="eventQueue"/>.
    /// </summary>
    Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken);
}

/// <summary>Helpers shared by the skill implementations.</summary>
public static class A2AMessageFactory
{
    /// <summary>Builds an agent-authored message carrying a single text part.</summary>
    public static Message AgentText(string text, string? contextId = null, string? taskId = null) => new()
    {
        Role = Role.Agent,
        MessageId = Guid.NewGuid().ToString("N"),
        ContextId = contextId,
        TaskId = taskId,
        Parts = [Part.FromText(text)],
    };

    /// <summary>
    /// Reads the caller's requested skill id from message metadata, if supplied.
    /// A2A metadata is the polite way to steer a multi-skill agent without
    /// smuggling directives into the prompt text.
    /// </summary>
    public static string? RequestedSkillId(this RequestContext context)
    {
        if (context.Message?.Metadata is { } metadata
            && metadata.TryGetValue("skill", out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
