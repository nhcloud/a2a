"""Builds the Microsoft Agent Framework agent that sits behind the A2A protocol.

The mirror of ``Agents/HostedAgentFactory.cs`` in the .NET host, including the
offline fallback so the demo runs on conference Wi-Fi with no keys.
"""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterable, Awaitable, Mapping, Sequence
from typing import Any

from agent_framework import (
    Agent,
    BaseChatClient,
    ChatResponse,
    ChatResponseUpdate,
    Content,
    Message,
    ResponseStream,
)

from .config import AzureOpenAIOptions

INSTRUCTIONS = (
    "You are the Contoso Research Agent, reachable over the A2A protocol.\n"
    "Answer clearly and concisely. Prefer short paragraphs and bullet points.\n"
    "When asked what you can do, describe your two skills: quick answers and\n"
    "long-running market research reports."
)

OFFLINE_CAPABILITIES_REPLY = (
    "I expose two A2A skills: 'quick-answer' for immediate question and answer "
    "turns, and 'market-research' for long-running report generation that returns "
    "a Task you can poll or stream."
)


class ScriptedChatClient(BaseChatClient):
    """A deterministic offline chat client.

    So the demo runs with no keys, no quota, and no surprises. Swap in Azure OpenAI
    by filling in the AZURE_OPENAI_* variables in python/.env.
    """

    def _inner_get_response(  # type: ignore[override]
        self,
        *,
        messages: Sequence[Message],
        stream: bool,
        options: Mapping[str, Any],
        **kwargs: Any,
    ) -> Awaitable[ChatResponse] | ResponseStream[ChatResponseUpdate, ChatResponse]:
        # Note this is a plain def, not async: the framework awaits the non-streaming
        # return value but expects a ResponseStream back when stream=True.
        reply = self._compose(messages)

        if stream:

            async def _stream() -> AsyncIterable[ChatResponseUpdate]:
                for word in reply.split(" "):
                    await asyncio.sleep(0.025)
                    yield ChatResponseUpdate(role="assistant", contents=[Content.from_text(word + " ")])

            return self._build_response_stream(_stream())

        async def _respond() -> ChatResponse:
            return ChatResponse(messages=[Message(role="assistant", contents=[reply])])

        return _respond()

    @staticmethod
    def _compose(messages: Sequence[Message]) -> str:
        prompt = ""
        for message in reversed(list(messages)):
            if message.role == "user":
                prompt = message.text or ""
                break

        lowered = prompt.lower()
        if "capabilit" in lowered or "what can you" in lowered:
            return OFFLINE_CAPABILITIES_REPLY

        return (
            f'[offline demo agent] You asked: "{prompt}". '
            "Set the AZURE_OPENAI_* variables in python/.env to route this through a real model."
        )


class HostedAgentFactory:
    """Produces the agent behind the protocol, lazily and once.

    This is the "opaque" half of the A2A trust model (talk slide 6): callers see an
    Agent Card and a JSON-RPC endpoint. Whether the work is done by Azure OpenAI, a
    scripted stub, or a 200-person department is nobody else's business.
    """

    def __init__(self, options: AzureOpenAIOptions) -> None:
        self._options = options
        self._agent: Agent | None = None

    @property
    def is_model_backed(self) -> bool:
        """True when a real model is wired up rather than the offline stub."""
        return self._options.is_configured

    @property
    def agent(self) -> Agent:
        if self._agent is None:
            self._agent = self._build()
        return self._agent

    def _build(self) -> Agent:
        chat_client = self._create_azure_openai_client() if self.is_model_backed else ScriptedChatClient()
        return chat_client.as_agent(name="ContosoResearchAgent", instructions=INSTRUCTIONS)

    def _create_azure_openai_client(self) -> BaseChatClient:
        # Azure AI Foundry and Azure OpenAI both expose an OpenAI-compatible
        # "/openai/v1" surface, so one client covers both. Same rule as the .NET host.
        from agent_framework.openai import OpenAIChatClient

        endpoint = (self._options.endpoint or "").rstrip("/")
        if not endpoint.endswith("/openai/v1"):
            endpoint += "/openai/v1"

        return OpenAIChatClient(
            model=self._options.deployment,
            api_key=self._options.api_key,
            base_url=endpoint,
        )
