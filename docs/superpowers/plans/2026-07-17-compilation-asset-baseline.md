# Compilation, Asset, and Scene Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore an importable Unity repository baseline with the four confirmed compiler defects repaired, stable Unity metadata, Launcher as enabled scene zero, and a deterministic required-runtime-asset gate.

**Architecture:** Fast source audits provide issue-specific red/green evidence even while unrelated SimulationCore and Network compiler defects are still open. Unity `2022.3.62f3c1` remains the authoritative compiler and importer; after the dependent workstreams remove their compiler blockers, Editor tests and the G0/G1 gates validate the same contracts through Unity APIs. Required assets are generated once by an explicit Editor command, checked in with stable metadata, and only validated during import/build.

**Tech Stack:** Unity `2022.3.62f3c1`, C# 9-compatible Unity assemblies, NUnit/EditMode tests, Bash, Unity `AssetDatabase`, `PrefabUtility`, and `JsonUtility`.

## Global Constraints

- Use only Unity `2022.3.62f3c1`; never open or rewrite this project with Unity 6.
- Supported Unity clients are WebGL and Windows Standalone x86-64.
- Keep `Assets/Scence/Launcher.unity` at its existing path; the misspelled `Scence` directory is serialized project state and is not renamed by this repair.
- SimulationCore remains the only target production battle engine; this plan does not change battle ownership or Legacy Combat.
- Build and test output must be written beneath `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}` and never beneath the repository.
- All generated Unity assets and importer-owned directories must have checked-in `.meta` files with stable GUIDs.
- Ignore dot-prefixed filesystem sentinels such as `.gitkeep`; Unity intentionally does not import them and must not receive hand-authored metadata.
- Do not close an issue in the evidence ledger until its red receipt, fix commit, green receipt, and broad gate receipt all exist and are readable.
- This plan owns exactly `BUILD-001`, `BUILD-002`, `BUILD-003`, `BUILD-004`, `ASSET-001`, `ASSET-002`, and `SCENE-001`.

## File Map

- Create `scripts/repair/audit-meta.sh`: deterministic filesystem audit for missing and orphaned Unity metadata.
- Create `scripts/repair/audit-compiler-contracts.sh`: issue-specific source audit for the four confirmed compiler repairs.
- Create `scripts/repair/audit-build-scenes.sh`: pre-Unity audit for Launcher ordering and BuildScript scene selection.
- Create `Assets/Tests/Editor/ZWebGLFrameWork.Editor.Tests.asmdef`: Editor-only NUnit assembly for formal G0/G1 contracts.
- Create `Assets/Tests/Editor/CompilationBaselineTests.cs`: named assembly-load regression checks for `BUILD-001..004`.
- Create `Assets/Tests/Editor/RepositoryIntegrityTests.cs`: metadata, scene-zero, and required-asset EditMode checks.
- Create `Assets/Editor/RequiredAssetManifest.cs`: manifest schema and fail-closed validator.
- Create `Assets/Editor/RequiredAssetGenerator.cs`: explicit deterministic generation command for required prefabs, `ViewConfig.asset`, and the manifest.
- Generate `Assets/Resources/Config/RequiredAssets.json`: sorted, checked-in runtime asset inventory.
- Generate `Assets/Resources/Config/ViewConfig.asset`: archetype-to-prefab map for archetypes `1`, `2`, `3`, `10`, and `11`.
- Generate `Assets/Resources/Prefabs/Units/*.prefab`: one prefab for every row in `Units.json`.
- Generate `Assets/Resources/Prefabs/Buildings/{TownHall,Cannon,ArcherTower}.prefab`: initial defender archetype views.
- Generate `Assets/Resources/UI/Panels/*.prefab`: one prefab for each supported runtime panel type.
- Modify `Assets/Editor/AutoFixProcessor.cs`: restore the missing `FindUsingInsertPosition` signature.
- Modify `Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs`: use the exact ref-delegate lambda signature.
- Modify `Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs`: use the exact ref-delegate lambda signature.
- Modify `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs`: import `System`.
- Modify `Assets/Scripts/Combat/Core/CombatBootstrap.cs`: import `UnityEngine`.
- Modify `ProjectSettings/EditorBuildSettings.asset`: register Launcher as enabled scene zero.
- Modify `Assets/Editor/BuildScript.cs`: consume enabled Build Settings order and reject an empty list.
- Modify `Assets/Editor/BuildValidator.cs`: make the boot/asset validation fail closed and callable by BuildScript.
- Delete `Assets/Scripts/Core/Async.meta`: remove the confirmed orphan.
- Create or modify importer-generated `.meta` files only through Unity `2022.3.62f3c1`.

---

### Task 1: Restore Stable Unity Metadata

**Issues closed after the final gate:** `ASSET-001`

**Files:**
- Create: `scripts/repair/audit-meta.sh`
- Delete: `Assets/Scripts/Core/Async.meta`
- Generate: missing `.meta` files below `Assets/` with Unity `2022.3.62f3c1`

**Interfaces:**
- Consumes: repository root as optional argument 1; Unity importer ownership rules.
- Produces: `audit-meta.sh [repository-root]`, exit `0` with exactly one PASS line or exit `1` with each missing/orphaned path.

- [ ] **Step 1: Add the metadata audit before importing the project**

Create `scripts/repair/audit-meta.sh` with executable mode:

