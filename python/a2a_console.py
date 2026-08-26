"""Microsoft Agent Framework (Python) calling the same A2A agent as the .NET console demo.

Mirrors the .NET sample beat for beat -- discovery, request/response, streaming, and a
long-running job -- so the talk can show one remote agent serving two ecosystems.

Run the .NET host first:
    dotnet run --project ../dotnet/A2A.Demo.HostedAgent
"""

from __future__ import annotations

import asyncio
import os
import sys
import time
from abc import ABC, abstractmethod
from dataclasses import dataclass
from pathlib import Path
from typing import Awaitable, Callable, Sequence

import httpx
from a2a.client import A2ACardResolver
from a2a.types import AgentCard
from agent_framework import BaseAgent
from agent_framework_a2a import A2AAgent
from dotenv import load_dotenv

# Match the .NET console's Console.OutputEncoding = UTF8. Without this, Windows
# defaults to cp1252 and model prose comes back with mangled dashes and quotes.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# Copy the repo root's .env.template to python/.env to configure this.
load_dotenv(Path(__file__).resolve().parent / ".env", override=False)

BASE_URL = os.environ.get("A2A_BASE_URL", "http://localhost:5401")
AGENT_CARD_PATH = os.environ.get("A2A_CARD_PATH", "/.well-known/agent-card.json")
JOB = "Research the market for AI agent interoperability tooling."


# -- console formatting ---------------------------------------------------------

class Ux:
    CYAN, GRAY, WHITE, GREEN, YELLOW, RED, MAGENTA, RESET = (
        "\033[36m", "\033[90m", "\033[97m", "\033[32m",
        "\033[33m", "\033[31m", "\033[35m", "\033[0m",
    )

    @staticmethod
    def banner(title: str, subtitle: str) -> None:
        print(f"\n{Ux.CYAN}{'=' * 74}")
        print(f"  {title}")
        print(f"{Ux.GRAY}  {subtitle}")
        print(f"{Ux.CYAN}{'=' * 74}{Ux.RESET}\n")

    @staticmethod
    def heading(text: str) -> None:
        print(f"\n{Ux.CYAN}-- {text} {'-' * max(0, 68 - len(text))}{Ux.RESET}")

    @staticmethod
    def info(text: str) -> None:
        print(f"{Ux.GRAY}  {text}{Ux.RESET}")

    @staticmethod
    def step(text: str) -> None:
        print(f"{Ux.GRAY}  > {text}{Ux.RESET}")

    @staticmethod
    def wire(text: str) -> None:
        print(f"{Ux.YELLOW}  << {text}{Ux.RESET}")

    @staticmethod
    def content(text: str) -> None:
        print(f"{Ux.WHITE}  {text}{Ux.RESET}")

    @staticmethod
    def success(text: str) -> None:
        print(f"{Ux.GREEN}  OK {text}{Ux.RESET}")

    @staticmethod
    def warn(text: str) -> None:
        print(f"{Ux.YELLOW}  ! {text}{Ux.RESET}")

    @staticmethod
    def error(text: str) -> None:
        print(f"{Ux.RED}  X {text}{Ux.RESET}")

    @staticmethod
    def prompt(text: str) -> None:
        print(f"\n{Ux.MAGENTA}  you -> {text}{Ux.RESET}")

    @staticmethod
    def agent(text: str) -> None:
        body = "\n".join(f"  {line}" for line in text.splitlines())
        print(f"\n{Ux.WHITE}{body}{Ux.RESET}\n")


# -- factories ------------------------------------------------------------------

class AgentFactory(ABC):
    """Produces an Agent Framework agent from some backing source.

    Same contract as the .NET ``IAgentFactory``: callers get an agent and never
    learn whether it runs locally or three continents away.
    """

    @property
    @abstractmethod
    def key(self) -> str: ...

    @property
    @abstractmethod
    def display_name(self) -> str: ...

    @property
    def is_configured(self) -> bool:
        return True

    @property
    def configuration_hint(self) -> str | None:
        return None

    @abstractmethod
    async def create_agent(self) -> BaseAgent: ...


