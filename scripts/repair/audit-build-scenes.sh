#!/usr/bin/env bash
set -euo pipefail

repo_root="${1:-$(pwd)}"
settings="$repo_root/ProjectSettings/EditorBuildSettings.asset"
build_script="$repo_root/Assets/Editor/BuildScript.cs"
launcher_guid='69ad5f5cc5a89984f80eae46293e4e5c'
failures=0

require() {
  local name="$1"
  shift
  if "$@"; then
    printf '[BuildScenes_LauncherIsEnabledSceneZero] PASS %s\n' "$name"
  else
    printf '[BuildScenes_LauncherIsEnabledSceneZero] FAIL %s\n' "$name"
    failures=$((failures + 1))
  fi
}

require launcher-entry rg -Uq -- \
  "m_Scenes:\n  - enabled: 1\n    path: Assets/Scence/Launcher.unity\n    guid: $launcher_guid" "$settings"
require enabled-settings-source rg -Fq -- 'EditorBuildSettings.scenes' "$build_script"
require no-global-scene-scan bash -c "! rg -Fq 'FindAssets(\"t:Scene\")' \"$build_script\""
require no-default-scene-fallback bash -c "! rg -Fq 'CreateDefaultScene' \"$build_script\""

if (( failures > 0 )); then
  printf '[BuildScenes_LauncherIsEnabledSceneZero] FAIL count=%d\n' "$failures"
  exit 1
fi

printf '[BuildScenes_LauncherIsEnabledSceneZero] PASS count=4\n'