```bash
#!/usr/bin/env bash
set -euo pipefail

repo_root="${1:-$(pwd)}"
assets_root="$repo_root/Assets"
failures=0

if [[ ! -d "$assets_root" ]]; then
  printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] FAIL missing Assets directory: %s\n' "$assets_root"
  exit 1
fi

while IFS= read -r path; do
  [[ "$path" == "$assets_root" ]] && continue
  relative="${path#"$assets_root"/}"
  basename="${path##*/}"
  [[ "$basename" == .* ]] && continue
  if [[ ! -f "$path.meta" ]]; then
    printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] missing-meta Assets/%s\n' "$relative"
    failures=$((failures + 1))
  fi
done < <(find "$assets_root" \( -type f -o -type d \) -print | LC_ALL=C sort)

while IFS= read -r meta; do
  target="${meta%.meta}"
  if [[ ! -e "$target" ]]; then
    printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] orphan-meta %s\n' "${meta#"$repo_root"/}"
    failures=$((failures + 1))
  fi
done < <(find "$assets_root" -type f -name '*.meta' -print | LC_ALL=C sort)

if (( failures > 0 )); then
  printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] FAIL count=%d\n' "$failures"
  exit 1
fi

printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] PASS count=0\n'
```

Run:

```bash
chmod +x scripts/repair/audit-meta.sh
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}"
mkdir -p "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001"
set +e
scripts/repair/audit-meta.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001/red.txt"
status=${PIPESTATUS[0]}
set -e
test "$status" -eq 1
```

Expected: the receipt ends with `RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] FAIL`; it includes `orphan-meta Assets/Scripts/Core/Async.meta` and at least one `missing-meta` line.

- [ ] **Step 2: Verify the exact editor before allowing it to import**

Run:

```bash
export UNITY_EDITOR="/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity"
test -x "$UNITY_EDITOR"
"$UNITY_EDITOR" -version | tee "$ZWEB_EVIDENCE_ROOT/baseline/unity-version.txt"
rg -F '2022.3.62f3c1' "$ZWEB_EVIDENCE_ROOT/baseline/unity-version.txt"
```

Expected: all three commands exit `0`. The current machine has the pinned editor and its `-version` output is `2022.3.62f3c1`; retain the receipt before import. Do not substitute `/Applications/Unity/Hub/Editor/6000.3.19f1/...`.

- [ ] **Step 3: Let the pinned importer generate metadata, then remove the orphan**

Run:

```bash
set +e
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" -logFile "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001/import.log"
import_status=$?
set -e
printf 'initial-import-exit=%d\n' "$import_status" \
  | tee "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001/import-exit.txt"
git rm Assets/Scripts/Core/Async.meta
```

Expected: Unity generates importer metadata and the exit receipt records its actual status. Unity batch mode versions differ in whether compiler errors make this import-only invocation nonzero, so the already captured compiler-contract red receipt is authoritative; `git rm` reports removal of the confirmed orphan.

- [ ] **Step 4: Verify the metadata audit is green**

Run:

```bash
scripts/repair/audit-meta.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-001/green.txt"
test "${PIPESTATUS[0]}" -eq 0
```

Expected: exactly `RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] PASS count=0`.

- [ ] **Step 5: Review and commit only importer metadata and the audit**

Run:

```bash
git status --short
git add scripts/repair/audit-meta.sh Assets
git diff --cached --check
git diff --cached --name-only | rg -v '^(scripts/repair/audit-meta\.sh|Assets/.+\.meta)$' && exit 1 || true
git commit -m "fix: restore stable Unity asset metadata"
```

Expected: the staged-path guard emits no paths; the commit succeeds. Record this commit as the `ASSET-001` fix commit after the broad G0 receipt exists.

---

### Task 2: Repair the Four Confirmed Compiler Contracts

**Issues closed after the final gate:** `BUILD-001`, `BUILD-002`, `BUILD-003`, `BUILD-004`

**Files:**
- Create: `scripts/repair/audit-compiler-contracts.sh`
- Modify: `Assets/Editor/AutoFixProcessor.cs`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs`
- Modify: `Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs`
- Modify: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs`
- Modify: `Assets/Scripts/Combat/Core/CombatBootstrap.cs`
- Create: `Assets/Tests/Editor/ZWebGLFrameWork.Editor.Tests.asmdef`
- Create: `Assets/Tests/Editor/CompilationBaselineTests.cs`

**Interfaces:**
- Consumes: `SimWorld.ForEachEntities<T1,T2>(RefAction<SimEntity,T1,T2>)` and existing assembly names.
- Produces: syntactically valid method/import/lambda forms plus named EditMode checks `EditorAssembly_AutoFixProcessor_Compiles`, `SimulationSystems_RefDelegates_Compile`, `GameplayAssembly_ResourceProducer_Compiles`, and `CombatAssembly_CombatBootstrap_Compiles`.

- [ ] **Step 1: Add and run the failing compiler-contract audit**

Create `scripts/repair/audit-compiler-contracts.sh`:

```bash
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
```

Run:

```bash
chmod +x scripts/repair/audit-compiler-contracts.sh
mkdir -p "$ZWEB_EVIDENCE_ROOT/baseline/BUILD"
set +e
scripts/repair/audit-compiler-contracts.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/BUILD/red.txt"
status=${PIPESTATUS[0]}
set -e
test "$status" -eq 1
```

Expected: all four issue IDs emit at least one `FAIL`, and the receipt ends with `CompilationContracts] FAIL count=5`.

- [ ] **Step 2: Apply the minimal compiler repairs**

In `AutoFixProcessor.cs`, replace the class-level bare block immediately after `SafeWriteAllText` with this method:

```csharp
private static int FindUsingInsertPosition(string content)
{
    var matches = Regex.Matches(content, @"^using .+;\s*$", RegexOptions.Multiline);
    if (matches.Count == 0) return 0;

    var last = matches[matches.Count - 1];
    return last.Index + last.Length;
}
```

