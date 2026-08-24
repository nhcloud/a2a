"""Entry point for the Python A2A hosted agent.

The mirror of ``dotnet/A2A.Demo.HostedAgent``. Same Agent Card, same two skills, same
protocol behaviour -- a different runtime on the other side of the wire.

    python a2a_host.py

Listens on http://localhost:5402 by default so it can run alongside the .NET host
on 5401. Point either console client at it:

    A2A_BASE_URL=http://localhost:5402 python a2a_console.py all
    dotnet run --project ../dotnet/A2A.Demo.Console -- all     # after editing A2A:BaseUrl
"""

from __future__ import annotations

import logging

import uvicorn

from hosted_agent.app import build_app
from hosted_agent.config import load_settings


def main() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(levelname)-8s %(name)s: %(message)s",
    )
    # The Agent Framework warns that the offline chat client has no tool support.
    # True, and irrelevant here -- the skills call the agent directly.
    logging.getLogger("agent_framework").setLevel(logging.ERROR)

    settings = load_settings()
    app = build_app(settings)

    uvicorn.run(app, host=settings.host, port=settings.port, log_level="warning")


if __name__ == "__main__":
    main()
