#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUTPUT="${MINISIEGE_OUTPUT:-$HOME/Desktop/MiniSiegeDemo}"
UNITY="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity-arm64.app/Contents/MacOS/Unity}"
TARGET="${1:-macos}"
case "$TARGET" in
  macos) METHOD=BuildMacOS; PLATFORM=StandaloneOSX ;;
  webgl) METHOD=BuildWebGL; PLATFORM=WebGL ;;
  *) echo 'Usage: build.sh [macos|webgl]' >&2; exit 2 ;;
esac
if [[ ! -x "$UNITY" ]]; then echo "Unity editor missing: $UNITY" >&2; exit 1; fi
mkdir -p "$OUTPUT"
dotnet publish "$ROOT/Server/MiniSiegeHost/MiniSiegeHost.csproj" -c Release -o "$OUTPUT/Server"
SETTINGS_BACKUP="$(mktemp)"
cp "$ROOT/ProjectSettings/ProjectSettings.asset" "$SETTINGS_BACKUP"
trap 'cp "$SETTINGS_BACKUP" "$ROOT/ProjectSettings/ProjectSettings.asset"; rm "$SETTINGS_BACKUP"' EXIT
MINISIEGE_OUTPUT="$OUTPUT" "$UNITY" -batchmode -quit -projectPath "$ROOT" -buildTarget "$PLATFORM" \
  -executeMethod "ZWebGLFrameWork.Editor.MiniSiegeBuild.$METHOD" -logFile "$OUTPUT/build-$TARGET.log"
cp "$ROOT/scripts/minisiege/run.sh" "$ROOT/scripts/minisiege/verify.sh" "$ROOT/scripts/minisiege/启动双人对战.command" "$OUTPUT/"
cp "$ROOT/docs/minisiege.md" "$OUTPUT/README.md"
chmod +x "$OUTPUT/run.sh" "$OUTPUT/verify.sh" "$OUTPUT/启动双人对战.command"
echo "Build complete: $OUTPUT (Unity log: build-$TARGET.log)"