In `SimMovementSystem.cs`, replace the lambda header with:

```csharp
World.ForEachEntities<SimTransformComponent, SimMovementComponent>(
    (SimEntity entity, ref SimTransformComponent transform, ref SimMovementComponent movement) =>
{
```

In `SimAttackSystem.cs`, replace the lambda header with:

```csharp
World.ForEachEntities<SimAttackComponent, SimTransformComponent>(
    (SimEntity entity, ref SimAttackComponent attack, ref SimTransformComponent transform) =>
{
```

Add this as the first import in `ResourceProducer.cs`:

```csharp
using System;
```

Add this import after `using Cysharp.Threading.Tasks;` in `CombatBootstrap.cs`:

```csharp
using UnityEngine;
```

- [ ] **Step 3: Add named formal compiler checks**

Create `Assets/Tests/Editor/ZWebGLFrameWork.Editor.Tests.asmdef`:

```json
{
  "name": "ZWebGLFrameWork.Editor.Tests",
  "rootNamespace": "ZWebGLFrameWork.Editor.Tests",
  "references": [
    "ZWebGLFrameWork.Editor",
    "ZWebGLFrameWork.Gameplay",
    "ZWebGLFrameWork.Combat",
    "ZWebGLFrameWork.SimulationCore"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

Create `Assets/Tests/Editor/CompilationBaselineTests.cs`:

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class CompilationBaselineTests
    {
        [Test]
        public void EditorAssembly_AutoFixProcessor_Compiles()
        {
            Assert.That(typeof(AutoFixProcessor).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Editor"));
        }

        [Test]
        public void SimulationSystems_RefDelegates_Compile()
        {
            Assert.That(typeof(SimMovementSystem).Assembly,
                Is.SameAs(typeof(SimAttackSystem).Assembly));
        }

        [Test]
        public void GameplayAssembly_ResourceProducer_Compiles()
        {
            Assert.That(typeof(ResourceProducer).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Gameplay"));
        }

        [Test]
public void CombatAssembly_CombatBootstrap_Compiles()
        {
            Assert.That(typeof(CombatBootstrap).Assembly.GetName().Name,
                Is.EqualTo("ZWebGLFrameWork.Combat"));
        }
    }
}
```

This BUILD-004 method is migration-only evidence. Simulation Task 17 removes this single method in the same commit that deletes `Assets/Scripts/Combat/Core/CombatBootstrap.cs`; the phase-aware source audit above then requires zero remaining `CombatBootstrap` references. CI's final required-test manifest must not require this migration-only method.

- [ ] **Step 4: Verify the issue-specific audit is green**

Run:

```bash
scripts/repair/audit-compiler-contracts.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/BUILD/green.txt"
test "${PIPESTATUS[0]}" -eq 0
```

Expected: five issue checks pass and the last line is `CompilationContracts] PASS count=5`.

The Unity EditMode tests remain a required broad G1 action in Task 5 because `NET-001` and `SIM-001` are separate compiler blockers owned by their respective plans.

- [ ] **Step 5: Commit the compiler repair as one reviewable baseline unit**

Run:

```bash
git add scripts/repair/audit-compiler-contracts.sh \
  Assets/Editor/AutoFixProcessor.cs \
  Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs \
  Assets/Scripts/SimulationCore/Systems/SimAttackSystem.cs \
  Assets/Scripts/Gameplay/Economy/ResourceProducer.cs \
  Assets/Scripts/Combat/Core/CombatBootstrap.cs \
  Assets/Tests/Editor
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
scripts/repair/audit-meta.sh "$PWD"
git diff --cached --check
git commit -m "fix: restore confirmed compiler contracts"
```

Expected: commit succeeds; save its hash for all four `BUILD-*` ledger entries.

---

### Task 3: Make Launcher the Only Enabled Scene Zero Source

**Issues closed after the final gate:** `SCENE-001`

**Files:**
- Create: `scripts/repair/audit-build-scenes.sh`
- Modify: `ProjectSettings/EditorBuildSettings.asset`
- Modify: `Assets/Editor/BuildScript.cs`
- Create: `Assets/Tests/Editor/RepositoryIntegrityTests.cs`

**Interfaces:**
- Consumes: `EditorBuildSettings.scenes` in serialized order.
- Produces: `BuildScript.GetEnabledScenePaths(): string[]`; builds fail when no enabled scene exists and never scan arbitrary scene assets.

- [ ] **Step 1: Add and run the failing scene contract audit**

Create `scripts/repair/audit-build-scenes.sh`:

```bash
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
  "m_Scenes:\\n  - enabled: 1\\n    path: Assets/Scence/Launcher.unity\\n    guid: $launcher_guid" "$settings"
require enabled-settings-source rg -Fq -- 'EditorBuildSettings.scenes' "$build_script"
require no-global-scene-scan bash -c "! rg -Fq 'FindAssets(\"t:Scene\")' \"$build_script\""
require no-default-scene-fallback bash -c "! rg -Fq 'CreateDefaultScene' \"$build_script\""

if (( failures > 0 )); then
  printf '[BuildScenes_LauncherIsEnabledSceneZero] FAIL count=%d\n' "$failures"
  exit 1
fi

printf '[BuildScenes_LauncherIsEnabledSceneZero] PASS count=4\n'
```

Run:

```bash
chmod +x scripts/repair/audit-build-scenes.sh
mkdir -p "$ZWEB_EVIDENCE_ROOT/baseline/SCENE-001"
set +e
scripts/repair/audit-build-scenes.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/SCENE-001/red.txt"
status=${PIPESTATUS[0]}
set -e
test "$status" -eq 1
```

