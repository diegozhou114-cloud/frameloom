#!/usr/bin/env bash
set -euo pipefail

package=Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket
if [[ ! -d "$package" ]]; then
  printf 'missing NativeWebSocket package root: %s\n' "$package" >&2
  exit 1
fi

# This verifies the Unity-imported metadata committed alongside the embedded
# package. The networked vendor command has its own provenance contract test.
# UPM does not import opt-in package samples under Samples~.
while IFS= read -r asset; do
  test -f "$asset.meta" || {
    printf 'missing Unity metadata for asset: %s\n' "$asset" >&2
    exit 1
  }
done < <(find "$package" -path '*/Samples~' -prune -o -type f ! -name '*.meta' -print)

while IFS= read -r directory; do
  test -f "$directory.meta" || {
    printf 'missing Unity metadata for directory: %s\n' "$directory" >&2
    exit 1
  }
done < <(find "$package" -path '*/Samples~' -prune -o -mindepth 1 -type d -print)
