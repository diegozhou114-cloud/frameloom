#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
if [[ -d "$HERE/MiniSiege.app" ]]; then
  export MINISIEGE_OUTPUT="$HERE"
fi
if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
fi
exec "$HERE/run.sh" macos