Expected: all four contracts fail against the empty Build Settings and global scene scan.

- [ ] **Step 2: Serialize Launcher as enabled scene zero**

Replace `  m_Scenes: []` in `ProjectSettings/EditorBuildSettings.asset` with:

```yaml
  m_Scenes:
  - enabled: 1
    path: Assets/Scence/Launcher.unity
    guid: 69ad5f5cc5a89984f80eae46293e4e5c
```

- [ ] **Step 3: Replace BuildScript scene discovery with enabled order**

Add `using System;`, `using System.Linq;`, and `using UnityEditor.Build;` to `Assets/Editor/BuildScript.cs`. Add this method to `BuildScript`:

```csharp
public static string[] GetEnabledScenePaths()
{
    return EditorBuildSettings.scenes
        .Where(scene => scene.enabled)
        .Select(scene => scene.path)
        .ToArray();
}
```

Replace the current scene-discovery block in `Build` with:

```csharp
var scenes = GetEnabledScenePaths();
if (scenes.Length == 0)
{
    throw new BuildFailedException(
        "[BuildScript] Build Settings has no enabled scenes; Launcher must be enabled scene zero.");
}
```

Delete `CreateDefaultScene()` in full. Keep `BuildPlayerOptions.scenes = scenes`; no other caller may reorder the returned array.

- [ ] **Step 4: Add the formal scene-zero EditMode check**