class A2AAgentFactory(AgentFactory):
    """Wraps a remote A2A endpoint as a local agent, card first."""

    def __init__(self, base_url: str = BASE_URL, card_path: str = AGENT_CARD_PATH) -> None:
        self._base_url = base_url.rstrip("/")
        self._card_path = card_path
        self._http = httpx.AsyncClient(timeout=httpx.Timeout(300.0))
        self._card: AgentCard | None = None
        self._agent: A2AAgent | None = None

    @property
    def key(self) -> str:
        return "a2a"

    @property
    def display_name(self) -> str:
        return f"Remote A2A agent ({self._base_url})"

    @property
    def card(self) -> AgentCard | None:
        return self._card

    async def resolve_card(self) -> AgentCard:
        resolver = A2ACardResolver(
            httpx_client=self._http,
            base_url=self._base_url,
            agent_card_path=self._card_path,
        )
        self._card = await resolver.get_agent_card()
        return self._card

    async def create_agent(self) -> A2AAgent:
        if self._agent is None:
            card = self._card or await self.resolve_card()
            self._agent = A2AAgent(agent_card=card, url=self._base_url, http_client=self._http)
        return self._agent

    async def aclose(self) -> None:
        await self._http.aclose()


class AzureOpenAIAgentFactory(AgentFactory):
    """Builds an agent that runs in this process against Azure OpenAI.

    The counterweight to the A2A factory: same return type, same call site,
    entirely different execution model. This one owns the model, the prompt, and
    the conversation state; the A2A one owns none of them.
    """

    def __init__(self) -> None:
        self._endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT")
        self._api_key = os.environ.get("AZURE_OPENAI_API_KEY")
        # No default: the deployment name comes from python/.env only.
        self._deployment = os.environ.get("AZURE_OPENAI_DEPLOYMENT")

    @property
    def key(self) -> str:
        return "azure-openai"

    @property
    def display_name(self) -> str:
        if not self._deployment:
            return "Local Azure OpenAI agent (no deployment configured)"
        return f"Local Azure OpenAI agent ({self._deployment})"

    @property
    def is_configured(self) -> bool:
        return bool(self._endpoint and self._api_key and self._deployment)

    @property
    def configuration_hint(self) -> str | None:
        if self.is_configured:
            return None
        return (
            "Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY and "
            "AZURE_OPENAI_DEPLOYMENT in python/.env."
        )

    def _chat_client(self):
        """Azure AI Foundry and Azure OpenAI both serve an OpenAI-compatible
        ``/openai/v1`` surface, so one client covers both endpoint styles -- the
        same trick the .NET factory uses.
        """
        if not self.is_configured:
            raise RuntimeError(self.configuration_hint)

        # Imported lazily so the A2A demos run without the OpenAI extra installed.
        from agent_framework.openai import OpenAIChatClient

        endpoint = self._endpoint.rstrip("/")  # type: ignore[union-attr]
        if not endpoint.lower().endswith("/openai/v1"):
            endpoint += "/openai/v1"

        return OpenAIChatClient(self._deployment, api_key=self._api_key, base_url=endpoint)

    async def create_agent(self) -> BaseAgent:
        from agent_framework import Agent

        return Agent(
            self._chat_client(),
            "You are a helpful coordinator agent running locally. Be concise.",
            name="LocalCoordinator",
        )

    def create_agent_with_tools(self, name: str, instructions: str, tools: list) -> BaseAgent:
        """Builds an agent with an explicit tool list.

        Used by the delegation demo to hand a remote A2A agent to a local one as a
        callable tool. Mirrors the .NET ``CreateAgentWithTools``.
        """
        from agent_framework import Agent

        return Agent(self._chat_client(), instructions, name=name, tools=tools)


class AgentFactoryProvider:
    """Resolves a factory by key, so demos stay ignorant of the backend."""

    def __init__(self, factories: Sequence[AgentFactory]) -> None:
        self._factories = {f.key: f for f in factories}

    @property
    def all(self) -> list[AgentFactory]:
        return list(self._factories.values())

    def get(self, key: str) -> AgentFactory:
        if key not in self._factories:
            raise KeyError(f"No agent factory registered for '{key}'.")
        return self._factories[key]

    async def create(self, key: str) -> BaseAgent:
        return await self.get(key).create_agent()


