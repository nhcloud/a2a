"""The long-running half of the demo. Mirror of ``Skills/MarketResearchSkill.cs``."""

from __future__ import annotations

import asyncio
import logging

from a2a.helpers import new_task_from_user_message
from a2a.server.agent_execution import RequestContext
from a2a.server.events import EventQueue
from a2a.server.tasks import TaskUpdater

from ..agent_factory import HostedAgentFactory
from .base import A2ASkill, requested_skill_id, text_part, user_text

logger = logging.getLogger(__name__)

TRIGGER_WORDS = ("research", "report", "analysis", "analyse", "analyze", "deep dive", "market")

ARTIFACT_ID = "market-research-report"

#: The sections the report is assembled from, one per progress step.
SECTIONS: tuple[tuple[str, str], ...] = (
    ("Executive summary", "Write a three-sentence executive summary for a market research brief on: {topic}"),
    ("Market landscape", "List four bullet points describing the current market landscape for: {topic}"),
    ("Key risks", "List three concise risks and one mitigation each for: {topic}"),
    ("Recommendation", "Give a single-paragraph recommendation with a clear next step for: {topic}"),
)


class MarketResearchSkill(A2ASkill):
    """Work that cannot finish inside one HTTP round trip.

    Talk slide 11 -- "Task: returned for long-running work; has an ID and a
    trackable lifecycle; artifacts are returned or updated as work progresses."
    The lifecycle emitted here is submitted -> working (repeatedly, with progress)
    -> completed, with one artifact streamed in sections.
    """

    SKILL_ID = "market-research"

    def __init__(self, agent_factory: HostedAgentFactory, step_seconds: float = 3.0) -> None:
        self._agent_factory = agent_factory
        # Padding so the "long-running" story is visible on stage even when the
        # model answers in under a second.
        self._step_seconds = step_seconds

    @property
    def id(self) -> str:
        return self.SKILL_ID

    def can_handle(self, context: RequestContext) -> bool:
        requested = requested_skill_id(context)
        if requested is not None:
            return requested.lower() == self.SKILL_ID

        text = user_text(context).lower()
        return any(word in text for word in TRIGGER_WORDS)

    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        topic = user_text(context) or "an unspecified topic"

        # The Python SDK expects the Task object itself on the queue before any
        # status update; the .NET SDK's TaskUpdater.SubmitAsync does this for you.
        task = context.current_task
        if task is None:
            if context.message is None:
                raise ValueError("A message is required to start a task.")
            task = new_task_from_user_message(context.message)
            await event_queue.enqueue_event(task)

        updater = TaskUpdater(
            event_queue=event_queue,
            task_id=task.id,
            context_id=context.context_id or task.context_id,
        )

        logger.info(
            "market-research | task %s | context %s | %s",
            task.id,
            context.context_id,
            topic,
        )

        try:
            # 1. Acknowledge. The caller gets a task id back straight away and can
            #    disconnect here -- the work carries on server-side.
            await updater.submit()

            await updater.start_work(
                updater.new_agent_message(
                    [text_part(f'Starting research on "{topic}". {len(SECTIONS)} sections to produce.')]
                )
            )

            # 2. Do the work in steps, reporting progress and streaming the artifact
            #    as it is written rather than hoarding it until the end.
            agent = self._agent_factory.agent
            session = agent.create_session()

            for index, (heading, prompt_template) in enumerate(SECTIONS):
                await updater.start_work(
                    updater.new_agent_message(
                        [text_part(f"Step {index + 1} of {len(SECTIONS)}: {heading}")]
                    )
                )

                await asyncio.sleep(self._step_seconds)

                section = await agent.run(prompt_template.format(topic=topic), session=session)
                is_last = index == len(SECTIONS) - 1

                await updater.add_artifact(
                    [text_part(f"## {heading}\n{section.text}\n\n")],
                    artifact_id=ARTIFACT_ID,
                    name="Market research report",
                    # append after the first chunk, so the client assembles one
                    # artifact rather than collecting four unrelated ones.
                    append=index > 0,
                    last_chunk=is_last,
                )

            # 3. Close the lifecycle. Terminal state -- polling clients stop here.
            await updater.complete(
                updater.new_agent_message(
                    [text_part(f'Research complete. The full report is attached as artifact "{ARTIFACT_ID}".')]
                )
            )

        except asyncio.CancelledError:
            logger.info("market-research | task %s canceled", task.id)
            await updater.cancel()
            raise
        except Exception as exc:  # noqa: BLE001 - report the failure through the protocol
            logger.exception("market-research | task %s failed", task.id)
            await updater.failed(
                updater.new_agent_message([text_part(f"Research failed: {exc}")])
            )
