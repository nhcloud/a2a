# A2A Knowledge Check

Based on **"When AI Agents Work Together: Understanding A2A, MCP, and the Future of Connected AI"** by Udaiappa Ramachandran (Udai) — practical demonstrations with the Microsoft Agent Framework.

Ten multiple-choice questions. One correct answer each unless noted. The answer key with explanations is at the end.

---

## Questions

### 1. The problem A2A solves
The deck describes today's agent landscape as "Islands of Intelligence." Which statement is **not** one of the four symptoms it lists?

- **A.** Every team, vendor, and framework ships its own agent
- **B.** Each agent is a silo with its own API, memory, tools, and prompts
- **C.** Agents cannot share a single underlying foundation model
- **D.** There is no standard way to discover, call, or trust another agent

---

### 2. A2A vs. MCP
Which pairing correctly matches the deck's metaphor?

- **A.** MCP is the agent's phone; A2A is the agent's hands
- **B.** MCP is the agent's hands; A2A is the agent's phone
- **C.** MCP is the agent's memory; A2A is the agent's voice
- **D.** MCP and A2A are competing standards for the same problem

---

### 3. Governance and backing
Which statement about A2A's origin and status is correct?

- **A.** Created by Microsoft, donated to the Cloud Native Computing Foundation, currently at v0.9
- **B.** Created by Google, donated to the Linux Foundation, reached v1.0
- **C.** Created by the Linux Foundation, with Google as the reference implementer, at v1.0
- **D.** Created by a joint AWS/IBM working group, donated to Apache, at v1.0

---

### 4. What actually makes an interaction A2A
Your team calls a Gemini agent through the Google SDK and a second agent through a Bedrock REST API. Is this A2A?

- **A.** Yes — both are agent-to-agent calls, so the protocol is satisfied
- **B.** Yes, but only if both providers use the same authentication scheme
- **C.** No — it is the **protocol interface** that makes an interaction A2A, not the model, SDK, framework, or provider
- **D.** No — A2A requires all participating agents to run on the same framework

---

### 5. The core actors
Which describes the **A2A Server**?

- **A.** The human or automated service that defines the goal and starts the interaction
- **B.** The calling agent that acts on the user's behalf and initiates the A2A call
- **C.** The remote agent that exposes an HTTP endpoint and remains opaque
- **D.** A central registry that brokers all traffic between client and remote agents

---

### 6. "Opaque" as the trust unlock
Why does the deck call the remote agent's opacity "the trust unlock"?

- **A.** It encrypts all A2A traffic end to end
- **B.** It lets organizations collaborate without exposing their IP or internal state
- **C.** It hides the caller's identity from the remote agent
- **D.** It guarantees the remote agent cannot log or retain request data

---

### 7. The vocabulary
Which term/definition pair is **wrong**?

- **A.** **Agent Card** — a JSON "business card": identity, endpoint, skills, auth requirements
- **B.** **Part** — the content inside a message: text, file, or structured data
- **C.** **Artifact** — a concrete deliverable produced during a task (doc, image, data)
- **D.** **Task** — a single turn of conversation, with a role of user or agent

---

### 8. Discovery
How do A2A clients find and size up a remote agent?

- **A.** They call a mandatory central A2A registry that all servers must publish to
- **B.** They read the server's published JSON Agent Card first, then decide fit and how to call — available at a well-known path, or via registries or direct config
- **C.** They send a probe request and inspect the error response for capabilities
- **D.** They negotiate capabilities during a TLS handshake extension

---

### 9. Transports
Which set of transport bindings does A2A 1.0 define?

- **A.** REST, GraphQL, and WebSockets
- **B.** JSON-RPC, gRPC, and HTTP+JSON
- **C.** gRPC and AMQP only
- **D.** JSON-RPC over WebSockets, with gRPC in preview

---

### 10. Result-delivery patterns
A job runs for hours and the client cannot hold a connection open. Which delivery pattern fits, and why?

- **A.** Request/response — the simplest pattern always applies
- **B.** Streaming (SSE) — it delivers real-time incremental updates
- **C.** Push notifications — the server calls your webhook, so no open connection is needed
- **D.** Streaming (SSE) with automatic reconnect, which A2A mandates for long jobs

---

### 11. Message vs. Task
When does an A2A agent return a **Task** rather than a **Message**?

