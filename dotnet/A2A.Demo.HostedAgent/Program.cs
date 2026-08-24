using A2A.AspNetCore;
using A2A.Demo.HostedAgent.Agents;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemoAgentOptions>(
    builder.Configuration.GetSection(DemoAgentOptions.SectionName));
builder.Services.Configure<AzureOpenAIOptions>(
    builder.Configuration.GetSection(AzureOpenAIOptions.SectionName));

var agentOptions = builder.Configuration
    .GetSection(DemoAgentOptions.SectionName)
    .Get<DemoAgentOptions>() ?? new DemoAgentOptions();

var azureOpenAIOptions = builder.Configuration
    .GetSection(AzureOpenAIOptions.SectionName)
    .Get<AzureOpenAIOptions>() ?? new AzureOpenAIOptions();

// The Agent Framework agent doing the actual work, behind the protocol. Built eagerly
// because the one-line hosting path below needs the instance at registration time.
var agentFactory = new HostedAgentFactory(
    Options.Create(azureOpenAIOptions),
    LoggerFactory.Create(logging => logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole()));

builder.Services.AddSingleton(agentFactory);

// ── Path 1: full control ────────────────────────────────────────────────────────
// An IAgentHandler that drives the task lifecycle by hand. Worth the extra code when
// the agent has several skills, or when long-running work needs real progress
// reporting and artifacts rather than one blob at the end.
builder.Services.AddSingleton<IA2ASkill, MarketResearchSkill>();  // long-running: gets first refusal
builder.Services.AddSingleton<IA2ASkill, QuickAnswerSkill>();     // catch-all

AgentCard agentCard = BuildAgentCard(agentOptions);

// Registers the handler plus the A2A server infrastructure: task store, event
// notifier, and the JSON-RPC request handler that MapA2A exposes.
builder.Services.AddA2AAgent<DemoAgentHandler>(agentCard);

// ── Path 2: the one-liner ───────────────────────────────────────────────────────
// Publishes the same Agent Framework agent with no handler and no lifecycle code.
// AllowBackgroundIfSupported lets callers ask for a Task instead of blocking.
builder.AddA2AServer(agentFactory.Agent, options =>
{
    options.AgentRunMode = AgentRunMode.AllowBackgroundIfSupported;
});

var app = builder.Build();

// Path 1 endpoints:
//   POST /a2a                              JSON-RPC: SendMessage, SendStreamingMessage,
//                                          GetTask, CancelTask, ListTasks, ...
//   GET  /a2a/.well-known/agent-card.json
app.MapA2A("/a2a");

// Standard root discovery location, so any A2A client can find this agent with
// nothing but the base URL (talk slide 9).
app.MapWellKnownAgentCard(agentCard);

// Path 2 endpoints — two bindings, because A2A 1.0 defines more than one and the
// hosting package exposes them separately:
//   POST /a2a/simple                        JSON-RPC
//   POST /a2a/simple-http/message:send      HTTP+JSON  (also message:stream)
//   GET  /a2a/simple-http/card              HTTP+JSON agent card
// Note: no "/v1" path segment in A2A 1.0.0-preview2, despite what older samples show.
app.MapA2AJsonRpc(agentFactory.Agent, "/a2a/simple");
app.MapA2AHttpJson(agentFactory.Agent, "/a2a/simple-http");

app.MapGet("/", () => Results.Redirect("/.well-known/agent-card.json"));

app.Logger.LogInformation(
    "A2A agent '{Name}' | backend: {Backend}",
    agentOptions.Name,
    agentFactory.IsModelBacked ? "Azure OpenAI via Microsoft Agent Framework" : "offline scripted agent");
app.Logger.LogInformation(
    "  card        {BaseUrl}/.well-known/agent-card.json", agentOptions.PublicBaseUrl.TrimEnd('/'));
app.Logger.LogInformation(
    "  full-control JSON-RPC  {BaseUrl}/a2a", agentOptions.PublicBaseUrl.TrimEnd('/'));
app.Logger.LogInformation(
    "  one-liner    JSON-RPC  {BaseUrl}/a2a/simple", agentOptions.PublicBaseUrl.TrimEnd('/'));
app.Logger.LogInformation(
    "  one-liner    HTTP+JSON {BaseUrl}/a2a/simple-http/message:send", agentOptions.PublicBaseUrl.TrimEnd('/'));

app.Run();

static AgentCard BuildAgentCard(DemoAgentOptions options) => new()
{
    Name = options.Name,
    Description = options.Description,
    Version = options.Version,
    DocumentationUrl = string.IsNullOrWhiteSpace(options.DocumentationUrl) ? null : options.DocumentationUrl,
    SupportedInterfaces =
    [
        new AgentInterface
        {
            Url = new Uri(new Uri(options.PublicBaseUrl), "/a2a").ToString(),
            ProtocolBinding = ProtocolBindingNames.JsonRpc,
        },
    ],
    DefaultInputModes = [.. options.DefaultInputModes.Distinct()],
    DefaultOutputModes = [.. options.DefaultOutputModes.Distinct()],
    Capabilities = new AgentCapabilities
    {
        Streaming = options.Streaming,
        PushNotifications = false,
    },
    Skills = [.. options.Skills.Select(s => new AgentSkill
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        Tags = s.Tags,
        Examples = s.Examples,
    })],
};