# -- demos ----------------------------------------------------------------------

async def demo_card(provider: AgentFactoryProvider) -> None:
    """Card-first discovery -- read the business card before calling."""
    factory: A2AAgentFactory = provider.get("a2a")  # type: ignore[assignment]
    Ux.heading(f"GET {AGENT_CARD_PATH}")

    card = await factory.resolve_card()
    Ux.info(f"Name        : {card.name}")
    Ux.info(f"Version     : {card.version}")
    Ux.info(f"Description : {card.description}")
    Ux.info(f"Streaming   : {card.capabilities.streaming}")
    Ux.info(f"Push notify : {card.capabilities.push_notifications}")
    Ux.info(f"Input modes : {', '.join(card.default_input_modes)}")
    Ux.info(f"Output modes: {', '.join(card.default_output_modes)}")

    Ux.heading("Interfaces the card advertises")
    for iface in card.supported_interfaces:
        Ux.info(f"{iface.protocol_binding:<10} {iface.url}  (protocol {iface.protocol_version})")

    Ux.heading("Skills the card advertises")
    for skill in card.skills:
        Ux.content(skill.id)
        Ux.info(f"    {skill.description}")
        Ux.info(f"    tags: {', '.join(skill.tags)}")
        for example in skill.examples:
            Ux.info(f'    e.g. "{example}"')

    if card.security_schemes:
        Ux.heading("Security schemes")
        for name, scheme in card.security_schemes.items():
            Ux.info(f"{name}: {scheme.WhichOneof('scheme')}")
    else:
        Ux.warn("No security schemes advertised -- fine for a demo, not for production.")

    Ux.heading("Raw card")
    # Serialized through protobuf's own JSON mapping, so this is the actual wire
    # format -- camelCase, protocol enum names and all -- not Python's snake_case.
    from google.protobuf.json_format import MessageToJson

    Ux.wire(MessageToJson(card, indent=2))


async def demo_ask(provider: AgentFactoryProvider) -> None:
    """Request / response -- one message in, one message out."""
    agent = await provider.create("a2a")

    Ux.heading("The agent, from the caller's point of view")
    Ux.info(f"Type        : {type(agent).__name__}")
    Ux.info(f"Name        : {agent.name}")
    Ux.step("An Agent Framework agent like any other. The network hop is an implementation detail.")

    session = agent.create_session()

    for turn in (
        "What are your capabilities?",
        "Which of those would you use for a job that takes ten minutes?",
    ):
        Ux.prompt(turn)
        started = time.perf_counter()
        response = await agent.run(turn, session=session)
        Ux.agent(response.text)
        Ux.step(f"answered in {(time.perf_counter() - started) * 1000:.0f} ms")

    Ux.success("Both turns shared one contextId -- that is what made it a conversation.")


async def demo_stream(provider: AgentFactoryProvider) -> None:
    """Streaming -- watch a long job report progress live over SSE."""
    agent = await provider.create("a2a")
    session = agent.create_session()

    Ux.prompt(JOB)
    Ux.step("stream=True -- hold the connection open and take updates as they land.")
    print()

    started = time.perf_counter()
    events = chunks = characters = 0

    async for update in agent.run(JOB, stream=True, session=session):
        events += 1
        at = f"[{time.perf_counter() - started:5.1f}s]"
        raw = getattr(update, "raw_representation", None)
        kind = type(raw).__name__ if raw is not None else "update"

        if update.text:
            chunks += 1
            characters += len(update.text)
            stripped = update.text.strip()
            first = stripped.splitlines()[0] if stripped else ""
            Ux.wire(f"{at} {kind}")
            Ux.content(f"        chunk {chunks}: {first[:60]} (+{len(update.text)} chars)")
        else:
            Ux.wire(f"{at} {kind}")

    elapsed = time.perf_counter() - started
    print()
    Ux.step(f"{events} protocol events | {chunks} carried report content | "
            f"{characters} chars | {elapsed:.1f}s")
    Ux.success("Same job as the polling demo. The caller just chose to stay on the line.")


