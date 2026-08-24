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
from pathlib import Path
from typing import Sequence

import httpx
from a2a.client import A2ACardResolver
from a2a.types import AgentCard
from agent_framework import BaseAgent
from agent_framework_a2a import A2AAgent
from dotenv import load_dotenv

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

    The counterweight to the A2A factory: same return type, entirely different
    execution model. Requires ``pip install agent-framework-azure-ai``.
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

    async def create_agent(self) -> BaseAgent:
        if not self.is_configured:
            raise RuntimeError(self.configuration_hint)

        # Imported lazily so the A2A demos run without the Azure extra installed.
        from agent_framework.azure import AzureOpenAIChatClient

        client = AzureOpenAIChatClient(
            endpoint=self._endpoint,
            api_key=self._api_key,
            deployment_name=self._deployment,
        )
        return client.create_agent(
            name="LocalCoordinator",
            instructions="You are a helpful coordinator agent running locally. Be concise.",
        )


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
    Ux.info(f"Streaming   : {getattr(card.capabilities, 'streaming', None)}")

    Ux.heading("Interfaces the card advertises")
    for iface in card.supported_interfaces or []:
        Ux.info(f"{iface.protocol_binding:<10} {iface.url}")

    Ux.heading("Skills the card advertises")
    for skill in card.skills or []:
        Ux.content(skill.id)
        Ux.info(f"    {skill.description}")
        Ux.info(f"    tags: {', '.join(skill.tags or [])}")


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


DEMOS = {
    "card": ("Discovery -- read the Agent Card", demo_card),
    "ask": ("Request / response -- call the remote agent", demo_ask),
    "stream": ("Streaming -- watch a long job report progress live", demo_stream),
    "job": ("Long-running job -- start, walk away, come back", demo_job),
}


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

    requested = argv or list(DEMOS)

    try:
        for key in requested:
            if key not in DEMOS:
                Ux.error(f"Unknown demo '{key}'. Known: {', '.join(DEMOS)}.")
                continue

            title, run = DEMOS[key]
            Ux.banner(title, f"python a2a_console.py {key}")
            try:
                await run(provider)
            except httpx.HTTPError as exc:
                Ux.error(f"Could not reach the remote agent: {exc}")
                Ux.info("Start it with:  dotnet run --project ../dotnet/A2A.Demo.HostedAgent")
            except Exception as exc:  # noqa: BLE001 - demo surface, keep the console alive
                Ux.error(f"{type(exc).__name__}: {exc}")
    finally:
        await a2a_factory.aclose()

    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main(sys.argv[1:])))