- **A.** Whenever the response contains more than one Part
- **B.** For long-running work — it carries an ID and a trackable lifecycle, with artifacts returned or updated as work progresses
- **C.** Whenever the transport binding is gRPC rather than JSON-RPC
- **D.** Only when the client explicitly requests push notifications

---

### 12. contextId
What does `contextId` do?

- **A.** Uniquely identifies a single request for idempotency
- **B.** Carries the caller's auth token between agents
- **C.** Groups related messages and tasks into one conversation
- **D.** Identifies which Agent Card version the client resolved

---

### 13. Maturity
Which item is **still varying by implementation** rather than stable in A2A 1.0?

- **A.** Agent Cards
- **B.** Tasks, messages, parts, and artifacts
- **C.** The JSON-RPC, gRPC, and HTTP+JSON bindings
- **D.** Multi-tenancy and identity propagation

---

### 14. Microsoft Agent Framework
Which statement about the Microsoft Agent Framework is correct?

- **A.** It is the direct successor to Semantic Kernel and AutoGen, supports .NET and Python with Go in public preview, and treats A2A as a first-class integration
- **B.** It is a rebrand of Semantic Kernel, .NET only, with A2A available as a community add-on
- **C.** It replaces MCP with A2A as its single integration standard
- **D.** It supports Python only, with .NET in public preview

---

### 15. The APIs
Match the sample to the API. In the deck's samples, which call **exposes** a .NET agent over A2A, and which **consumes** a remote agent from Python?

- **A.** Expose: `A2AExecutor(agent)` — Consume: `app.MapA2A(...)`
- **B.** Expose: `app.MapA2A(agent, "/a2a/pirate", agentCard: new() { ... })` — Consume: `A2AAgent(agent_card=card, url=host)`
- **C.** Expose: `A2ACardResolver(...)` — Consume: `DefaultRequestHandler(...)`
- **D.** Expose: `app.UseA2A()` — Consume: `A2AClient.Connect(host)`

---

## Answer Key

| # | Answer |
|---|---|
| 1 | **C** |
| 2 | **B** |
| 3 | **B** |
| 4 | **C** |
| 5 | **C** |
| 6 | **B** |
| 7 | **D** |
| 8 | **B** |
| 9 | **B** |
| 10 | **C** |
| 11 | **B** |
| 12 | **C** |
| 13 | **D** |
| 14 | **A** |
| 15 | **B** |

---

## Explanations

**1 — C.** The four symptoms are: everyone is building agents; each is a silo (own API, memory, tools, prompts); collaboration means glue code for every pairing; and no common language for discovery, calling, or trust. Sharing a foundation model was never the problem — A2A is explicitly model-neutral.

**2 — B.** MCP is agent → **tool**: it connects an agent to APIs, data, and resources — the agent's **hands**. A2A is agent → **agent**: it lets independent agents discover and delegate to each other — the agent's **phone**. They are complementary, not competing; the deck says to use both together. Remember also what A2A is *not*: not an agent framework, not a tool-call protocol, not a chat app.

**3 — B.** Created by Google and donated to the **Linux Foundation** for open governance. It reached **v1.0**, with SDKs across Python, JavaScript, Java, C#/.NET, Go, and Rust. Backers include AWS, Cisco, Google, IBM, Microsoft, Salesforce, SAP, and ServiceNow.

**4 — C.** This is the deck's most easily-missed point: *it is the protocol interface that makes an interaction A2A — not the model, SDK, framework, or provider.* Vendor-SDK integration (Bedrock, Gemini, OpenAI, Microsoft Foundry, Copilot Studio) means provider-specific auth and request models, possibly proprietary discovery, and integration changes when you switch providers. A real A2A call hits an A2A-compliant endpoint through a standard interface, uses an Agent Card for skills/endpoints/security, exchanges standard messages, tasks, artifacts, and operations, and decouples the caller from the agent's framework. Note D is wrong for the opposite reason — A2A exists precisely so agents on *different* frameworks can work together.

