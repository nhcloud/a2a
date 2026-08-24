"""The request/response half of the demo. Mirror of ``Skills/QuickAnswerSkill.cs``."""

from __future__ import annotations

import logging
import uuid

from a2a.server.agent_execution import RequestContext
from a2a.server.events import EventQueue
from a2a.types import Message, Role
from agent_framework import AgentSession

from ..agent_factory import HostedAgentFactory
from .base import A2ASkill, text_part, user_text

logger = logging.getLogger(__name__)


class QuickAnswerSkill(A2ASkill):
    """The agent can answer right now, so it replies with a ``Message``.

    Talk slide 11 -- "Message: returned when the agent can answer immediately."
    This is the cheapest possible A2A interaction and should stay the default.
    No task is ever created.
    """

    SKILL_ID = "quick-answer"

    def __init__(self, agent_factory: HostedAgentFactory) -> None:
        self._agent_factory = agent_factory
        # One Agent Framework session per A2A context_id, so multi-turn conversations
        # keep their history. context_id is A2A's conversation grouping key (slide 11).
        self._sessions: dict[str, AgentSession] = {}

    @property
    def id(self) -> str:
        return self.SKILL_ID

    def can_handle(self, context: RequestContext) -> bool:
        """Fallback skill -- takes anything the long-running skill declined."""
        return True

    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        prompt = user_text(context)
        logger.info("quick-answer | context %s | %s", context.context_id, prompt)

        agent = self._agent_factory.agent
        session = self._session_for(context.context_id)

        response = await agent.run(prompt, session=session)

        await event_queue.enqueue_event(
            Message(
                role=Role.ROLE_AGENT,
                message_id=uuid.uuid4().hex,
                context_id=context.context_id or "",
                parts=[text_part(response.text)],
            )
        )

    def _session_for(self, context_id: str | None) -> AgentSession:
        agent = self._agent_factory.agent
        if not context_id:
            return agent.create_session()

        session = self._sessions.get(context_id)
        if session is None:
            session = agent.create_session()
            self._sessions[context_id] = session
        return session
