#!/usr/bin/env bash
set -euo pipefail

repo_root="${1:-$(pwd)}"
failures=0

require_text() {
  local issue="$1"
  local file="$2"
  local text="$3"
  if ! rg -Fq -- "$text" "$repo_root/$file"; then
    printf '[%s] FAIL %s missing exact contract: %s\n' "$issue" "$file" "$text"
    failures=$((failures + 1))
  else
    printf '[%s] PASS %s\n' "$issue" "$file"
  fi
}

require_text BUILD-001 Assets/Editor/AutoFixProcessor.cs \
  'private static int FindUsingInsertPosition(string content)'
require_text BUILD-002 Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs \
  '(SimEntity entity, ref SimTransformComponent transform, ref SimMovementComponent movement) =>'
require_text BUILD-002 Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs \
  '(SimEntity entity, ref SimAttackComponent attack, ref SimTransformComponent transform) =>'
require_text BUILD-003 Assets/Scripts/Gameplay/Economy/ResourceProducer.cs 'using System;'
if [[ -f "$repo_root/Assets/Scripts/Combat/Core/CombatBootstrap.cs" ]]; then
  require_text BUILD-004 Assets/Scripts/Combat/Core/CombatBootstrap.cs 'using UnityEngine;'
elif rg -n '\bCombatBootstrap\b' "$repo_root/Assets/Scripts" "$repo_root/Assets/Tests" \
    --glob '*.cs'; then
  printf '[BUILD-004] FAIL CombatBootstrap removed but source/test references remain\n'
  failures=$((failures + 1))
else
  printf '[BUILD-004] PASS legacy CombatBootstrap removed with zero references\n'
fi

if (( failures > 0 )); then
  printf '[CompilationContracts] FAIL count=%d\n' "$failures"
  exit 1
fi

printf '[CompilationContracts] PASS count=5\n'