**5 — C.** The three actors: **User** (human or automated service; defines the goal, starts the interaction), **A2A Client** (the calling agent; acts on the user's behalf, initiates the call), **A2A Server** (the remote agent; exposes an HTTP endpoint, opaque). There is no mandatory central broker — D describes an architecture A2A does not require.

**6 — B.** Opacity means the remote agent's prompts, tools, memory, and internal state never leave its boundary — only the standardized protocol surface is exposed. That is what makes cross-team and cross-vendor collaboration acceptable: you can call a partner's agent without either side surrendering IP. (The deck does note that data disclosure still follows app policy — opacity is about implementation, not a blanket data guarantee.)

**7 — D.** That is the definition of a **Message**. A **Task** is a stateful unit of work with an ID and a lifecycle, used for long-running jobs.

**8 — B.** Every A2A Server publishes an Agent Card as JSON declaring name, URL, streaming support, skills, and auth. The **card-first flow**: read the card, *then* decide fit and how to call. A **well-known path** is the standard discovery location; registries and direct config are also supported — so no central registry is mandatory. In the .NET sample, `GET .../v1/card` returns the card.

**9 — B.** A2A 1.0 defines **JSON-RPC, gRPC, and HTTP+JSON** bindings, secured via standard transport-level auth.

**10 — C.** **Push notifications** — the server calls your webhook, so no open connection is needed; this is the pattern for very long-running or disconnected work. Streaming (SSE) holds an open HTTP connection and is for live UX; request/response is simplest and can poll, but neither fits a client that cannot stay connected for hours.

**11 — B.** A **Message** comes back when the agent can answer immediately — a single turn, quick Q&A. A **Task** comes back for long-running work, with an ID, a trackable lifecycle, and artifacts returned or updated as progress is made. In the Python sample this is `stream=True` (live, Message-style UX) versus `background=True` (Task lifecycle, polled later with `poll_task(continuation_token)`).

**12 — C.** `contextId` groups related messages **and** tasks into one conversation. Reuse the same `contextId` on a later request to continue that conversation, whichever form the individual exchanges took.

**13 — D.** **Stable in 1.0:** core operations and data model, Agent Cards, tasks/messages/parts/artifacts, the three bindings, and the streaming/polling/push patterns. **Still varying:** SDK and framework package stability, provider-native endpoint availability, **multi-tenancy and identity propagation**, operational tooling and interoperability testing, and framework-specific adapters. Build on the stable core; expect maturity to differ by SDK and provider.

**14 — A.** The Agent Framework is the direct successor to Semantic Kernel and AutoGen for agent development and orchestration. It supports .NET and Python, with Go in public preview, and offers A2A in both directions — **expose** your agents so any A2A client can call them, and **consume** remote A2A agents as if they were local. A2A is a core integration *alongside* MCP, AG-UI, and M365 — it does not replace MCP (ruling out C).

**15 — B.** Sample 1 (.NET) exposes an agent with one mapping call: `app.MapA2A(agent, "/a2a/pirate", agentCard: new() { Name = "Pirate Agent", Version = "1.0" })`. Sample 2 (Python) consumes a remote agent by resolving its card with `A2ACardResolver` and wrapping the endpoint in `A2AAgent(agent_card=card, url=host)` — install with `pip install agent-framework-a2a`. Sample 3 is the mirror image of Sample 2: `A2AExecutor(agent)` passed to a `DefaultRequestHandler` serves *your* Python agent over A2A.

---

## Quick reference

**Architecture patterns**

- **Delegation** — a host agent routes sub-tasks to specialized remote agents.
- **Agents as Tools** — wrap remote A2A agents as callable tools of a host agent.
- **Cross-Framework Mesh** — mix Agent Framework, LangGraph, CrewAI, and custom agents.
- **MCP + A2A Together** — each agent uses MCP for tools, A2A to collaborate.

**Worked example** — a customer wants to replace a damaged product. The **Support Agent** coordinates and delegates over A2A to an **Order Agent** (verifies the purchase), a **Policy Agent** (checks eligibility), and a **Shipping Agent** (arranges the replacement). Each is built and managed independently; swapping the shipping agent for a compatible partner agent takes minimal changes elsewhere.

**Resources**

- A2A Protocol & Spec — [a2a-protocol.org](https://a2a-protocol.org)
- Agent Framework A2A Docs — [learn.microsoft.com/agent-framework/integrations/a2a](https://learn.microsoft.com/agent-framework/integrations/a2a)
- Agent Framework Repo — [github.com/microsoft/agent-framework](https://github.com/microsoft/agent-framework) (Python A2A samples under `python/samples/04-hosting/a2a`)
- A2A SDKs — [github.com/a2aproject](https://github.com/a2aproject)
- A2A Samples — [github.com/a2aproject/a2a-samples](https://github.com/a2aproject/a2a-samples)
- This repo — [github.com/nhcloud/a2a](https://github.com/nhcloud/a2a)

> **The takeaway:** A2A lets independent agents collaborate without exposing their internal implementation.
