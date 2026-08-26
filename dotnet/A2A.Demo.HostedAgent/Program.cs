using A2A.AspNetCore;
using A2A.Demo.HostedAgent.Agents;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.Options;

// The controllers log an arrow per inbound call; without this the console codepage
// mangles them. Matches what the demo console already does.
Console.OutputEncoding = System.Text.Encoding.UTF8;

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
// notifier, and the IA2ARequestHandler that A2AController calls into.
builder.Services.AddA2AAgent<DemoAgentHandler>(agentCard);

// The card as a service, so AgentCardController can inject it.
builder.Services.AddSingleton(agentCard);

// MVC, because path 1's protocol surface is a controller rather than MapA2A. The
// controllers hand back A2A.AspNetCore's own IResult types for the wire format, so
// MVC's JSON formatters never touch an A2A payload and need no configuration here.
builder.Services.AddControllers();

// ── Path 2: the one-liner ───────────────────────────────────────────────────────
// Publishes the same Agent Framework agent with no handler and no lifecycle code.
// AllowBackgroundIfSupported lets callers ask for a Task instead of blocking.
builder.AddA2AServer(agentFactory.Agent, options =>
{
    options.AgentRunMode = AgentRunMode.AllowBackgroundIfSupported;
});

var app = builder.Build();

// Path 1 endpoints, all attribute-routed on controllers so every call the console
// makes lands on a named method you can breakpoint:
//   POST /a2a                              A2AController: SendMessage, SendStreamingMessage,
//                                          GetTask, CancelTask, ListTasks, ...
//   GET  /.well-known/agent-card.json      AgentCardController — standard root discovery,
//   GET  /a2a/.well-known/agent-card.json  so any client can find this agent from the
//                                          base URL alone (talk slide 9).
//   GET  /                                 redirect to the card
app.MapControllers();

// Path 2 stays a minimal-API one-liner on purpose: the contrast with the controller
// above is the point. Same agent, no handler, no controller, no lifecycle code.
//
// Path 2 endpoints — two bindings, because A2A 1.0 defines more than one and the
// hosting package exposes them separately:
//   POST /a2a/simple                        JSON-RPC
//   POST /a2a/simple-http/message:send      HTTP+JSON  (also message:stream)
//   GET  /a2a/simple-http/card              HTTP+JSON agent card
// Note: no "/v1" path segment in A2A 1.0.0-preview2, despite what older samples show.
app.MapA2AJsonRpc(agentFactory.Agent, "/a2a/simple");
app.MapA2AHttpJson(agentFactory.Agent, "/a2a/simple-http");

app.Logger.LogInformation(
    "A2A agent '{Name}' | backend: {Backend}",
    agentOptions.Name,
    agentFactory.IsModelBacked ? "Azure OpenAI via Microsoft Agent Framework" : "offline scripted agent");
app.Logger.LogInformation(
    "  card        {BaseUrl}/.well-known/agent-card.json  (AgentCardController)", agentOptions.PublicBaseUrl.TrimEnd('/'));
app.Logger.LogInformation(
    "  full-control JSON-RPC  {BaseUrl}/a2a  (A2AController)", agentOptions.PublicBaseUrl.TrimEnd('/'));
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