async def demo_job(provider: AgentFactoryProvider) -> None:
    """Long-running job -- start in the background, poll with a continuation token."""
    agent: A2AAgent = await provider.create("a2a")  # type: ignore[assignment]
    session = agent.create_session()

    Ux.heading("1. Start the job without waiting for it")
    Ux.prompt(JOB)
    Ux.step("background=True -- tell the agent we will come back for this.")

    started = time.perf_counter()
    response = await agent.run(JOB, session=session, background=True)
    Ux.success(f"Returned in {(time.perf_counter() - started) * 1000:.0f} ms.")

    token = response.continuation_token
    if token is None:
        Ux.warn("The agent answered immediately -- no task was created, so there is nothing to poll.")
        Ux.agent(response.text)
        return

    Ux.info(f"continuation token: {dict(token)}")
    Ux.step("The work is running on the remote agent. This process is free.")

    Ux.heading("2. Poll with the continuation token")
    total = time.perf_counter()
    poll = 0

    while token is not None:
        await asyncio.sleep(2)
        poll += 1
        response = await agent.poll_task(token)
        token = response.continuation_token
        Ux.info(f"poll {poll:2} | t+{time.perf_counter() - total:5.1f}s | "
                f"{len(response.text):5} chars so far"
                + ("" if token else " | done"))

    Ux.success(f"Task finished after {poll} polls ({time.perf_counter() - total:.1f}s).")
    Ux.heading("The finished artifact")
    Ux.agent(response.text)
    Ux.step("The Task outlived the request that created it -- a taskId is a durable handle.")


async def demo_delegate(provider: AgentFactoryProvider) -> None:
    """Agents as tools -- a local agent decides, on its own, to hand work to a remote one.

    The local agent has no idea it is making a network call. It sees a tool. The
    remote agent has no idea it is being orchestrated. It sees an A2A message.
    Neither knows the other's model, framework, or prompts -- the contract is the
    Agent Card and nothing else.
    """
    azure: AzureOpenAIAgentFactory = provider.get("azure-openai")  # type: ignore[assignment]

    if not azure.is_configured:
        Ux.warn("This demo needs a local model to do the orchestrating.")
        Ux.info(azure.configuration_hint)
        return

    # The remote agent, reached over A2A.
    remote = await provider.create("a2a")

    # One call turns it into a tool the local agent can invoke.
    remote_as_tool = remote.as_tool(
        name="nashua_research_agent",
        description=(
            "Delegates a question or a research request to the Nashua Research Agent, "
            "a specialist agent reachable over A2A. Use it for anything involving market "
            "research, competitive analysis, or reports."
        ),
    )

    Ux.heading("Wiring")
    Ux.info(f"Local agent  : {azure.display_name}")
    Ux.info(f"Remote agent : {remote.name} (over A2A)")
    Ux.step(f'Exposed to the local agent as tool "{remote_as_tool.name}".')

    coordinator = azure.create_agent_with_tools(
        name="Coordinator",
        instructions=(
            "You are a coordinator. You have no research ability of your own.\n"
            "Whenever the user asks anything that needs research, market knowledge,\n"
            "or a report, call the nashua_research_agent tool and relay what it\n"
            "returns. Say which agent produced the answer."
        ),
        tools=[remote_as_tool],
    )

    session = coordinator.create_session()

    request = ("I need to understand the market for AI agent interoperability tooling. "
               "Get me the key points.")
    Ux.prompt(request)

    response = await coordinator.run(request, session=session)
    Ux.agent(response.text)

    tool_calls = sum(
        1
        for message in response.messages
        for content in message.contents
        if getattr(content, "type", None) == "function_call"
    )

    Ux.step(f"{tool_calls} delegated call(s) crossed the A2A boundary during that turn.")
    Ux.success("Swap the remote agent for a partner's compatible agent and this code does not change.")


