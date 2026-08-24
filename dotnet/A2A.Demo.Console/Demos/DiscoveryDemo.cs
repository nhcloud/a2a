using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using System.Text.Json;

namespace A2A.Demo.Client.Demos;

/// <summary>
/// Card-first discovery: fetch the Agent Card and decide from it whether the remote
/// agent is worth calling.
/// </summary>
/// <remarks>Talk slide 9 — Agent Cards &amp; discovery.</remarks>
public sealed class DiscoveryDemo : IDemoScenario
{
    private readonly A2AAgentFactory _a2a;

    public DiscoveryDemo(A2AAgentFactory a2a) => _a2a = a2a;

    public string Key => "card";

    public string Title => "Discovery — read the Agent Card";

    public string Summary => "Fetch the remote agent's business card before calling it.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Ux.Heading("GET /.well-known/agent-card.json");

        AgentCard card = await _a2a.ResolveCardAsync(cancellationToken).ConfigureAwait(false);

        Ux.Info($"Name        : {card.Name}");
        Ux.Info($"Version     : {card.Version}");
        Ux.Info($"Description : {card.Description}");
        Ux.Info($"Streaming   : {card.Capabilities?.Streaming ?? false}");
        Ux.Info($"Push notify : {card.Capabilities?.PushNotifications ?? false}");
        Ux.Info($"Input modes : {string.Join(", ", card.DefaultInputModes ?? [])}");
        Ux.Info($"Output modes: {string.Join(", ", card.DefaultOutputModes ?? [])}");

        Ux.Heading("Interfaces the card advertises");
        foreach (AgentInterface iface in card.SupportedInterfaces ?? [])
        {
            Ux.Info($"{iface.ProtocolBinding,-10} {iface.Url}  (protocol {iface.ProtocolVersion})");
        }

        Ux.Heading("Skills the card advertises");
        foreach (AgentSkill skill in card.Skills ?? [])
        {
            Ux.WriteLine($"  {skill.Id}", ConsoleColor.White);
            Ux.Info($"    {skill.Description}");
            Ux.Info($"    tags: {string.Join(", ", skill.Tags ?? [])}");
            foreach (string example in skill.Examples ?? [])
            {
                Ux.Info($"    e.g. \"{example}\"");
            }
        }

        if (card.SecuritySchemes is { Count: > 0 })
        {
            Ux.Heading("Security schemes");
            foreach ((string name, SecurityScheme scheme) in card.SecuritySchemes)
            {
                Ux.Info($"{name}: {scheme.SchemeCase}");
            }
        }
        else
        {
            Ux.Warn("No security schemes advertised — fine for a demo, not for production.");
        }

        Ux.Heading("Raw card");
        // Serialize with the SDK's own options so this is the actual wire format —
        // camelCase, protocol enum names and all — not .NET's default shape.
        var wireOptions = new JsonSerializerOptions(A2AJsonUtilities.DefaultOptions)
        {
            WriteIndented = true,
        };
        Ux.Wire(JsonSerializer.Serialize(card, wireOptions));
    }
}
