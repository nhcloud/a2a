# Guided walkthrough

Run the five demos in order and watch what the protocol actually does. Each step is a
command, the output worth looking at, and the point it makes. Timings assume the
offline agent (`Demo:LongRunningStepSeconds: 3.0`) — about 8 minutes end to end, or 4
if you skip `stream` and `delegate`.

Slide numbers refer to [A2A_Talk.pptx](A2A_Talk.pptx) in this folder, so you can line
each demo up with the deck.

## Setup

```bash
cd c:\repo\nhcloud\a2a
dotnet build dotnet/A2ADemo.slnx                         # first build takes a moment
dotnet run --project dotnet/A2A.Demo.HostedAgent         # leave running, terminal 1
curl http://localhost:5401/.well-known/agent-card.json   # confirm it answers
```

Terminal 2 is where you run the client: `dotnet run --project dotnet/A2A.Demo.Console`

For step 7 you also need the Python side, in a third terminal:

```bash
cd python
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python a2a_host.py                                       # http://localhost:5402
curl http://localhost:5402/.well-known/agent-card.json   # confirm it answers
```

Keep the host terminal visible next to the client. Its log lines narrate the server
side while the client talks, and the two halves together are more informative than
either alone. The .NET host's protocol surface is a pair of controllers
([A2AController](dotnet/A2A.Demo.HostedAgent/Controllers/A2AController.cs),
[AgentCardController](dotnet/A2A.Demo.HostedAgent/Controllers/AgentCardController.cs)),
so each inbound call prints an arrow line naming the demo that produced it:

```
→ POST /a2a  SendMessage  (id e35dd979-…)  ← demo 2 'ask' (one per turn) · …
```

