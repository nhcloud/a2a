"""Configuration for the Python A2A host.

Two sources, split by what they hold. appsettings.json carries the Agent Card and
skill metadata in the same shape as the .NET host's, so the two hosts describe
themselves identically. Azure OpenAI settings come from python/.env instead —
dotenv is the Python convention, and it keeps credentials in a gitignored file
rather than a committed one.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from dotenv import load_dotenv

PYTHON_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SETTINGS_PATH = PYTHON_ROOT / "appsettings.json"
DEFAULT_ENV_PATH = PYTHON_ROOT / ".env"

# Loaded once at import so every os.environ read below sees it. Copy the repo root's
# .env.template to python/.env to fill it in; without it, everything still runs
# against the offline scripted agent.
load_dotenv(DEFAULT_ENV_PATH, override=False)


@dataclass
class SkillOptions:
    """A single advertised A2A skill."""

    id: str = ""
    name: str = ""
    description: str = ""
    tags: list[str] = field(default_factory=list)
    examples: list[str] = field(default_factory=list)


@dataclass
class DemoAgentOptions:
    """Everything the host needs to describe itself in its Agent Card.

    The Agent Card is the A2A "business card" (talk slide 9): identity, endpoint,
    skills and auth requirements. Keeping it in configuration makes the point that
    discovery metadata is data, not code.
    """

    name: str = "Contoso Research Agent"
    description: str = "A demo agent exposed over the Agent2Agent (A2A) protocol."
    version: str = "1.0.0"
    documentation_url: str | None = None
    public_base_url: str = "http://localhost:5402"
    default_input_modes: list[str] = field(default_factory=lambda: ["text"])
    default_output_modes: list[str] = field(default_factory=lambda: ["text"])
    streaming: bool = True
    skills: list[SkillOptions] = field(default_factory=list)


@dataclass
class AzureOpenAIOptions:
    """Azure OpenAI settings for the Agent Framework agent behind the protocol.

    All three come from python/.env only — no appsettings.json keys, no code
    defaults — so each value lives in exactly one place and no credential is
    resolvable from a file that git tracks. Leave any of them unset and the host
    serves the offline scripted agent rather than guessing.

    ``endpoint`` accepts either an Azure AI Foundry v1 endpoint
    ("https://{resource}.services.ai.azure.com/openai/v1") or a classic Azure OpenAI
    resource URL ("https://{resource}.openai.azure.com/").
    """

    endpoint: str | None = None
    api_key: str | None = None
    deployment: str | None = None

    @property
    def is_configured(self) -> bool:
        return bool(self.endpoint and self.api_key and self.deployment)


@dataclass
class HostSettings:
    agent: DemoAgentOptions
    azure_openai: AzureOpenAIOptions
    long_running_step_seconds: float = 3.0
    host: str = "127.0.0.1"
    port: int = 5402


def _section(data: dict[str, Any], name: str) -> dict[str, Any]:
    value = data.get(name)
    return value if isinstance(value, dict) else {}


def load_settings(path: Path | str = DEFAULT_SETTINGS_PATH) -> HostSettings:
    """Reads the Agent Card from appsettings.json and the rest from the environment.

    Environment variable names match the .NET host's, so a single exported variable
    configures whichever host you happen to be running. Azure OpenAI is environment
    only; appsettings.json has no AzureOpenAI section to override.
    """
    path = Path(path)
    data: dict[str, Any] = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}

    agent_section = _section(data, "A2AAgent")
    demo_section = _section(data, "Demo")

    agent = DemoAgentOptions(
        name=agent_section.get("Name", DemoAgentOptions.name),
        description=agent_section.get("Description", DemoAgentOptions.description),
        version=agent_section.get("Version", DemoAgentOptions.version),
        documentation_url=agent_section.get("DocumentationUrl") or None,
        public_base_url=agent_section.get("PublicBaseUrl", DemoAgentOptions.public_base_url),
        default_input_modes=agent_section.get("DefaultInputModes", ["text"]),
        default_output_modes=agent_section.get("DefaultOutputModes", ["text"]),
        streaming=agent_section.get("Streaming", True),
        skills=[
            SkillOptions(
                id=s.get("Id", ""),
                name=s.get("Name", ""),
                description=s.get("Description", ""),
                tags=s.get("Tags", []),
                examples=s.get("Examples", []),
            )
            for s in agent_section.get("Skills", [])
        ],
    )

    # python/.env only, by design — see AzureOpenAIOptions.
    azure_openai = AzureOpenAIOptions(
        endpoint=os.environ.get("AZURE_OPENAI_ENDPOINT") or None,
        api_key=os.environ.get("AZURE_OPENAI_API_KEY") or None,
        deployment=os.environ.get("AZURE_OPENAI_DEPLOYMENT") or None,
    )

    return HostSettings(
        agent=agent,
        azure_openai=azure_openai,
        long_running_step_seconds=float(
            os.environ.get("DEMO_LONG_RUNNING_STEP_SECONDS")
            or demo_section.get("LongRunningStepSeconds", 3.0)
        ),
        host=os.environ.get("A2A_HOST", "127.0.0.1"),
        port=int(os.environ.get("A2A_PORT", "5402")),
    )
