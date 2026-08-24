<!-- Demo companion for the A2A_Talk.pptx deck in this folder. -->

# A2A + Microsoft Agent Framework — live demo

Companion code for **"When AI Agents Work Together: A2A, MCP, and Connected AI"**
([A2A_Talk.pptx](A2A_Talk.pptx)).

Two hosts and two clients, one protocol. Each host publishes an agent over A2A;
each client calls one through the Microsoft Agent Framework as if it were local.
All four combinations work, which is the interoperability claim demonstrated rather
than asserted.

```
   .NET console  ┌──────────────────────────────┐          ┌────────────────────────────┐
   (MAF 1.19.0)  │  A2A.Demo.Console            │───┐  ┌──▶│  A2A.Demo.HostedAgent      │  .NET host
                 │  ├─ A2AAgentFactory          │   │  │   │  ASP.NET Core · :5401      │
                 │  └─ AzureOpenAIAgentFactory  │   ├──┤   └────────────────────────────┘
                 └──────────────────────────────┘   │  │
                                                    │  │   Agent Card · 2 skills
   Python        ┌──────────────────────────────┐   │  │   Message + Task lifecycle
   (MAF 1.15.0)  │  python/a2a_console.py       │───┘  │   ↳ Agent Framework agent
                 │  same factory pattern        │      │
                 └──────────────────────────────┘      │   ┌────────────────────────────┐
                                                       └──▶│  python/a2a_host.py        │  Python host
                          A2A: JSON-RPC · HTTP+JSON         │  Starlette · :5402         │
                                                            └────────────────────────────┘
```

The two hosts are feature-for-feature equivalent — same Agent Card, same two skills,
same lifecycle, same offline fallback, same endpoints. Either client points at either
host by changing one URL.

## Quick start

Two terminals, no cloud account required — both hosts fall back to a scripted agent
so the whole demo runs offline. Each stack has a start script that does the right
thing, including creating the Python venv on first run.

```bash
# terminal 1 — the remote agent (pick either; they behave identically)
dotnet/start.cmd            # .NET,   http://localhost:5401   (start.sh on macOS/Linux)
python/start.cmd            # Python, http://localhost:5402

# terminal 2 — the caller
dotnet/start.cmd client            # interactive menu
dotnet/start.cmd client all        # run every demo
dotnet/start.cmd client job        # just the long-running one
python/start.cmd client card job   # the Python caller
```

Or drive the projects directly. The solution lives in `dotnet/`, alongside the SDK pin
and the central package versions:

```bash
dotnet build dotnet/A2ADemo.slnx
dotnet run --project dotnet/A2A.Demo.HostedAgent
dotnet run --project dotnet/A2A.Demo.Console -- all
cd python && python a2a_host.py
cd python && python a2a_console.py all
```

Point a client at the other host with one setting:

```bash
A2A__BaseUrl=http://localhost:5402 dotnet run --project dotnet/A2A.Demo.Console -- all
A2A_BASE_URL=http://localhost:5401 python python/a2a_console.py all
```

For the Python side in detail see [python/README.md](python/README.md).

## The five demos

| Key        | Demo                  | What it shows                                                              | Slide |
| ---------- | --------------------- | -------------------------------------------------------------------------- | ----- |
| `card`     | Discovery             | Fetch the Agent Card, read skills and bindings off it, dump the raw JSON     | 9     |
| `ask`      | Request / response    | One message in, one message out. A **Message**, no task, ~150 ms            | 10–11 |
| `stream`   | Streaming (SSE)       | A long job delivered live: status transitions and artifact chunks as they land | 10 |
| `job`      | Long-running job      | Background start → continuation token → poll → the **Task** on the wire     | 11    |
| `delegate` | Agents as tools       | A local Azure OpenAI agent calls the remote A2A agent as a tool             | 15, 21 |

`stream` and `job` run **the same server-side work**. Only the delivery pattern
differs — which is the cleanest way to make slide 10 concrete.

`delegate` is the one demo that needs Azure OpenAI, because something has to do the
deciding. The rest run offline.

## Configuration

Everything works unconfigured — both hosts fall back to a scripted offline agent, and
every demo except `delegate` still runs. To put a real Azure OpenAI model behind the
agent, each stack has its own local settings file. **Both are gitignored**, so keys
never reach the repo.

### Python — `python/.env`

Copy the template from the repo root:

```bash
cp .env.template python/.env        # copy .env.template python\.env  on Windows
```

Then fill in the Azure OpenAI block. Both the host and the client read it, so one
file covers both. [.env.template](.env.template) documents every variable; the ones
that matter are:

```ini
AZURE_OPENAI_ENDPOINT=https://<resource>.services.ai.azure.com/openai/v1
AZURE_OPENAI_API_KEY=<key>
AZURE_OPENAI_DEPLOYMENT=gpt-4o-mini
```

### .NET — `appsettings.Development.json`

Create one next to each project's `appsettings.json`. It is loaded automatically and
overrides the checked-in defaults.

