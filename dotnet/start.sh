#!/usr/bin/env bash
# Starts the .NET side of the A2A demo.
#
#   ./start.sh                  the A2A hosted agent on http://localhost:5401
#   ./start.sh host             same thing, explicitly
#   ./start.sh client           the console client, interactive menu
#   ./start.sh client all       every demo, non-interactive
#   ./start.sh client card job  just those demos
#
# Configure a real model in A2A.Demo.HostedAgent/appsettings.Development.json
# (and A2A.Demo.Console/appsettings.Development.json for the delegate demo).
# Both are gitignored. See the repo README.

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

target="${1:-host}"
shift || true

case "$target" in
    host)
        echo "Starting the .NET A2A hosted agent on http://localhost:5401 ..."
        exec dotnet run --project A2A.Demo.HostedAgent --urls http://localhost:5401
        ;;
    client)
        if [ "$#" -eq 0 ]; then
            exec dotnet run --project A2A.Demo.Console
        fi
        exec dotnet run --project A2A.Demo.Console -- "$@"
        ;;
    *)
        echo "Unknown target '$target'. Use 'host' or 'client'." >&2
        exit 1
        ;;
esac