Create `Assets/Tests/Editor/RepositoryIntegrityTests.cs` with the scene test first; Task 4 extends this file with asset checks:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class RepositoryIntegrityTests
    {
        [Test]
        public void RepositoryMetaAudit_HasNoMissingOrOrphanedMeta()
        {
            var errors = new List<string>();
            var assetsRoot = Path.GetFullPath("Assets");
            foreach (var path in Directory.EnumerateFileSystemEntries(
                assetsRoot, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path).StartsWith(".")) continue;
                if (path.EndsWith(".meta"))
                {
                    if (!File.Exists(path.Substring(0, path.Length - 5)) &&
                        !Directory.Exists(path.Substring(0, path.Length - 5)))
                        errors.Add($"orphan-meta {path}");
                    continue;
                }
                if (!File.Exists(path + ".meta"))
                    errors.Add($"missing-meta {path}");
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void BuildScenes_LauncherIsEnabledSceneZero()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            Assert.That(enabled, Is.Not.Empty);
            Assert.That(enabled[0], Is.EqualTo("Assets/Scence/Launcher.unity"));
            Assert.That(BuildScript.GetEnabledScenePaths(), Is.EqualTo(enabled));
            Assert.That(File.Exists(enabled[0]), Is.True);
        }
    }
}
```

- [ ] **Step 5: Verify the scene audit is green and commit**

Run:

```bash
scripts/repair/audit-build-scenes.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/SCENE-001/green.txt"
test "${PIPESTATUS[0]}" -eq 0
git add scripts/repair/audit-build-scenes.sh ProjectSettings/EditorBuildSettings.asset \
  Assets/Editor/BuildScript.cs Assets/Tests/Editor/RepositoryIntegrityTests.cs
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
scripts/repair/audit-meta.sh "$PWD"
git diff --cached --check
git commit -m "fix: build enabled Launcher scene first"
```

Expected: `BuildScenes_LauncherIsEnabledSceneZero] PASS count=4`, then a successful commit.

---

### Task 4: Generate and Validate Every Required Runtime Asset

**Issues closed after the final gate:** `ASSET-002`

**Files:**
- Create: `Assets/Editor/RequiredAssetManifest.cs`
- Create: `Assets/Editor/RequiredAssetGenerator.cs`
- Modify: `Assets/Editor/BuildValidator.cs`
- Modify: `Assets/Tests/Editor/RepositoryIntegrityTests.cs`
- Generate: `Assets/Resources/Config/RequiredAssets.json`
- Generate: `Assets/Resources/Config/ViewConfig.asset`
- Generate: `Assets/Resources/Prefabs/Units/*.prefab`
- Generate: `Assets/Resources/Prefabs/Buildings/*.prefab`
- Generate: `Assets/Resources/UI/Panels/*.prefab`

**Interfaces:**
- Consumes: `Assets/Resources/Config/Units.json`, the five current archetype IDs, four approved modes, and eleven supported panel component types.
- Produces: `RequiredAssetManifestValidator.Validate(string manifestPath): RequiredAssetValidationResult` and menu/batch entry `RequiredAssetGenerator.Generate()`.

- [ ] **Step 1: Add the failing manifest test**

Append this test to `RepositoryIntegrityTests`:

```csharp
[Test]
public void RequiredRuntimeAssets_AllProductionKeysResolve()
{
    var result = RequiredAssetManifestValidator.Validate(
        RequiredAssetManifestValidator.ManifestPath);

    Assert.That(result.IsValid, Is.True,
        string.Join("\n", result.Errors));
}

[Test]
public void RequiredAssetManifest_EmptyEntryArrayIsRejected()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path,
            "{\"version\":1," +
            "\"supportedModes\":[\"builder_base\",\"campaign\",\"clan_war\",\"village_attack\"]," +
            "\"archetypeIds\":[1],\"unitIds\":[1],\"uiPanels\":[\"LoginPanel\"]," +
            "\"entries\":[]}");

        var result = RequiredAssetManifestValidator.Validate(path);

        Assert.That(result.Errors,
            Does.Contain("Manifest contains no required asset entries."));
    }
    finally
    {
        File.Delete(path);
    }
}

[Test]
public void RequiredAssetManifest_NullAndEmptyEntriesAreRejected()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path,
            "{\"version\":1," +
            "\"supportedModes\":[\"builder_base\",\"campaign\",\"clan_war\",\"village_attack\"]," +
            "\"archetypeIds\":[1],\"unitIds\":[1],\"uiPanels\":[\"LoginPanel\"]," +
            "\"entries\":[null,{\"key\":\"\",\"assetPath\":\"\",\"expectedType\":\"\"}]}");

        var result = RequiredAssetManifestValidator.Validate(path);

        Assert.That(result.Errors,
            Does.Contain("Manifest contains a null required asset entry."));
        Assert.That(result.Errors,
            Does.Contain("Required asset entry has an empty key."));
        Assert.That(result.Errors,
            Does.Contain("Required asset entry <empty> has an empty path."));
        Assert.That(result.Errors,
            Does.Contain("Required asset entry <empty> has an empty expected type."));
    }
    finally
    {
        File.Delete(path);
    }
}
```

Run after the Network and Simulation compiler-unblock tasks are green:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002"
set +e
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Editor.Tests.RepositoryIntegrityTests \
  -testResults "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/red.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/red.log"
status=$?
set -e
test "$status" -ne 0
rg -F 'RequiredAssets.json' "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/red.log"
```

Expected: the fixture fails because the validator and required runtime assets do not exist yet; after Step 2 the two malformed-manifest tests must pass while the production-key test remains red until Step 3 generates the assets.

- [ ] **Step 2: Add the manifest schema and fail-closed validator**

Create `Assets/Editor/RequiredAssetManifest.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.Combat;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    public sealed class RequiredAssetManifestDocument
    {
        public int version;
        public string[] supportedModes;
        public int[] archetypeIds;
        public int[] unitIds;
        public string[] uiPanels;
        public RequiredAssetEntry[] entries;
    }

    [Serializable]
    public sealed class RequiredAssetEntry
    {
        public string key;
        public string assetPath;
        public string expectedType;
    }

    public readonly struct RequiredAssetValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }

        public RequiredAssetValidationResult(List<string> errors)
        {
            Errors = errors;
            IsValid = errors.Count == 0;
        }
    }

    public static class RequiredAssetManifestValidator
    {
        public const string ManifestPath =
            "Assets/Resources/Config/RequiredAssets.json";

        private static readonly string[] ApprovedModes =
        {
            "builder_base", "campaign", "clan_war", "village_attack"
        };

        public static RequiredAssetValidationResult Validate(string manifestPath)
        {
            var errors = new List<string>();
            if (!File.Exists(manifestPath))
            {
                errors.Add($"Missing required asset manifest: {manifestPath}");
                return new RequiredAssetValidationResult(errors);
            }

            RequiredAssetManifestDocument document;
            try
            {
                document = JsonUtility.FromJson<RequiredAssetManifestDocument>(
                    File.ReadAllText(manifestPath));
            }
            catch (Exception exception)
            {
                errors.Add($"Invalid manifest JSON: {exception.Message}");
                return new RequiredAssetValidationResult(errors);
            }

            if (document == null || document.version != 1)
                errors.Add("Required asset manifest version must equal 1.");

            ValidateUnique(document?.supportedModes, "supported mode", errors);
            ValidateUnique(document?.uiPanels, "UI panel", errors);
            ValidateUnique(document?.archetypeIds, "archetype id", errors);
            ValidateUnique(document?.unitIds, "unit id", errors);

            if (document?.supportedModes == null ||
                !document.supportedModes.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(ApprovedModes))
            {
                errors.Add("Supported modes must be builder_base, campaign, clan_war, and village_attack.");
            }

            var entries = document?.entries ?? Array.Empty<RequiredAssetEntry>();
            if (entries.Length == 0)
                errors.Add("Manifest contains no required asset entries.");

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    errors.Add("Manifest contains a null required asset entry.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.key))
                    errors.Add("Required asset entry has an empty key.");
                if (string.IsNullOrWhiteSpace(entry.assetPath))
                    errors.Add($"Required asset entry {entry.key ?? "<empty>"} has an empty path.");
                if (string.IsNullOrWhiteSpace(entry.expectedType))
                    errors.Add($"Required asset entry {entry.key ?? "<empty>"} has an empty expected type.");
            }

            var duplicateKeys = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.key))
                .GroupBy(entry => entry.key)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key);
            foreach (var key in duplicateKeys)
                errors.Add($"Duplicate required asset key: {key}");

            var duplicatePaths = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.assetPath))
                .GroupBy(entry => entry.assetPath)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key);
            foreach (var path in duplicatePaths)
                errors.Add($"Duplicate required asset path: {path}");

            foreach (var entry in entries.OrderBy(value => value?.key, StringComparer.Ordinal))
                ValidateEntry(entry, errors);

            var viewConfig = AssetDatabase.LoadAssetAtPath<ViewConfig>(
                "Assets/Resources/Config/ViewConfig.asset");
            if (viewConfig == null || viewConfig.Entries == null || viewConfig.Entries.Length != 5)
            {
                errors.Add("ViewConfig must contain exactly five non-null archetype mappings.");
            }
            else
            {
                foreach (var entry in viewConfig.Entries)
                {
                    if (entry.Prefab == null)
                        errors.Add($"ViewConfig archetype {entry.ArchetypeId} has a null prefab.");
                }

                if (viewConfig.Entries.Select(entry => entry.ArchetypeId).Distinct().Count() != 5)
                    errors.Add("ViewConfig contains duplicate archetype ids.");
            }

            return new RequiredAssetValidationResult(errors);
        }

        private static void ValidateEntry(RequiredAssetEntry entry, List<string> errors)
        {
            if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                return;

            var asset = AssetDatabase.LoadMainAssetAtPath(entry.assetPath);
            if (asset == null)
            {
                errors.Add($"Required asset does not resolve: {entry.key} -> {entry.assetPath}");
                return;
            }

            var typeMatches = entry.expectedType switch
            {
                "GameObject" => asset is GameObject,
                "SceneAsset" => asset is SceneAsset,
                "ScriptableObject" => asset is ScriptableObject,
                "TextAsset" => asset is TextAsset,
                _ => false,
            };
            if (!typeMatches)
                errors.Add($"Required asset has wrong type: {entry.key} expected {entry.expectedType}");
        }

        private static void ValidateUnique<T>(T[] values, string label, List<string> errors)
        {
            if (values == null || values.Length == 0)
            {
                errors.Add($"Manifest contains no {label} values.");
                return;
            }

            if (values.Distinct().Count() != values.Length)
                errors.Add($"Manifest contains duplicate {label} values.");
        }
    }
}
```

- [ ] **Step 3: Add the explicit deterministic asset generator**

Create `Assets/Editor/RequiredAssetGenerator.cs` with these exact inputs and entry rules:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Editor
{
    public static class RequiredAssetGenerator
    {
        private const string ViewConfigPath =
            "Assets/Resources/Config/ViewConfig.asset";

        private static readonly string[] Modes =
        {
            "builder_base", "campaign", "clan_war", "village_attack"
        };

        private static readonly (int id, string name, string path, PrimitiveType primitive)[] Archetypes =
        {
            (1, "TownHall", "Assets/Resources/Prefabs/Buildings/TownHall.prefab", PrimitiveType.Cube),
            (2, "Cannon", "Assets/Resources/Prefabs/Buildings/Cannon.prefab", PrimitiveType.Cylinder),
            (3, "ArcherTower", "Assets/Resources/Prefabs/Buildings/ArcherTower.prefab", PrimitiveType.Cylinder),
            (10, "Barbarian", "Assets/Resources/Prefabs/Units/Barbarian.prefab", PrimitiveType.Capsule),
            (11, "Archer", "Assets/Resources/Prefabs/Units/Archer.prefab", PrimitiveType.Capsule),
        };

        private static readonly Type[] PanelTypes =
        {
            typeof(BattleHUD), typeof(BuildingInfoPanel), typeof(CharacterSelectPanel),
            typeof(DeployPanel), typeof(ErrorPanel), typeof(ForceUpdatePanel),
            typeof(LoadingPanel), typeof(LoginPanel), typeof(MainVillageHUD),
            typeof(ResultPanel), typeof(ToastPanel),
        };

        [MenuItem("ZWebGL/Generate Required Runtime Assets", false, 90)]
        public static void Generate()
        {
            var units = ConfigLoader.LoadJsonArrayFromResources<UnitConfig>("Config/Units")
                .OrderBy(unit => unit.Id)
                .ToArray();
            if (units.Length == 0)
                throw new InvalidOperationException("Units.json must contain at least one unit.");
            if (units.Select(unit => unit.Id).Distinct().Count() != units.Length)
                throw new InvalidOperationException("Units.json contains duplicate unit ids.");

            Directory.CreateDirectory("Assets/Resources/Prefabs/Units");
            Directory.CreateDirectory("Assets/Resources/Prefabs/Buildings");
            Directory.CreateDirectory("Assets/Resources/UI/Panels");

            foreach (var unit in units)
            {
                CreatePrimitivePrefab(
                    $"Assets/Resources/Prefabs/Units/{unit.Name}.prefab",
                    unit.Name,
                    PrimitiveType.Capsule);
            }

            foreach (var archetype in Archetypes.Where(value => value.id < 10))
                CreatePrimitivePrefab(archetype.path, archetype.name, archetype.primitive);

            foreach (var panelType in PanelTypes.OrderBy(type => type.Name, StringComparer.Ordinal))
                CreatePanelPrefab(panelType);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateViewConfig();
            WriteManifest(units);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var validation = RequiredAssetManifestValidator.Validate(
                RequiredAssetManifestValidator.ManifestPath);
            if (!validation.IsValid)
                throw new InvalidOperationException(string.Join("\n", validation.Errors));
        }

        private static void CreatePrimitivePrefab(
            string path, string name, PrimitiveType primitiveType)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return;

            var instance = GameObject.CreatePrimitive(primitiveType);
            try
            {
                instance.name = name;
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void CreatePanelPrefab(Type panelType)
        {
            var path = $"Assets/Resources/UI/Panels/{panelType.Name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return;

            // UIManager.GetOrCreatePanel<T> attaches the requested panel component at runtime.
            var instance = new GameObject(panelType.Name, typeof(RectTransform));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void CreateViewConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ViewConfig>(ViewConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ViewConfig>();
                AssetDatabase.CreateAsset(config, ViewConfigPath);
            }

            config.Entries = Archetypes
                .OrderBy(value => value.id)
                .Select(value => new ViewConfig.ArchetypeEntry
                {
                    ArchetypeId = value.id,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(value.path),
                })
                .ToArray();
            EditorUtility.SetDirty(config);
        }

        private static void WriteManifest(UnitConfig[] units)
        {
            var entries = new List<RequiredAssetEntry>
            {
                Entry("config.app", "Assets/Resources/Config/AppConfig.asset", "ScriptableObject"),
                Entry("config.buildings", "Assets/Resources/Config/Buildings.json", "TextAsset"),
                Entry("config.levels", "Assets/Resources/Config/Levels.json", "TextAsset"),
                Entry("config.units", "Assets/Resources/Config/Units.json", "TextAsset"),
                Entry("config.views", ViewConfigPath, "ScriptableObject"),
                Entry("scene.launcher", "Assets/Scence/Launcher.unity", "SceneAsset"),
            };

            entries.AddRange(units.Select(unit => Entry(
                $"unit.{unit.Id:D4}",
                $"Assets/Resources/Prefabs/Units/{unit.Name}.prefab",
                "GameObject")));
            entries.AddRange(Archetypes.Where(value => value.id < 10).Select(value => Entry(
                $"archetype.{value.id:D4}", value.path, "GameObject")));
            entries.AddRange(PanelTypes.Select(type => Entry(
                $"ui.{type.Name}",
                $"Assets/Resources/UI/Panels/{type.Name}.prefab",
                "GameObject")));

            var document = new RequiredAssetManifestDocument
            {
                version = 1,
                supportedModes = Modes.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                archetypeIds = Archetypes.Select(value => value.id).OrderBy(value => value).ToArray(),
                unitIds = units.Select(unit => unit.Id).OrderBy(value => value).ToArray(),
                uiPanels = PanelTypes.Select(type => type.Name)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                entries = entries.OrderBy(entry => entry.key, StringComparer.Ordinal).ToArray(),
            };

            File.WriteAllText(
                RequiredAssetManifestValidator.ManifestPath,
                JsonUtility.ToJson(document, true) + "\n",
                new UTF8Encoding(false));
        }

        private static RequiredAssetEntry Entry(string key, string path, string expectedType)
        {
            return new RequiredAssetEntry
            {
                key = key,
                assetPath = path,
                expectedType = expectedType,
            };
        }
    }
}
```

- [ ] **Step 4: Generate assets explicitly and verify repeatability**

Run with the pinned editor after all compiler-unblock dependencies are green:

```bash
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod ZWebGLFrameWork.Editor.RequiredAssetGenerator.Generate \
  -logFile "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/generate-first.log"
git diff -- Assets/Resources > "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/first.diff"
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod ZWebGLFrameWork.Editor.RequiredAssetGenerator.Generate \
  -logFile "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/generate-second.log"
git diff -- Assets/Resources > "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/second.diff"
cmp "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/first.diff" \
    "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/second.diff"
```

Expected: both editor runs exit `0`; the second generation leaves byte-identical tracked content. Prefab GUIDs remain unchanged because existing assets are not recreated.

- [ ] **Step 5: Make BuildValidator fail closed**

Change `ValidateBootPipeline` in `Assets/Editor/BuildValidator.cs` from private `void` to this public result contract, preserving its call from `AnalyzeCompilationResult`:

```csharp
public static bool ValidateBootPipeline()
{
    Debug.Log("[BuildValidator] Checking boot pipeline and required runtime assets...");
    var errors = new List<string>();

    var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
    if (scenes.Length == 0 || scenes[0].path != "Assets/Scence/Launcher.unity")
        errors.Add("Launcher must be enabled scene zero.");

    var assetResult = RequiredAssetManifestValidator.Validate(
        RequiredAssetManifestValidator.ManifestPath);
    errors.AddRange(assetResult.Errors);

    foreach (var error in errors)
        Debug.LogError($"[BuildValidator] {error}");

    if (errors.Count != 0)
    {
        Debug.LogError($"[BuildValidator] Boot pipeline validation failed: {errors.Count} errors.");
        return false;
    }

    Debug.Log("[BuildValidator] Boot pipeline and required assets passed.");
    return true;
}
```

At the start of `BuildScript.Build`, immediately after `AssetDatabase.Refresh()`, add:

```csharp
if (!BuildValidator.ValidateBootPipeline())
    throw new BuildFailedException("Required runtime asset validation failed.");
```

- [ ] **Step 6: Run the green asset test and commit generated assets**

Run:

```bash
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Editor.Tests.RepositoryIntegrityTests \
  -testResults "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/green.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/green.log"
rg -F 'test-result="Passed"' "$ZWEB_EVIDENCE_ROOT/baseline/ASSET-002/green.xml"
scripts/repair/audit-meta.sh "$PWD"
git add Assets/Editor/RequiredAssetManifest.cs Assets/Editor/RequiredAssetGenerator.cs \
  Assets/Editor/BuildValidator.cs Assets/Editor/BuildScript.cs \
  Assets/Tests/Editor/RepositoryIntegrityTests.cs Assets/Resources
git add -A -- ':(glob)Assets/*.meta' ':(glob)Assets/**/*.meta'
scripts/repair/audit-meta.sh "$PWD"
git diff --cached --check
git commit -m "fix: require complete runtime asset manifest"
```

Expected: the production-key, malformed-manifest, metadata, and scene tests pass; the metadata audit passes; and the asset commit contains manifest, assets, and importer-generated metadata.

---

### Task 5: Execute Formal G0/G1 Baseline Gates and Close Evidence

**Issues closed:** `BUILD-001`, `BUILD-002`, `BUILD-003`, `BUILD-004`, `ASSET-001`, `ASSET-002`, `SCENE-001`

**Files:**
- Modify: `docs/superpowers/specs/2026-07-16-project-repair-evidence.json`
- Create through the CI evidence tool: hash-addressed receipts beneath `docs/superpowers/evidence/artifacts/`

**Interfaces:**
- Consumes: CI plan evidence command `node scripts/audit/evidence-ledger.mjs close ...`, all compiler-unblock dependencies, and the exact Unity editor.
- Produces: seven Closed entries whose artifact receipts are readable and whose fix commits identify Tasks 1-4.

- [ ] **Step 1: Prove the exact project version before the broad run**

Run:

```bash
test "$(awk '/m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)" = '2022.3.62f3c1'
"$UNITY_EDITOR" -version | rg -F '2022.3.62f3c1'
```

Expected: both checks exit `0`.

- [ ] **Step 2: Run the complete Editor test assembly**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/baseline/G1"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -assemblyNames ZWebGLFrameWork.Editor.Tests \
  -testResults "$ZWEB_EVIDENCE_ROOT/baseline/G1/editor-tests.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/baseline/G1/editor-tests.log"
rg -F 'test-result="Passed"' "$ZWEB_EVIDENCE_ROOT/baseline/G1/editor-tests.xml"
rg -n 'error CS[0-9]+|Scripts have compiler errors|AssemblyBuilderException|MissingReferenceException|The referenced script .* is missing' \
  "$ZWEB_EVIDENCE_ROOT/baseline/G1/editor-tests.log" && exit 1 || true
```

