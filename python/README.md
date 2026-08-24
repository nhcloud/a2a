# Python — Microsoft Agent Framework over A2A

The .NET demo's counterpart, both halves of it: a **host** that publishes an agent
over A2A, and a **client** that calls one. Feature-for-feature the same as
[dotnet/A2A.Demo.HostedAgent](../dotnet/A2A.Demo.HostedAgent) and
[dotnet/A2A.Demo.Console](../dotnet/A2A.Demo.Console) — same Agent Card, same two skills,
same demos, same offline fallback.

Which means all four combinations work, and that is the interoperability claim
demonstrated rather than asserted:

|                    | → .NET host (5401) | → Python host (5402) |
| ------------------ | ------------------ | -------------------- |
| **.NET client**    | ✅                  | ✅                    |
| **Python client**  | ✅                  | ✅                    |

## Setup

The start scripts create the venv and install requirements on first run, so this is
usually all you need:

```bash
start.cmd            # Windows
./start.sh           # macOS / Linux
```

Manually, if you prefer:

```bash
python -m venv .venv
.venv\Scripts\activate         # Windows
# source .venv/bin/activate     # macOS / Linux
pip install -r requirements.txt
```

### Configuration — `.env`

Copy the template from the repo root into this folder:

```bash
cp ../.env.template .env         # copy ..\.env.template .env  on Windows
```

Both the host and the client read it. It is gitignored, so keys stay out of the repo.
Everything in it is optional — with no `.env` at all the host falls back to a scripted
offline agent and every demo still runs.

| Variable                                                            | Purpose                                  |
| ------------------------------------------------------------------- | ---------------------------------------- |
| `AZURE_OPENAI_ENDPOINT` / `AZURE_OPENAI_API_KEY` / `AZURE_OPENAI_DEPLOYMENT` | Put a real model behind the agent |
| `A2A_HOST` / `A2A_PORT`                                             | Where the host listens (default `127.0.0.1:5402`) |
| `DEMO_LONG_RUNNING_STEP_SECONDS`                                    | Padding per step in the long job (default 3s) |
| `A2A_BASE_URL`                                                      | Which agent the client calls             |

`.env` wins over [appsettings.json](appsettings.json), which holds the Agent Card and
skill metadata in the same schema the .NET host uses.

## The host

```bash
start.cmd            # or: ./start.sh
start.cmd host       # same thing, explicitly
python a2a_host.py   # or drive it directly
```

Listens on `http://localhost:5402`, so it runs happily alongside the .NET host on
5401. Endpoints mirror the .NET host exactly:

```
GET  /.well-known/agent-card.json      discovery
POST /a2a                              full-control path (JSON-RPC)
POST /a2a/simple                       one-liner path (JSON-RPC)
POST /a2a/simple-http/message:send     one-liner path (HTTP+JSON)
```

### Layout

```
start.cmd / start.sh              venv bootstrap + run
a2a_host.py                       entry point (uvicorn)
hosted_agent/
  app.py                          Agent Card + both hosting paths   ← Program.cs
  handler.py                      AgentExecutor, routes to a skill  ← DemoAgentHandler.cs
  agent_factory.py                the agent + offline fallback      ← HostedAgentFactory.cs
  config.py                       .env + appsettings.json binding   ← DemoAgentOptions.cs
  skills/
    base.py                       skill abstraction                 ← IA2ASkill.cs
    quick_answer.py               Message path                      ← QuickAnswerSkill.cs
    market_research.py            Task path                         ← MarketResearchSkill.cs
```

## The client

```bash
start.cmd client                 # all demos
start.cmd client card ask job    # pick specific ones
./start.sh client all            # macOS / Linux

python a2a_console.py card       # or drive it directly
```

Target is whatever `A2A_BASE_URL` says (default `http://localhost:5401`, the .NET
host). Override it per-run:

```bash
A2A_BASE_URL=http://localhost:5402 python a2a_console.py all
```

Demos: `card`, `ask`, `stream`, `job`. (The .NET client's fifth demo, `delegate`,
has no Python equivalent here — it needs a local model to do the orchestrating.)

## Versions

Checked on 2026-08-23:

| Package                  | Version        | Notes                                 |
| ------------------------ | -------------- | ------------------------------------- |
| `agent-framework`        | `1.15.0`       | stable meta-package                   |
| `agent-framework-core`   | `1.15.0`       | stable                                |
| `agent-framework-a2a`    | `1.0.0b260821` | beta — the A2A integration is pre-1.0 |
| `agent-framework-openai` | `1.14.0`       | stable                                |
| `a2a-sdk`                | `1.1.2`        |                                       |

Python trails .NET: the .NET Agent Framework is on 1.19.0 while Python's stable line
is 1.15.0, and the A2A packages are pre-release on both.

## Where the two SDKs differ

Nothing below is a protocol disagreement — the wire format is the same. It is all
SDK ergonomics, and it is exactly the "implementation maturity varies" point on
slide 13. Each one cost real debugging time, so they are worth a mention on stage.

**`AgentInterface.protocolVersion` must be set explicitly.** Protobuf omits empty
strings from JSON, and the .NET SDK marks the field required — so a Python card that
leaves it unset is *unreadable* to .NET clients. One line fixes it; nothing warns you.

**The Python SDK requires an `A2A-Version: 1.0` request header.** Missing means 0.3,
and the request is rejected outright. The Agent Framework clients send it on both
sides, so this only bites hand-rolled `curl` calls — but it bites hard, and the error
message does not mention the header.

**No `/v1` path segment** on HTTP+JSON routes in either SDK: `/message:send`, not
`/v1/message:send` as older samples show.

**`TaskUpdater.submit()` does less in Python.** .NET's `SubmitAsync` puts the `Task`
on the queue for you; Python's raises *"Agent should enqueue Task before
TaskStatusUpdateEvent"* unless you enqueue `new_task_from_user_message(...)` first.
See the top of `market_research.py`.

**The one-liner paths disagree about Message vs. Task.** .NET's `MapA2AJsonRpc`
returns a plain `Message` when the agent answers immediately. Python's `A2AExecutor`
always creates a `Task`, and streams the answer into an artifact one word per part.
Both are legal; they are not the same caller experience.

**`BaseChatClient._inner_get_response` is not `async`.** It is a plain `def` that
returns an awaitable for `stream=False` and a `ResponseStream` for `stream=True`.
Writing it as `async def` fails only on the streaming path, with
`'coroutine' object has no attribute 'map'`.
