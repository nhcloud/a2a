# Stage runbook

Timings assume the offline agent (`Demo:LongRunningStepSeconds: 3.0`). Total demo
time ≈ 8 minutes if you run everything, ≈ 4 if you drop `stream` or `delegate`.

## Before you walk on

```bash
cd c:\repo\nhcloud\a2a
dotnet build dotnet/A2ADemo.slnx                         # warm the build
dotnet run --project dotnet/A2A.Demo.HostedAgent         # leave running, terminal 1
curl http://localhost:5401/.well-known/agent-card.json   # confirm it answers
```

Terminal 2, ready but not run: `dotnet run --project dotnet/A2A.Demo.Console`

If you plan to run beat 7, warm the Python side too — a third terminal, left running:

```bash
cd python
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
python a2a_host.py                                       # http://localhost:5402
curl http://localhost:5402/.well-known/agent-card.json   # confirm it answers
```

Two terminals side by side (three if you are doing beat 7). Font size up. The host
terminal is worth showing — its log lines narrate the server side while the client
talks.

**If the venue Wi-Fi is bad:** nothing here needs the internet once the build is
warm. Leave Azure OpenAI unconfigured and the whole thing runs locally. You lose only
the `delegate` demo, and the answers read `[offline demo agent]`, which is honest and
takes three seconds to explain.

**If you want real model output:** set the user secrets from the README before you
walk on, and check the host logs say `backend: Azure OpenAI via Microsoft Agent Framework`.

---

