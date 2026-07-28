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
require_text DATA-008 Assets/Scripts/Data/Model/VillageData.cs \
  'public void ClearBuildings()'
require_text DATA-008 Assets/Scripts/Data/Model/VillageData.cs \
  'OnBuildingRemoved?.Invoke(building);'
require_absent DATA-008 Assets/Scripts/Data/SaveManager.cs \
  'village.Buildings.Clear();'
require_absent DATA-008 Assets/Scripts/Data/DataManager.cs \
  'Village.Buildings.Clear();'
require_text DATA-009 Assets/Scripts/Data/Model/VillageData.cs \
  'if (building == null || string.IsNullOrWhiteSpace(building.Id))'
require_text DATA-009 Assets/Scripts/Data/Model/VillageData.cs \
  'if (GetBuilding(building.Id) != null)'
require_text DATA-010 Assets/Scripts/Data/SaveManager.cs \
  'return Fail("建筑位置重叠", out error);'
require_text DATA-011 Assets/Scripts/Data/SaveManager.cs \
  'return Fail("建筑等级超出配置范围", out error);'
require_text DATA-012 Assets/Scripts/Data/SaveManager.cs \
  'return Fail("满级建筑不能处于升级状态", out error);'
require_text CONFIG-001 Assets/Scripts/Data/ConfigTableManager.cs \
  'if (id <= 0)'
require_text CONFIG-002 Assets/Scripts/Data/ConfigTableManager.cs \
  '!Enum.IsDefined(typeof(ResourceType), building.ResourceType)'
require_text CONFIG-003 Assets/Scripts/Data/ConfigTableManager.cs \
  '(footprint.SizeX <= 0 || footprint.SizeY <= 0)'
require_text DATA-013 Assets/Scripts/UI/Panels/DeployPanel.cs \
  'GetUnitsByTownHall(player.TownHallLevel.Value)'
require_text DATA-014 Assets/Scripts/UI/Panels/DeployPanel.cs \
  'FrameworkBattleContent.TryGetUnitArchetypeId(unit.Id, out _)'
require_text DATA-015 Assets/Scripts/Gameplay/Building/BuildingInteraction.cs \
  'DataManager.Instance.Player.TownHallLevel.Value = building.Level.Value;'
require_text DATA-016 Assets/Scripts/Data/SaveManager.cs \
  'return Fail("玩家大本营等级与建筑等级不一致", out error);'
require_text DATA-017 Assets/Scripts/Data/SaveManager.cs \
  'return Fail("村庄包含多个大本营", out error);'
require_text DATA-018 Assets/Scripts/Data/Model/VillageData.cs \
  'Buildings.Exists(item => item.TypeId == 1)'
require_text BOOT-007 Assets/Scripts/FSM/Game/States/LaunchState.cs \
  'if (!configOk)'
require_text BOOT-007 Assets/Scripts/FSM/Game/States/LaunchState.cs \
  'PublishError("ConfigTableManager", "部分配置表加载失败，可能影响游戏功能");'
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

printf '[CompilationContracts] PASS count=22\n'