Expected: Unity exits `0`, XML is Passed, and the forbidden-log scan emits no lines.

- [ ] **Step 3: Run G0 source and repository checks**

Run:

```bash
scripts/repair/audit-meta.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/G1/meta.txt"
scripts/repair/audit-compiler-contracts.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/G1/compiler-contracts.txt"
scripts/repair/audit-build-scenes.sh "$PWD" | tee "$ZWEB_EVIDENCE_ROOT/baseline/G1/build-scenes.txt"
git status --porcelain --untracked-files=all | tee "$ZWEB_EVIDENCE_ROOT/baseline/G1/git-status.txt"
test ! -s "$ZWEB_EVIDENCE_ROOT/baseline/G1/git-status.txt"
```

Expected: all audits pass and `git-status.txt` is empty. If Unity changed any tracked file, investigate and commit the intentional importer result before rerunning from a clean tree.

- [ ] **Step 4: Record seven evidence closures using executed artifact receipts**

Run the CI plan's ledger command through this fixed mapping:

```bash
LEDGER=docs/superpowers/specs/2026-07-16-project-repair-evidence.json
CLOSED_AT="$(date -u +%F)"

close_baseline_issue() {
  issue="$1"
  red="$2"
  green="$3"
  gate="$4"
  subject="$5"
  fix="$(git log --format=%H --fixed-strings --grep="$subject" -n 1)"
  test -n "$fix"
  test "$(git show -s --format=%s "$fix")" = "$subject"
  node scripts/audit/evidence-ledger.mjs close \
    --ledger "$LEDGER" --issue "$issue" \
    --red "$ZWEB_EVIDENCE_ROOT/baseline/$red" --fix "$fix" \
    --green "$ZWEB_EVIDENCE_ROOT/baseline/$green" \
    --gate "$ZWEB_EVIDENCE_ROOT/baseline/$gate" \
    --mode Fixed --date "$CLOSED_AT"
}

close_baseline_issue ASSET-001 ASSET-001/red.txt ASSET-001/green.txt G1/meta.txt \
  "fix: restore stable Unity asset metadata"
close_baseline_issue BUILD-001 BUILD/red.txt BUILD/green.txt G1/editor-tests.xml \
  "fix: restore confirmed compiler contracts"
close_baseline_issue BUILD-002 BUILD/red.txt BUILD/green.txt G1/editor-tests.xml \
  "fix: restore confirmed compiler contracts"
close_baseline_issue BUILD-003 BUILD/red.txt BUILD/green.txt G1/editor-tests.xml \
  "fix: restore confirmed compiler contracts"
close_baseline_issue BUILD-004 BUILD/red.txt BUILD/green.txt G1/editor-tests.xml \
  "fix: restore confirmed compiler contracts"
close_baseline_issue SCENE-001 SCENE-001/red.txt SCENE-001/green.txt G1/build-scenes.txt \
  "fix: build enabled Launcher scene first"
close_baseline_issue ASSET-002 ASSET-002/red.xml ASSET-002/green.xml G1/editor-tests.xml \
  "fix: require complete runtime asset manifest"
```

