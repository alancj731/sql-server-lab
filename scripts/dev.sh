#!/usr/bin/env bash
# Runs the API (http://localhost:5080), the worker, and Angular (http://localhost:4200) in local simulated mode.
# Ctrl+C stops all three.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
# Background processes cannot answer interactive prompts (e.g. Angular CLI analytics).
export NG_CLI_ANALYTICS=false
export DOTNET_CLI_TELEMETRY_OPTOUT=1

pids=()
cleanup() { kill "${pids[@]}" 2>/dev/null || true; wait 2>/dev/null || true; }
trap cleanup EXIT INT TERM

dotnet build "$root/backend/SqlServerLab.slnx" --nologo -v quiet

(cd "$root/backend/src/SqlServerLab.Api" && dotnet run --no-build --launch-profile http) &
pids+=($!)
(cd "$root/backend/src/SqlServerLab.Worker" && dotnet run --no-build) &
pids+=($!)
(cd "$root/frontend" && npx ng serve --port 4200 </dev/null) &
pids+=($!)

wait -n
