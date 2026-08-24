"""Skill abstraction shared by the Python host's skills.

The mirror of ``Skills/IA2ASkill.cs``.
"""

from __future__ import annotations

from abc import ABC, abstractmethod

from a2a.server.agent_execution import RequestContext
from a2a.server.events import EventQueue
from a2a.types import Part


class A2ASkill(ABC):
    """One advertised A2A skill.

    The skill decides how to answer: a plain ``Message`` for immediate work, or a
    full ``Task`` lifecycle for anything long-running.
    """

    @property
    @abstractmethod
    def id(self) -> str:
        """Matches the skill id advertised in the Agent Card."""

    @abstractmethod
    def can_handle(self, context: RequestContext) -> bool:
        """Whether this skill should take the incoming request."""

    @abstractmethod
    async def execute(self, context: RequestContext, event_queue: EventQueue) -> None:
        """Runs the skill, writing protocol events onto ``event_queue``."""


def text_part(text: str) -> Part:
    """Builds a single text part."""
    return Part(text=text)


def user_text(context: RequestContext) -> str:
    """The concatenated text of the inbound message."""
    return context.get_user_input() or ""


def requested_skill_id(context: RequestContext) -> str | None:
    """Reads the caller's requested skill id from message metadata, if supplied.

    A2A metadata is the polite way to steer a multi-skill agent without smuggling
    directives into the prompt text.
    """
    message = context.message
    if message is None:
        return None

    metadata = getattr(message, "metadata", None)
    if not metadata:
        return None

    try:
        value = metadata["skill"]
    except (KeyError, TypeError):
        return None

    # Protobuf Struct values arrive as google.protobuf.Value; plain dicts as str.
    if isinstance(value, str):
        return value
    string_value = getattr(value, "string_value", None)
    return string_value or None