`dotnet/A2A.Demo.HostedAgent/appsettings.Development.json` — puts a real model behind
the hosted agent:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.services.ai.azure.com/openai/v1",
    "ApiKey": "<key>",
    "Deployment": "gpt-4o-mini"
  }
}
```

`dotnet/A2A.Demo.Console/appsettings.Development.json` — only needed for the
`delegate` demo, which needs a local model to do the orchestrating:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.services.ai.azure.com/openai/v1",
    "ApiKey": "<key>",
    "Deployment": "gpt-4o-mini"
  }
}
```

`dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>"` works too, and keeps the key
out of the working tree entirely.

### Endpoints and other knobs

Both endpoint styles work — Azure AI Foundry (`…services.ai.azure.com`) and classic
Azure OpenAI (`…openai.azure.com`). The `/openai/v1` suffix is appended if missing.

| Setting (.NET)                | `.env` (Python)                  | Purpose                                              |
| ----------------------------- | -------------------------------- | ---------------------------------------------------- |
| `Demo:LongRunningStepSeconds` | `DEMO_LONG_RUNNING_STEP_SECONDS` | Padding per step so the long job is visible on stage. Default 3s → ~12s total. |
| `A2A:BaseUrl`                 | `A2A_BASE_URL`                   | Which agent the client calls.                        |
| `A2A:BearerToken`             | —                                | Sent as `Authorization: Bearer` when the agent needs auth. |
| —                             | `A2A_HOST` / `A2A_PORT`          | Where the Python host listens. Default `127.0.0.1:5402`. |
| `Logging:LogLevel:Default`    | —                                | Set to `Information` to show protocol chatter.       |

## The factory pattern

Both factories return the same thing — a Microsoft Agent Framework `AIAgent`:

```csharp
public interface IAgentFactory
{
    string Key { get; }                                  // "a2a" | "azure-openai"
    bool IsConfigured { get; }
    Task<AIAgent> CreateAgentAsync(CancellationToken ct);
}
```

| Factory                                                                    | Produces                            | Owns                              |
| -------------------------------------------------------------------------- | ----------------------------------- | --------------------------------- |
| [A2AAgentFactory](dotnet/A2A.Demo.Console/Factories/A2AAgentFactory.cs)         | remote agent behind the A2A protocol | nothing — the card is the contract |
| [AzureOpenAIAgentFactory](dotnet/A2A.Demo.Console/Factories/AzureOpenAIAgentFactory.cs) | in-process agent on Azure OpenAI     | model, prompt, conversation state  |

The demos only ever see `AIAgent`. Swapping a local agent for a remote one is a
configuration change, not a rewrite — which is the whole argument for A2A.

## The two hosting paths

Both hosts publish the same agent twice, so the talk can contrast the two styles.
[Program.cs](dotnet/A2A.Demo.HostedAgent/Program.cs) and
[hosted_agent/app.py](python/hosted_agent/app.py) are line-for-line equivalents:

**Full control** — an `IAgentHandler` (.NET) or `AgentExecutor` (Python) driving the
task lifecycle by hand. Worth the extra code when the agent has several skills, or
when long-running work needs real progress reporting and artifacts instead of one
blob at the end.

```
POST /a2a                                JSON-RPC
GET  /.well-known/agent-card.json        discovery
```

**One line** — the Agent Framework hosting package, no handler, no lifecycle code:

```csharp
builder.AddA2AServer(agent, o => o.AgentRunMode = AgentRunMode.AllowBackgroundIfSupported);
// ...
app.MapA2AJsonRpc(agent, "/a2a/simple");
app.MapA2AHttpJson(agent, "/a2a/simple-http");
```

and in Python:

```python
simple_handler = DefaultRequestHandler(
    agent_executor=A2AExecutor(agent, stream=True),
    task_store=InMemoryTaskStore(),
    agent_card=agent_card,
)
```

```
POST /a2a/simple                         JSON-RPC
POST /a2a/simple-http/message:send       HTTP+JSON
GET  /a2a/simple-http/card               HTTP+JSON card (.NET only)
```

One behavioural difference worth knowing: .NET's one-liner returns a plain `Message`
when the agent can answer immediately, while Python's `A2AExecutor` always creates a
`Task`. Both are legal A2A; they are not the same caller experience.

## The two skills

| Skill             | Returns   | Behaviour                                                                 |
| ----------------- | --------- | ------------------------------------------------------------------------- |
| `quick-answer`    | `Message` | Answers in one turn. Catch-all.                                            |
| `market-research` | `Task`    | Four sections, ~12s, `submitted → working → completed` with one artifact streamed in appended chunks. |

Routing is on message metadata `{"skill": "market-research"}` when supplied, otherwise
on keywords (`research`, `report`, `analysis`, `market`). Metadata is the polite way to
steer a multi-skill agent without smuggling directives into the prompt.

Both hosts implement both skills identically —
[MarketResearchSkill.cs](dotnet/A2A.Demo.HostedAgent/Skills/MarketResearchSkill.cs) and
[market_research.py](python/hosted_agent/skills/market_research.py) emit the same
event sequence, so a client cannot tell which one it is talking to.

## Versions

Checked against nuget.org and PyPI on 2026-08-23.