## Beat 1 — after slide 9 (Agent Cards & Discovery)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- card
```

**~20 seconds.** Point at three things:

- Skills are declared, not discovered by trial and error.
- The interface list is how the client picks a binding — nobody hardcodes a URL shape.
- Scroll to the raw JSON: *"that's the whole contract. No SDK, no schema package,
  no shared types."*

Line to land: **"I know what this agent can do and how to call it, and I still know
nothing about how it is built."**

---

## Beat 2 — after slide 11 (Message vs. Task)

```bash
dotnet run --project dotnet/A2A.Demo.Console -- ask
```

**~15 seconds.** The tell is in the trace line:

```
contextId 6336b00e… | taskId (none) | task state (no task — answered as a Message)
```

Say: *"No task was created. The agent could answer, so it answered."* Then note the
second turn reuses the same `contextId` — that is the only thing making it a
conversation.

Also worth saying out loud: `A2AAgent` is an `AIAgent`. The call site is
`agent.RunAsync(text, session)`, identical to a local agent.

---

## Beat 3 — the centrepiece, after slide 11 or 19

```bash
dotnet run --project dotnet/A2A.Demo.Console -- job
```

**~15 seconds.** Three sections, and each earns a sentence:

1. **Start** — returns in ~20 ms with a continuation token and `state: Submitted`.
   *"The work has not started finishing. We just have a receipt."*
2. **Poll** — watch `Working` repeat while the character count climbs, then flip to
   `Completed` and the token go null. *"That is the whole long-running pattern. No open
   connection, no webhook, no polling loop I had to write."*
3. **The wire** — the `tasks/get` dump showing history, artifact id, part count.
   *"One artifact, four parts, appended as the agent wrote them."*

Closing line: **"That task id is durable. A different process, or the same process
tomorrow, can pick it up."**

---

## Beat 4 — optional, if you have time, after slide 10

```bash
dotnet run --project dotnet/A2A.Demo.Console -- stream
```

**~15 seconds, and it is 12 of them watching a progress bar.** Run it only if the
room is engaged — it is the prettiest output in the set but the least surprising.

The thing to say: this is the **same server-side work** as the polling demo. Nothing
changed but `RunStreamingAsync`. The `«` lines are real A2A events —
`TaskStatusUpdate` carrying progress, `TaskArtifactUpdate` carrying content, with
`append` and `lastChunk` visible.

If you are short on time, cut this and describe it instead. Beat 3 makes the point.

---

## Beat 5 — after slide 21 (Architecture & Patterns)

**Needs Azure OpenAI.** Skip it if unconfigured — it prints a clean "not configured"
message rather than an exception, but do not discover that live.

```bash
dotnet run --project dotnet/A2A.Demo.Console -- delegate
```

**~20 seconds.** One line of setup does the work:

```csharp
AIFunction remoteAsTool = remote.AsAIFunction(new AIFunctionFactoryOptions { … });
```

*"The local agent sees a tool. The remote agent sees an A2A message. Neither knows
anything about the other's model or framework."*

Then the punchline for the vendor-flexibility slide: **"Swap the remote agent for a
partner's compatible agent and this code does not change."**

---

## Beat 6 — the host side, if someone asks "what did that cost you to build?"

Show [Program.cs](dotnet/A2A.Demo.HostedAgent/Program.cs) and contrast the two paths:

```csharp
// one line, no handler, no lifecycle code
app.MapA2AJsonRpc(agent, "/a2a/simple");
```

Then hit it live to prove it is real:

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

*"Same agent, two bindings, and the caller picks from the card."*

---

## Beat 7 — cross-language, and the one to keep if you cut anything

Two moves, both cheap, and together they are the strongest argument in the deck.

**7a. Python client, .NET agent.** Nothing on the server changed.

```bash
cd python && .venv\Scripts\python.exe a2a_console.py job
```

*"Same remote agent. It does not know or care that this caller is Python."*
**~20 seconds.**

**7b. Flip it — .NET client, Python agent.** Start the Python host in a third
terminal (or leave it running from the start):

```bash
cd python && python a2a_host.py          # http://localhost:5402
```

Then point the .NET console at it:

```bash
A2A__BaseUrl=http://localhost:5402 dotnet run --project dotnet/A2A.Demo.Console -- card job
```

**~25 seconds.** The card demo shows a *different* agent name — "Contoso Research
Agent (Python)" — so the room can see the swap actually happened. Then the job demo
runs the identical lifecycle: background start, continuation token, six polls,
`Completed`.

The line to land: **"A .NET client just drove a Python agent's task lifecycle. No
adapter, no shared library, no shared types. It read a card and made calls."**

If the room is quiet, show the matrix instead of running both:

|                    | → .NET host | → Python host |
| ------------------ | ----------- | ------------- |
| **.NET client**    | ✅           | ✅             |
| **Python client**  | ✅           | ✅             |

---

## Q&A ammunition

**"Is this production ready?"**
The protocol core is 1.0 and behaved exactly as specified. The Agent Framework's A2A
packages are all pre-release, and two APIs this demo uses are marked evaluation-only.
See the maturity notes in the README — quoting the specific drift you hit is more
credible than a general hedge.

**"How is this different from just calling their REST API?"**
Point back at the card demo. Discovery, capability declaration, and a defined task
lifecycle come for free. With a bespoke API you write all three yourself, per vendor.

**"What about auth?"**
The card advertises security schemes; the client sends a bearer token
(`A2A:BearerToken` in the console's config). The gateway pattern from the Akumina
build adds Entra ID JWT validation on the JSON-RPC endpoint with the card left
anonymous so callers can discover how to authenticate.

**"What if the remote agent goes away mid-task?"**
The task id is the handle. `tasks/get` is a plain request — reconnecting is not a
special case, and that is why the polling pattern matters more than streaming for
anything genuinely long.

**"Did you have to write adapters to make .NET and Python talk?"**
No. One line: the Python card has to set `protocolVersion` explicitly, because
protobuf omits empty strings and the .NET SDK marks the field required. That is the
entire interop cost, and it is an SDK quirk, not a protocol one. The maturity notes
in the README list the others I hit building the same host twice.

**"Why not just MCP?"**
Slide 5. MCP gives one agent its tools; A2A lets independent agents delegate. The
`delegate` demo is literally both shapes at once — the remote agent is exposed to the
local one *as a tool*, but it is a full agent on the other side of the call.
