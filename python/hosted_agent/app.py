"""Builds the Starlette app. The mirror of the .NET host's ``Program.cs``.

Publishes the same agent twice, so the talk can contrast the two hosting styles:

* **Full control** -- a custom ``AgentExecutor`` that routes to skills and drives the
  task lifecycle by hand. Worth the extra code when the agent has several skills, or
  when long-running work needs real progress reporting and artifacts.
* **One liner** -- ``A2AExecutor`` from ``agent_framework_a2a``, which wraps any Agent
  Framework agent with no handler and no lifecycle code.
"""

from __future__ import annotations

import logging

from a2a.server.request_handlers import DefaultRequestHandler
from a2a.server.routes import (
    create_agent_card_routes,
    create_jsonrpc_routes,
    create_rest_routes,
)
from a2a.server.tasks import InMemoryTaskStore
from a2a.types import AgentCapabilities, AgentCard, AgentInterface, AgentSkill
from agent_framework_a2a import A2AExecutor
from starlette.applications import Starlette
from starlette.responses import RedirectResponse
from starlette.routing import Mount, Route

from .agent_factory import HostedAgentFactory
from .config import DemoAgentOptions, HostSettings
from .handler import DemoAgentExecutor
from .skills import MarketResearchSkill, QuickAnswerSkill

logger = logging.getLogger(__name__)


def build_agent_card(options: DemoAgentOptions) -> AgentCard:
    """The A2A business card: identity, endpoint, skills (talk slide 9)."""
    base = options.public_base_url.rstrip("/")

    return AgentCard(
        name=options.name,
        description=options.description,
        version=options.version,
        documentation_url=options.documentation_url or "",
        default_input_modes=list(dict.fromkeys(options.default_input_modes)),
        default_output_modes=list(dict.fromkeys(options.default_output_modes)),
        capabilities=AgentCapabilities(streaming=options.streaming, push_notifications=False),
        supported_interfaces=[
            # protocol_version is set explicitly: protobuf omits empty strings from
            # JSON, and the .NET A2A SDK marks the field required, so leaving it
            # unset makes this card unreadable to .NET clients.
            AgentInterface(url=f"{base}/a2a", protocol_binding="JSONRPC", protocol_version="1.0"),
        ],
        skills=[
            AgentSkill(
                id=skill.id,
                name=skill.name,
                description=skill.description,
                tags=list(skill.tags),
                examples=list(skill.examples),
            )
            for skill in options.skills
        ],
    )


def build_app(settings: HostSettings) -> Starlette:
    agent_factory = HostedAgentFactory(settings.azure_openai)
    agent_card = build_agent_card(settings.agent)

    # -- Path 1: full control ---------------------------------------------------
    full_control_handler = DefaultRequestHandler(
        agent_executor=DemoAgentExecutor(
            [
                # Registration order is priority order.
                MarketResearchSkill(agent_factory, settings.long_running_step_seconds),
                QuickAnswerSkill(agent_factory),
            ]
        ),
        task_store=InMemoryTaskStore(),
        agent_card=agent_card,
    )

    # -- Path 2: the one-liner --------------------------------------------------
    # Same Agent Framework agent, published with no handler and no lifecycle code.
    simple_handler = DefaultRequestHandler(
        agent_executor=A2AExecutor(agent_factory.agent, stream=True),
        task_store=InMemoryTaskStore(),
        agent_card=agent_card,
    )

    routes: list[Route | Mount] = [
        # Standard root discovery location, so any A2A client can find this agent
        # with nothing but the base URL (talk slide 9).
        *create_agent_card_routes(agent_card),
        # Path 1 -- JSON-RPC: SendMessage, SendStreamingMessage, GetTask,
        # CancelTask, ListTasks, ...
        *create_jsonrpc_routes(full_control_handler, "/a2a"),
        # Path 2 -- JSON-RPC and HTTP+JSON, because A2A 1.0 defines more than one
        # binding and the caller picks from the card.
        *create_jsonrpc_routes(simple_handler, "/a2a/simple"),
        *create_rest_routes(simple_handler, path_prefix="/a2a/simple-http"),
        Route("/", lambda request: RedirectResponse("/.well-known/agent-card.json")),
    ]

    app = Starlette(routes=routes)

    logger.info(
        "A2A agent '%s' | backend: %s",
        settings.agent.name,
        "Azure OpenAI via Microsoft Agent Framework"
        if agent_factory.is_model_backed
        else "offline scripted agent",
    )
    base = settings.agent.public_base_url.rstrip("/")
    logger.info("  card         %s/.well-known/agent-card.json", base)
    logger.info("  full-control JSON-RPC  %s/a2a", base)
    logger.info("  one-liner    JSON-RPC  %s/a2a/simple", base)
    logger.info("  one-liner    HTTP+JSON %s/a2a/simple-http/message:send", base)

    return app