| Package                                     | Version                  | Status  |
| ------------------------------------------- | ------------------------ | ------- |
| `Microsoft.Agents.AI`                       | `1.19.0`                 | stable  |
| `Microsoft.Agents.AI.OpenAI`                | `1.19.0`                 | stable  |
| `Microsoft.Agents.AI.A2A`                   | `1.19.0-preview.260822.1`| preview |
| `Microsoft.Agents.AI.Hosting.A2A`           | `1.19.0-preview.260822.1`| preview |
| `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`| `1.19.0-preview.260822.1`| preview |
| `A2A` / `A2A.AspNetCore`                    | `1.0.0-preview2`         | preview |
| `agent-framework-core` (Python)             | `1.15.0`                 | stable  |
| `agent-framework-a2a` (Python)              | `1.0.0b260821`           | beta    |
| `agent-framework-openai` (Python)           | `1.14.0`                 | stable  |
| `a2a-sdk` (Python)                          | `1.1.2`                  |         |

Versions are centralised in [dotnet/Directory.Build.props](dotnet/Directory.Build.props).

### Maturity notes for the talk

Slide 13 says "stable core, evolving tooling". This build ran into exactly that, and
these are worth mentioning on stage because they are the honest state of things:

- **The Agent Framework's A2A packages are all pre-release**, while the core framework
  is stable at 1.19.0. Python trails at 1.15.0 with a beta A2A package.
- **`AllowBackgroundResponses` and `ContinuationToken` are marked evaluation-only**
  (`MEAI001`) in .NET. Using them needs `<NoWarn>$(NoWarn);MEAI001</NoWarn>`. They work,
  but the API can move.
- **The hosting API has already moved.** Slide 17's `app.MapA2A(agent, path, agentCard: …)`
  is not the current shape; 1.19 splits it into `MapA2AJsonRpc` and `MapA2AHttpJson`,
  and the run mode moves to `AddA2AServer`.
- **The HTTP+JSON routes have no `/v1` segment** in `A2A` 1.0.0-preview2 — it is
  `/message:send`, not `/v1/message:send` as slide 18 shows.
- **JSON-RPC method names are PascalCase** in this SDK (`SendMessage`, `GetTask`), not
  the `message/send` form older samples use.

And from building the same host twice, once per language — all SDK ergonomics, no
protocol disagreement, and each one cost real debugging time:

- **`AgentInterface.protocolVersion` must be set explicitly in Python.** Protobuf
  omits empty strings from JSON and .NET marks the field required, so a Python card
  that leaves it unset is *unreadable* to .NET clients. Nothing warns you.
- **The Python SDK requires an `A2A-Version: 1.0` header**; missing means 0.3 and the
  request is rejected. Both Agent Framework clients send it, so this only bites
  hand-rolled `curl` — but the error never mentions the header.
- **`TaskUpdater.submit()` does less in Python.** .NET's `SubmitAsync` enqueues the
  `Task` for you; Python's fails unless you enqueue `new_task_from_user_message(...)`
  first.
- **The one-liner hosting paths disagree about Message vs. Task** (see above).

None of this touches the protocol itself. The data model, the card, and the task
lifecycle behaved exactly as specified in both stacks — a .NET client drives a Python
host's task lifecycle without a single adapter. It is the SDK surface that is still
settling.

## Layout

```
.env.template                    copy to python/.env
DEMO-SCRIPT.md                   guided walkthrough of the five demos

dotnet/
  A2ADemo.slnx                   the solution
  Directory.Build.props          central package versions
  global.json                    pins the .NET SDK
  start.cmd / start.sh           run the host, or the client with demo args
  A2A.Demo.HostedAgent/          the .NET A2A server
    Program.cs                   card, two hosting paths, endpoints
    Agents/DemoAgentHandler.cs   IAgentHandler — routes to a skill
    Agents/HostedAgentFactory.cs the Agent Framework agent + offline fallback
    Skills/QuickAnswerSkill.cs   Message path
    Skills/MarketResearchSkill.cs Task path — lifecycle, progress, artifacts
  A2A.Demo.Console/              the .NET caller
    Factories/                   IAgentFactory, A2A, Azure OpenAI, provider
    Demos/                       the five scenarios

python/
  start.cmd / start.sh           same, and creates the venv on first run
  a2a_host.py                    the Python A2A server (mirrors HostedAgent)
  hosted_agent/                  card, executor, skills, agent factory
  a2a_console.py                 the Python caller (mirrors Console)
  appsettings.json               same schema as the .NET host's
  requirements.txt               pinned Agent Framework + a2a-sdk versions
```

The whole .NET side — solution, SDK pin, and build properties — lives under `dotnet/`,
so `cd dotnet && dotnet build` is self-contained. The paths below are written from the
repo root and work from there too.

Local settings live in `python/.env` and `appsettings.Development.json` next to each
.NET project. Both are gitignored — see [Configuration](#configuration).

## Links

- A2A protocol & spec — <https://a2a-protocol.org>
- Agent Framework A2A docs — <https://learn.microsoft.com/agent-framework/integrations/a2a>
- Agent Framework repo — <https://github.com/microsoft/agent-framework>
- A2A SDKs — <https://github.com/a2aproject>
