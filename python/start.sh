#!/usr/bin/env bash
# Starts the Python side of the A2A demo, creating the venv on first run.
#
#   ./start.sh                  the A2A hosted agent on http://localhost:5402
#   ./start.sh host             same thing, explicitly
#   ./start.sh client           every demo, against whatever A2A_BASE_URL points at
#   ./start.sh client card job  just those demos
#
# Configure a real model by copying ../.env.template to .env in this folder.
# .env is gitignored. See the repo README.

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

# Windows venvs put the interpreter in Scripts/, everything else in bin/.
if [ -x ".venv/bin/python" ]; then
    PY=".venv/bin/python"
elif [ -x ".venv/Scripts/python.exe" ]; then
    PY=".venv/Scripts/python.exe"
else
    echo "Creating virtual environment in .venv ..."
    "${PYTHON:-python3}" -m venv .venv
    PY=".venv/bin/python"
    [ -x "$PY" ] || PY=".venv/Scripts/python.exe"
    "$PY" -m pip install --upgrade pip --quiet
    echo "Installing requirements ..."
    "$PY" -m pip install --quiet -r requirements.txt
fi

target="${1:-host}"
shift || true

case "$target" in
    host)
        echo "Starting the Python A2A hosted agent on http://localhost:5402 ..."
        exec "$PY" a2a_host.py
        ;;
    client)
        exec "$PY" a2a_console.py "$@"
        ;;
    *)
        echo "Unknown target '$target'. Use 'host' or 'client'." >&2
        exit 1
        ;;
esac
