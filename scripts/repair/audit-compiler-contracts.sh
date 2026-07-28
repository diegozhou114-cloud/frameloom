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

require_absent() {
  local issue="$1"
  local file="$2"
  local text="$3"
  if rg -Fq -- "$text" "$repo_root/$file"; then
    printf '[%s] FAIL %s contains forbidden contract: %s\n' "$issue" "$file" "$text"
    failures=$((failures + 1))
  else
    printf '[%s] PASS %s\n' "$issue" "$file"
  fi
}

require_count() {
  local issue="$1"
  local file="$2"
  local text="$3"
  local expected="$4"
  local actual
  actual=$(rg -F -- "$text" "$repo_root/$file" | wc -l | tr -d ' ')
  if [[ "$actual" != "$expected" ]]; then
    printf '[%s] FAIL %s expected %s occurrences of contract, found %s: %s\n' \
      "$issue" "$file" "$expected" "$actual" "$text"
    failures=$((failures + 1))
  else
    printf '[%s] PASS %s\n' "$issue" "$file"
  fi
}

require_missing() {
  local issue="$1"
  local file="$2"
  if [[ -e "$repo_root/$file" ]]; then
    printf '[%s] FAIL obsolete path remains: %s\n' "$issue" "$file"
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
require_absent RESOURCE-001 Assets/Scripts/FSM/Game/States/LaunchState.cs 'CheckVersionAsync('
require_absent RESOURCE-001 Assets/Scripts/FSM/Game/States/LaunchState.cs 'DownloadUpdatesAsync('
require_count BOOT-006 Assets/Scripts/FSM/Game/States/LoginState.cs \
  'if (token.IsCancellationRequested || !IsActive) return;' 2
require_missing RESOURCE-002 Assets/Scripts/FSM/Game/HotUpdateManager.cs
require_text DATA-005 Assets/Scripts/FSM/Game/BattleRewardService.cs \
  'public sealed class BattleRewardService'
require_text DATA-005 Assets/Scripts/FSM/Game/BattleRewardService.cs \
  'SaveFailed,'
require_text DATA-005 Assets/Scripts/Data/PlayerSaveData.cs \
  'public List<string> AppliedBattleResultIds = new List<string>();'
require_text DATA-005 Assets/Scripts/FSM/Game/States/BattleState.cs \
  'BattleRewardCommitStatus status = _rewardService.Commit('
require_absent DATA-005 Assets/Scripts/FSM/Game/States/BattleState.cs \
  'EventBus.Subscribe<SimBattleResult>(CombatManager.EventCombatEnded'
require_absent DATA-005 Assets/Scripts/Combat/CombatManager.cs \
  'EventBus.Publish(EventCombatEnded, result);'
require_text DATA-006 Assets/Scripts/Data/SaveManager.cs \
  '(saved.State != BuildingState.Upgrading && saved.UpgradeEndTime != 0)'
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

printf '[CompilationContracts] PASS count=8\n'