If you would rather step through it than read it, breakpoint the matching method —
`SendMessageAsync`, `SendStreamingMessage`, `GetTaskAsync`. The full mapping is in the
[README](README.md#which-demo-hits-what).

**No internet needed** once the build is warm. Leave Azure OpenAI unconfigured and
everything runs locally — the only demo you lose is `delegate`, and the answers read
`[offline demo agent]` instead of model output.

**For real model output:** set the Azure OpenAI settings from the
[README](README.md#configuration) first, then check the host logs say
`backend: Azure OpenAI via Microsoft Agent Framework`.

---

## 1. Discovery — the Agent Card (slide 9)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- card
```

**~20 seconds.** Three things in the output:

- Skills are declared, not discovered by trial and error.
- The interface list is how the client picks a binding — nobody hardcodes a URL shape.
- Scroll to the raw JSON: that is the whole contract. No SDK, no schema package, no
  shared types.

The takeaway: you now know what this agent can do and how to call it, and still know
nothing about how it is built.

---

## 2. Request / response — Message, not Task (slide 11)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- ask
```

**~15 seconds.** The tell is in the trace line:

```
contextId 6336b00e… | taskId (none) | task state (no task — answered as a Message)
```

No task was created. The agent could answer, so it answered. Notice the second turn
reuses the same `contextId` — that is the only thing making it a conversation.

Worth noting in the code: `A2AAgent` is an `AIAgent`. The call site is
`agent.RunAsync(text, session)`, identical to a local agent.

---

## 3. Long-running work — the Task lifecycle (slides 11, 19)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- job
```

**~15 seconds.** The centrepiece. Three sections in the output:

1. **Start** — returns in ~20 ms with a continuation token and `state: Submitted`. The
   work has not started finishing; you just have a receipt.
2. **Poll** — `Working` repeats while the character count climbs, then flips to
   `Completed` and the token goes null. That is the whole long-running pattern: no open
   connection, no webhook, no polling loop in your own code.
3. **The wire** — the `tasks/get` dump showing history, artifact id, and part count.
   One artifact, four parts, appended as the agent wrote them.

The task id is durable. A different process, or the same process tomorrow, can pick it
up.

---

## 4. Streaming — same work, different delivery (slide 10, optional)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- stream
```

**~15 seconds,** most of it watching a progress bar. It is the prettiest output in the
set and the least surprising — skip it if you are short on time, since step 3 already
makes the point.

What matters: this is the **same server-side work** as the polling demo. Nothing
changed but `RunStreamingAsync`. The `«` lines are real A2A events —
`TaskStatusUpdate` carrying progress, `TaskArtifactUpdate` carrying content, with
`append` and `lastChunk` visible.

---

## 5. Agents as tools — delegation (slide 21)

**Needs Azure OpenAI.** Unconfigured, it prints a clean "not configured" message
rather than an exception, but there is nothing to see.

```bash
dotnet run --project dotnet/A2A.Demo.Console -- delegate
```

**~20 seconds.** One line of setup does the work:

```csharp
AIFunction remoteAsTool = remote.AsAIFunction(new AIFunctionFactoryOptions { … });
```

The local agent sees a tool. The remote agent sees an A2A message. Neither knows
anything about the other's model or framework — which is the vendor-flexibility
argument: swap the remote agent for a partner's compatible agent and this code does not
change.

---

## 6. The host side — what it cost to build

If you are wondering how much code sits behind all of this, open
[Program.cs](dotnet/A2A.Demo.HostedAgent/Program.cs) and compare the two paths. The
short one is:

```csharp
// one line, no handler, no lifecycle code
app.MapA2AJsonRpc(agent, "/a2a/simple");
```

Hit it directly to see it is real:

```bash
curl -s -X POST http://localhost:5401/a2a/simple \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"1","method":"SendMessage","params":{"message":{"role":"ROLE_USER","messageId":"1","parts":[{"text":"What are your capabilities?"}]}}}'
```

And the HTTP+JSON binding of the same agent:

```bash
curl -s -X POST http://localhost:5401/a2a/simple-http/message:send \
  -H "Content-Type: application/json" \
  -d '{"message":{"role":"ROLE_USER","messageId":"1","parts":[{"text":"Hello"}]}}'
```

Same agent, two bindings, and the caller picks from the card.

---

## 7. Cross-language — the one to run if you only run one

Two moves, both cheap, and together they are the strongest evidence in the repo.

**7a. Python client, .NET agent.** Nothing on the server changed.

```bash
cd python && .venv\Scripts\python.exe a2a_console.py job
```

**~20 seconds.** Same remote agent. It does not know or care that this caller is
Python.

**7b. Flip it — .NET client, Python agent.** Start the Python host in a third terminal
(or leave it running from setup):

```bash
cd python && python a2a_host.py          # http://localhost:5402
```

Then point the .NET console at it:

```bash
A2A__BaseUrl=http://localhost:5402 dotnet run --project dotnet/A2A.Demo.Console -- card job
```

**~25 seconds.** The card demo shows a *different* agent name — "Nashua Research Agent
(Python)" — so you can see the swap really happened. Then the job demo runs the
identical lifecycle: background start, continuation token, six polls, `Completed`.

A .NET client just drove a Python agent's task lifecycle. No adapter, no shared
library, no shared types. It read a card and made calls.

All four combinations work:

|                    | → .NET host | → Python host |
| ------------------ | ----------- | ------------- |
| **.NET client**    | ✅           | ✅             |
| **Python client**  | ✅           | ✅             |

---

## Common questions

**"Is this production ready?"**
The protocol core is 1.0 and behaved exactly as specified. The Agent Framework's A2A
packages are all pre-release, and two APIs this demo uses are marked evaluation-only.
See the [maturity notes](README.md#maturity-notes-for-the-talk) in the README for the
specific drift this build hit.

**"How is this different from just calling their REST API?"**
Look back at the card demo. Discovery, capability declaration, and a defined task
lifecycle come for free. With a bespoke API you write all three yourself, per vendor.

**"What about auth?"**
The card advertises security schemes; the client sends a bearer token
(`A2A:BearerToken` in the console's config). A gateway pattern adds Entra ID JWT
validation on the JSON-RPC endpoint with the card left anonymous, so callers can still
discover how to authenticate.

**"What if the remote agent goes away mid-task?"**
The task id is the handle. `tasks/get` is a plain request — reconnecting is not a
special case, and that is why the polling pattern matters more than streaming for
anything genuinely long.

**"Do you need adapters to make .NET and Python talk?"**
No. One quirk: the Python card has to set `protocolVersion` explicitly, because
protobuf omits empty strings and the .NET SDK marks the field required. That is the
entire interop cost, and it is an SDK quirk, not a protocol one. The maturity notes in
the README list the others that came up building the same host twice.

**"Why not just MCP?"**
Slide 5. MCP gives one agent its tools; A2A lets independent agents delegate. The
`delegate` demo is literally both shapes at once — the remote agent is exposed to the
local one *as a tool*, but it is a full agent on the other side of the call.