Expected: each command copies content-addressed receipts into `docs/superpowers/evidence/artifacts/`, updates only that ledger entry, deduplicates identical files by SHA-256, and exits `0`.

- [ ] **Step 5: Validate and commit the closure ledger**

Run:

```bash
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
git add docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  docs/superpowers/evidence/artifacts
git diff --cached --check
git commit -m "test: record repository baseline closure evidence"
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: validator exits `0`, commit succeeds, and final status emits no output.

## Self-Review Record

- Spec coverage: Tasks 1-5 cover every requirement assigned to repository/compiler/assets/scene-zero baseline and defer `SCENE-002` to the runtime plan.
- Ownership: the seven owned IDs appear in closure mappings exactly once; no Runtime, Simulation, Network/Server, or CI issue is claimed here.
- Type consistency: `RequiredAssetManifestValidator.Validate(string)` returns `RequiredAssetValidationResult`; both BuildValidator and NUnit use that exact contract. `BuildScript.GetEnabledScenePaths()` returns `string[]` and preserves `EditorBuildSettings.scenes` order.
- Simplicity boundary: generated prefabs are inspection-safe functional placeholders for existing code-driven UI and entity views; this plan does not add gameplay, art systems, Addressables, or a second asset framework.
- Formal verification limitation: the current host has Unity `2022.3.62f3c1`, but this planning pass did not import, compile, test, or serialize the project. No result from Unity `6000.3.19f1` is accepted as evidence.
