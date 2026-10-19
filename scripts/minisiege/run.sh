#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUTPUT="${MINISIEGE_OUTPUT:-$HOME/Desktop/MiniSiegeDemo}"
MODE="${1:-macos}"
mkdir -p "$OUTPUT"
if [[ -f "$OUTPUT/Server/MiniSiegeHost.dll" ]]; then
  dotnet "$OUTPUT/Server/MiniSiegeHost.dll" --urls http://0.0.0.0:8787 > "$OUTPUT/server.log" 2>&1 &
else
  dotnet run --project "$ROOT/Server/MiniSiegeHost" -- --urls http://0.0.0.0:8787 > "$OUTPUT/server.log" 2>&1 &
fi
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true' EXIT INT TERM
for ((attempt=0; attempt<60; attempt++)); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$OUTPUT/server.log" >&2; exit 1; fi
  if curl --silent --fail http://127.0.0.1:8787/health >/dev/null; then break; fi
  sleep 0.5
done
if ! curl --silent --fail http://127.0.0.1:8787/health >/dev/null; then
  echo "Server did not become ready; see $OUTPUT/server.log" >&2; exit 1
fi
if [[ "$MODE" == macos ]]; then
  if [[ ! -d "$OUTPUT/MiniSiege.app" ]]; then echo 'Run build.sh macos first.' >&2; exit 1; fi
  open -n "$OUTPUT/MiniSiege.app" --args -siegeConnect
  open -n "$OUTPUT/MiniSiege.app" --args -siegeConnect
elif [[ "$MODE" == webgl ]]; then
  python3 -m http.server 8790 --bind 127.0.0.1 --directory "$OUTPUT/WebGL" > "$OUTPUT/webgl.log" 2>&1 &
  WEB_PID=$!
  trap 'kill "$SERVER_PID" "$WEB_PID" 2>/dev/null || true' EXIT INT TERM
  open http://127.0.0.1:8790
  open http://127.0.0.1:8790
else
  echo 'Usage: run.sh [macos|webgl]' >&2; exit 2
fi
echo 'Server running at ws://127.0.0.1:8787/siege; Ctrl+C stops it.'
wait "$SERVER_PID"