@dataclass(frozen=True)
class Demo:
    """One runnable beat of the demo -- the .NET ``IDemoScenario``, minus the DI."""

    key: str
    """Menu key, also accepted as a command-line argument."""

    title: str
    summary: str
    """The point this demo makes, shown under the title."""

    run: Callable[[AgentFactoryProvider], Awaitable[None]]


DEMOS: list[Demo] = [
    Demo("card",
         "Discovery -- read the Agent Card",
         "Fetch the remote agent's business card before calling it.",
         demo_card),
    Demo("ask",
         "Request / response -- call the remote agent",
         "One message in, one message out, over A2A.",
         demo_ask),
    Demo("stream",
         "Streaming -- watch a long job report progress live",
         "SSE: task status transitions and artifact chunks as they happen.",
         demo_stream),
    Demo("job",
         "Long-running job -- start, walk away, come back",
         "Background start, continuation-token polling, and the Task on the wire.",
         demo_job),
    Demo("delegate",
         "Delegation -- a local agent calls the remote one as a tool",
         "Azure OpenAI agent orchestrates; the A2A agent executes.",
         demo_delegate),
]

DEMOS_BY_KEY = {demo.key: demo for demo in DEMOS}


async def run_demo(provider: AgentFactoryProvider, key: str) -> None:
    """Run one demo by key, keeping the console alive whatever it throws."""
    demo = DEMOS_BY_KEY.get(key.strip().lower())
    if demo is None:
        Ux.error(f"Unknown demo '{key}'. Known: {', '.join(DEMOS_BY_KEY)}.")
        return

    Ux.banner(demo.title, demo.summary)

    try:
        await demo.run(provider)
    except (asyncio.CancelledError, KeyboardInterrupt):
        Ux.warn("Canceled.")
    except httpx.HTTPError as exc:
        Ux.error(f"Could not reach the remote agent: {exc}")
        Ux.info("Start it with:  dotnet run --project ../dotnet/A2A.Demo.HostedAgent")
    except Exception as exc:  # noqa: BLE001 - demo surface, keep the console alive
        Ux.error(f"{type(exc).__name__}: {exc}")


async def read_choice() -> str | None:
    """Prompt for a menu selection. ``None`` means quit (EOF or Ctrl+C)."""
    try:
        return (await asyncio.to_thread(input, f"\n{Ux.CYAN}  select -> {Ux.RESET}")).strip()
    except (EOFError, KeyboardInterrupt):
        print()
        return None


async def menu(provider: AgentFactoryProvider) -> None:
    """The interactive loop -- same shape as the .NET console's."""
    while True:
        Ux.heading("Demos")
        for index, demo in enumerate(DEMOS, start=1):
            Ux.content(f"{index}. {demo.title}")
            Ux.info(f"   {demo.summary}")
        Ux.content("q. quit")

        choice = await read_choice()
        if not choice or choice.lower() == "q":
            return

        chosen = (
            DEMOS[int(choice) - 1]
            if choice.isdigit() and 1 <= int(choice) <= len(DEMOS)
            else DEMOS_BY_KEY.get(choice.lower())
        )

        if chosen is None:
            Ux.error(f"No demo matches '{choice}'.")
            continue

        await run_demo(provider, chosen.key)


async def main(argv: list[str]) -> int:
    a2a_factory = A2AAgentFactory()
    provider = AgentFactoryProvider([a2a_factory, AzureOpenAIAgentFactory()])

    Ux.banner(
        "A2A + Microsoft Agent Framework (Python)",
        f"Calling the same remote agent as the .NET demo. Target: {BASE_URL}",
    )

    Ux.heading("Registered agent factories")
    for factory in provider.all:
        if factory.is_configured:
            Ux.success(f"{factory.key:<14} {factory.display_name}")
        else:
            Ux.warn(f"{factory.key:<14} not configured -- {factory.configuration_hint}")

    try:
        # Non-interactive mode: pass demo keys as arguments, or "all".
        if argv:
            requested = list(DEMOS_BY_KEY) if argv[0].lower() == "all" else argv
            for key in requested:
                await run_demo(provider, key)
            return 0

        await menu(provider)
    finally:
        await a2a_factory.aclose()

    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main(sys.argv[1:])))
