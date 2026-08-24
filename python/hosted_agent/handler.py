"""The A2A server's entry point. Mirror of ``Agents/DemoAgentHandler.cs``."""

from __future__ import annotations

import logging
import uuid
from collections.abc import Sequence

from a2a.server.agent_execution import AgentExecutor, RequestContext
from a2a.server.events import EventQueue
from a2a.types import Message, Role
from a2a.server.tasks import TaskUpdater

from .skills import A2ASkill, text_part

logger = logging.getLogger(__name__)


class DemoAgentExecutor(AgentExecutor):
    """Routes an inbound message to a skill and gets out of the way.

    ``execute`` handles ``SendMessage`` and ``SendStreamingMessage``; ``cancel``
    handles ``CancelTask``. Everything protocol-shaped -- task persistence, SSE
    fan-out, JSON-RPC framing -- is handled by the request handler this executor
    is registered with.
    """

    def __init__(self, skills: Sequence[A2ASkill]) -> None:
        # Registration order is priority order: the long-running skill gets first refusal.
        self._skills = list(skills)

    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        skill = next((s for s in self._skills if s.can_handle(context)), None)

        if skill is None:
            await event_queue.enqueue_event(
                Message(
                    role=Role.ROLE_AGENT,
                    message_id=uuid.uuid4().hex,
                    context_id=context.context_id or "",
                    parts=[text_part("No skill on this agent can handle that request.")],
                )
            )
            return

        logger.info("Routing to skill %s (task %s)", skill.id, context.task_id)
        await skill.execute(context, event_queue)

    async def cancel(self, context: RequestContext, event_queue: EventQueue) -> None:
        logger.info("Cancel requested for task %s", context.task_id)
        updater = TaskUpdater(
            event_queue=event_queue,
            task_id=context.task_id or "",
            context_id=context.context_id or "",
        )
        await updater.cancel()
