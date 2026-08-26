using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace A2A.Demo.HostedAgent.Controllers;

/// <summary>
/// Discovery: serves the Agent Card, replacing <c>app.MapWellKnownAgentCard(card)</c>.
/// </summary>
/// <remarks>
/// This is the first thing any A2A client touches — demo 1 ("card") does nothing else,
/// and the other four hit it implicitly, because <c>A2AAgentFactory</c> resolves the
/// card before it will build an agent. Everything the caller knows about this agent
/// comes from the JSON below (talk slide 9).
/// </remarks>
[ApiController]
public sealed class AgentCardController : ControllerBase
{
    private readonly AgentCard _card;
    private readonly ILogger<AgentCardController> _logger;

    public AgentCardController(AgentCard card, ILogger<AgentCardController> logger)
    {
        _card = card;
        _logger = logger;
    }

    /// <summary>
    /// The card, at both locations A2A clients look: the well-known root path, and
    /// alongside the JSON-RPC endpoint for clients given the full endpoint URL.
    /// </summary>
    [HttpGet("/.well-known/agent-card.json")]
    [HttpGet("/a2a/.well-known/agent-card.json")]
    public ContentResult Get()
    {
        DemoTrace.Http(
            _logger,
            HttpContext,
            "demo 1 'card' — and every other demo, because the client resolves the card before it calls anything");

        // Serialized with the SDK's options so this is the real wire shape: camelCase,
        // protocol enum names, the union converters. MVC's default formatter would
        // produce something that looks close and is not.
        return Content(
            JsonSerializer.Serialize(_card, A2AJsonUtilities.DefaultOptions),
            "application/json");
    }

    /// <summary>Convenience for anyone who opens the base URL in a browser.</summary>
    [HttpGet("/")]
    public IActionResult Index() => Redirect("/.well-known/agent-card.json");
}
