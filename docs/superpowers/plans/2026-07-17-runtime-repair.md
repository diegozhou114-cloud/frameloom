# Runtime Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close all 26 assigned runtime issues with executable PlayMode regressions and the smallest production changes that satisfy the approved repair design.

**Architecture:** Keep `GameFSM` as the application-flow owner, add explicit state lifetime and transition semantics, and keep persistence/UI/gameplay fixes behind focused services rather than test-only branches. Runtime tests live in one non-Editor test assembly and exercise public behavior; CI workflow wiring and expected-test-count enforcement remain owned by the CI repair plan.

**Tech Stack:** Unity `2022.3.62f3c1`, C# 9, NUnit, Unity Test Framework PlayMode tests, UniTask, uGUI, `JsonUtility`.

## Global Constraints

- Use Unity editor `2022.3.62f3c1`; Unity 6 migration is out of scope.
- Supported clients are WebGL and Windows Standalone; Android and iOS are not release gates.
- Offline mode remains supported.
- Do not add Addressables, a second FSM, a second save model, or another battle authority.
- Do not change Network, SimulationCore, ReplayValidator, server, CI workflow, Legacy Combat internals, or their issue IDs in this plan.
- Every production change starts with a focused failing PlayMode test, then the minimal passing implementation, then the focused test and full runtime suite.
- Every task is one reviewable commit; do not combine task commits.
- Task 0 creates the cross-plan test interface `Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef`. The CI plan still owns `CI-003`, workflow execution, and nonzero expected-count enforcement; Task 0 does not close a runtime issue ID.

## Test Command

All commands below run from the repository root. The exact pinned-editor executable is:

```bash
UNITY="/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity"
export ZWEB_EVIDENCE_ROOT="${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}"
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
```

Each focused command writes XML and logs beneath `${ZWEB_EVIDENCE_ROOT:-/tmp/zweb-repair-evidence}/runtime/`; those files are execution artifacts and must not be committed.

## File Map

- `Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef`: shared non-Editor PlayMode test assembly.
- `Assets/Tests/Runtime/Support/RuntimeTestBase.cs`: deterministic scene/object/event cleanup and temporary-directory ownership.
- `Assets/Tests/Runtime/Support/RuntimeAssemblySmokeTests.cs`: nonzero discovery proof before behavior suites are added.
- `Assets/Tests/Runtime/Core/*`: Core resource-provider regressions.
- `Assets/Tests/Runtime/FSM/*`: generic FSM, boot, pause, cancellation, and scene-load regressions.
- `Assets/Tests/Runtime/Data/*`: atomic persistence, save mapping, autosave, offline entry, rewards, version validation, and LocalStore regressions.
- `Assets/Tests/Runtime/UI/*`: EventSystem and panel lifecycle regressions.
- `Assets/Tests/Runtime/Gameplay/*`: occupancy, production, resource typing, scene rebinding, and saturation regressions.
- `Assets/Tests/Runtime/Scenes/*`: Launcher-to-village runtime composition regression.
- `Assets/Scripts/Core/Resource/*`: provider cache and AssetBundle contract fixes.
- `Assets/Scripts/Core/Scene/SceneLoader.cs`: explicit, cancellable scene-load results.
- `Assets/Scripts/FSM/Core/*`: committed transition events, queued reentrant transitions, suspension, and lifetime tokens.
- `Assets/Scripts/FSM/Game/*`: boot decisions, pause/resume, state cancellation, rewards, and offline entry orchestration.
- `Assets/Scripts/Data/*`: shared atomic writer, complete save mapping, transactional restore, autosave fingerprinting, and LocalStore backend.
- `Assets/Scripts/UI/*`: one EventSystem, build/bind panel lifecycle, base initialization, and cache retention.
- `Assets/Scripts/Gameplay/*`: authoritative footprints, reactive production, typed resource output, scene rebinding, saturation, and village service assembly.

## Issue Coverage Matrix

The register contains 38 named acceptance methods for this plan's 26 owned issues. Every name below is declared verbatim as a test method in its task's test file; the final structured-result validator rejects any missing name.

| Task | Owned issue | Exact acceptance methods | Focused evidence stem |
|---:|---|---|---|
| 1 | `CORE-001` | `ResourceManager_SetProvider_InvalidatesOldProviderCache` | `core-001` |
| 2 | `CORE-002` | `AssetBundleProvider_LoadAndUnload_UsesDependenciesAndBundleIdentity` | `core-002` |
| 3 | `BOOT-001` | `GameFSM_InitialTransition_EntersLaunchExactlyOnce` | `boot-001` |
| 6 | `BOOT-002` | `GameFSM_PauseResume_PreservesBattleContext` | `boot-002` |
| 7 | `BOOT-003` | `LaunchState_Exit_CancelsPipelineAndPreventsLateTransition` | `boot-003` |
| 8 | `BOOT-004` | `LaunchState_HotUpdateFailure_DoesNotContinue`; `LaunchState_NeedRestart_DoesNotContinue` | `boot-004` |
| 9 | `BOOT-005` | `LoadingState_MissingScene_PublishesFailureOnly`; `LoadingState_SceneTimeout_PublishesFailureOnly`; `LoadingState_DoesNotCompleteBeforeActivation` | `boot-005` |
| 4 | `FSM-001` | `FSM_OnEnterRedirect_EmitsOnlyOrderedCommittedTransitions` | `fsm-001` |
| 5/9 | `FSM-002` | `BattleState_ExitBeforeStartCompletes_CancelsLateStart`; `LoadingState_Exit_CancelsUnderlyingSceneLoad` | `fsm-002`, `boot-005` |
| 10 | `DATA-001` | `SaveManager_FirstWriteAndFailedReplace_AreAtomic` | `data-001` |
| 11 | `DATA-002` | `SaveGame_BuildingRoundTrip_PreservesCompleteState` | `data-002` |
| 12 | `DATA-003` | `MainVillageAutosave_AllPersistentChangesTrigger`; `MainVillageAutosave_FailureDoesNotAdvanceBaseline`; `MainVillageExit_SavesPendingChanges` | `data-003` |
| 13 | `DATA-004` | `OfflineLogin_LoadFailure_DoesNotEnterVillage`; `OfflineLogin_DefaultSaveCreationFailure_DoesNotEnterVillage` | `data-004` |
| 14 | `DATA-005` | `BattleResult_ValidCompletion_AppliesAndSavesRewardsOnce`; `BattleResult_DuplicateInvalidOrRejected_DoesNotReward`; `BattleResult_NearCap_Clamps`; `BattleResult_ReturnWaitsForSave` | `data-005` |
| 15 | `DATA-006` | `SaveGame_UnknownOrInvalidSchema_LeavesRuntimeStateUnchanged` | `data-006` |
| 16 | `DATA-007` | `LocalStore_FirstWriteFailureAndSupportedTypes_AreObservableAndAtomic` | `data-007` |
| 17 | `UI-001` | `UIManager_Initialize_CreatesSingleEventSystem`; `UIManager_WithExistingEventSystem_DoesNotDuplicate`; `LauncherButton_PointerClick_InvokesAction` | `ui-001` |
| 18 | `UI-002` | `Panel_Reopen_ReusesHierarchyAndRebindsData` | `ui-002` |
| 19 | `UI-003` | `BuildingInfoPanel_Awake_InitializesUIBase` | `ui-003` |
| 20 | `UI-004` | `UIManager_CloseLayer_DoesNotLeakOrDuplicateCachedPanel` | `ui-004` |
| 21 | `GAME-001` | `VillageOccupancy_MultiCellFootprintBlocksEveryCell` | `game-001` |
| 22 | `GAME-002` | `ResourceProducer_AddRemoveUpgrade_RecalculatesAndStopUnsubscribes` | `game-002` |
| 23 | `GAME-003` | `ResourceProducer_DarkElixirDrill_ProducesDarkElixirOnly` | `game-003` |
| 24 | `GAME-004` | `SceneChange_AllGameplayServices_RebindAndReleaseOldScene` | `game-004` |
| 25 | `GAME-005` | `ResourceProducer_NearCap_SaturatesWithoutOverflow` | `game-005` |
| 26 | `SCENE-002` | `LauncherPlayMode_EntersInteractiveVillage` | `scene-002` |

---

### Task 0: Runtime PlayMode Test Assembly

**Files:**
- Create: `Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef`
- Create: `Assets/Tests/Runtime/Support/RuntimeTestBase.cs`
- Create: `Assets/Tests/Runtime/Support/RuntimeAssemblySmokeTests.cs`

**Interfaces:**
- Consumes: Unity Test Framework `TestAssemblies`, NUnit, all runtime assemblies named in the asmdef.
- Produces: `ZWebGLFrameWork.Runtime.Tests.Support.RuntimeTestBase.CreateOwnedObject(string)`, `CreateTempDirectory(string)`, and one discoverable passing PlayMode test for every later task.

- [ ] **Step 1: Capture the zero-discovery red state**

Run after the compiler-baseline source audits are green but before creating `Assets/Tests/Runtime`:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime/task0-red"
set +e
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests \
  -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" \
  -testResults "$ZWEB_EVIDENCE_ROOT/runtime/task0-red/results.xml" \
  -logFile "$ZWEB_EVIDENCE_ROOT/runtime/task0-red/unity.log"
status=$?
set -e
if test -s "$ZWEB_EVIDENCE_ROOT/runtime/task0-red/results.xml" && \
   rg -Fq 'RuntimePlayModeAssembly_IsDiscoverable' \
     "$ZWEB_EVIDENCE_ROOT/runtime/task0-red/results.xml"; then
  echo "unexpected runtime PlayMode discovery before asmdef" >&2
  exit 1
fi
printf 'unity-exit=%d; RuntimePlayModeAssembly_IsDiscoverable absent\n' "$status" \
  | tee "$ZWEB_EVIDENCE_ROOT/runtime/task0-red/red.txt"
```

Expected: the named test is absent because no non-Editor runtime test assembly exists; `red.txt` is nonempty whether Unity reports zero discovery as exit `0` or nonzero.

- [ ] **Step 2: Create the non-Editor test assembly**

```json
{
    "name": "ZWebGLFrameWork.Runtime.Tests",
    "rootNamespace": "ZWebGLFrameWork.Runtime.Tests",
    "references": [
        "ZWebGLFrameWork.Core",
        "ZWebGLFrameWork.Data",
        "ZWebGLFrameWork.Gameplay",
        "ZWebGLFrameWork.UI",
        "ZWebGLFrameWork.FSM",
        "ZWebGLFrameWork.Combat",
        "UniTask",
        "UnityEngine.UI"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false,
    "optionalUnityReferences": [
        "TestAssemblies"
    ]
}
```

- [ ] **Step 3: Add deterministic cleanup support**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Runtime.Tests.Support
{
    public abstract class RuntimeTestBase
    {
        private readonly List<GameObject> _ownedObjects = new();
        private readonly List<string> _tempDirectories = new();
        private readonly List<string> _loadedSceneNames = new();
        private readonly List<Action> _cleanupActions = new();

        protected GameObject CreateOwnedObject(string name)
        {
            var instance = new GameObject($"RuntimeTest_{name}");
            _ownedObjects.Add(instance);
            return instance;
        }

        protected string CreateTempDirectory(string name)
        {
            string path = Path.Combine(
                Application.temporaryCachePath,
                $"zweb-runtime-{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            _tempDirectories.Add(path);
            return path;
        }

        protected void TrackLoadedScene(Scene scene)
        {
            if (scene.IsValid()) _loadedSceneNames.Add(scene.name);
        }

        protected void RegisterCleanup(Action cleanup)
        {
            _cleanupActions.Add(cleanup ?? throw new ArgumentNullException(nameof(cleanup)));
        }

        [SetUp]
        public void RuntimeSetUp()
        {
            EventBus.ResetInstance();
            EventBus.ClearAll();
            Time.timeScale = 1f;
        }

        [UnityTearDown]
        public IEnumerator RuntimeTearDown()
        {
            EventBus.ClearAll();
            Time.timeScale = 1f;

            foreach (var instance in _ownedObjects)
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            }
            _ownedObjects.Clear();

            // UIManager may create a persistent EventSystem outside the fixture's owned objects.
            foreach (EventSystem eventSystem in
                UnityEngine.Object.FindObjectsOfType<EventSystem>(true))
            {
                if (eventSystem != null)
                    UnityEngine.Object.Destroy(eventSystem.gameObject);
            }
            yield return null;

            foreach (string sceneName in _loadedSceneNames)
            {
                Scene scene = SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid() && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            _loadedSceneNames.Clear();

            for (int index = _cleanupActions.Count - 1; index >= 0; index--)
            {
                try { _cleanupActions[index](); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _cleanupActions.Clear();

            foreach (string path in _tempDirectories)
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            _tempDirectories.Clear();
        }
    }
}
```

- [ ] **Step 4: Add one nonzero discovery test**

Create `Assets/Tests/Runtime/Support/RuntimeAssemblySmokeTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace ZWebGLFrameWork.Runtime.Tests.Support
{
    public sealed class RuntimeAssemblySmokeTests
    {
        [Test]
        public void RuntimePlayModeAssembly_IsDiscoverable()
        {
            Assert.That(Application.isPlaying, Is.True);
        }
    }
}
```

- [ ] **Step 5: Import the assembly and capture the focused green result**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-task0-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-task0-green.log"
rg -F 'RuntimePlayModeAssembly_IsDiscoverable' "$ZWEB_EVIDENCE_ROOT/runtime/runtime-task0-green.xml"
```

Expected: Unity exits `0`; the XML reports `RuntimePlayModeAssembly_IsDiscoverable` Passed and a nonzero testcase count. CI still owns the checked-in expected-count manifest and `CI-003`.

- [ ] **Step 6: Commit the test boundary**

```bash
git add Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef \
  Assets/Tests/Runtime/Support/RuntimeTestBase.cs \
  Assets/Tests/Runtime/Support/RuntimeAssemblySmokeTests.cs
git commit -m "test: add runtime play mode assembly"
```

---

### Task 1: CORE-001 Invalidate Cache When Switching Resource Provider

**Files:**
- Create: `Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs`
- Modify: `Assets/Scripts/Core/Resource/ResourceManager.cs:54-59`

**Interfaces:**
- Consumes: `IResourceProvider.LoadAsync<T>(string)` and `UnloadUnused()`.
- Produces: `ResourceManager.SetProvider(IResourceProvider)` with null rejection and complete old-provider cache invalidation.

- [ ] **Step 1: Write the failing provider-switch test**

```csharp
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceManagerProviderTests : RuntimeTestBase
    {
        private sealed class AssetMarker : ScriptableObject { }

        private sealed class FakeProvider : IResourceProvider
        {
            private readonly Object _asset;
            public bool UnloadUnusedCalled { get; private set; }
            public bool SupportsSyncLoad => true;

            public FakeProvider(Object asset) => _asset = asset;
            public UniTask<T> LoadAsync<T>(string path) where T : Object =>
                UniTask.FromResult(_asset as T);
            public T Load<T>(string path) where T : Object => _asset as T;
            public void Unload(string path) { }
            public void UnloadUnused() => UnloadUnusedCalled = true;
        }

        [Test]
        public async System.Threading.Tasks.Task ResourceManager_SetProvider_InvalidatesOldProviderCache()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var firstProvider = new FakeProvider(firstAsset);
            var secondProvider = new FakeProvider(secondAsset);

            Assert.That(manager.Init(firstProvider), Is.True);
            Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"), Is.SameAs(firstAsset));

            manager.SetProvider(secondProvider);

            Assert.That(firstProvider.UnloadUnusedCalled, Is.True);
            Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"), Is.SameAs(secondAsset));
            Object.Destroy(firstAsset);
            Object.Destroy(secondAsset);
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify the stale cache**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Core.ResourceManagerProviderTests.ResourceManager_SetProvider_InvalidatesOldProviderCache" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/core-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/core-001-red.log"
rg -F 'ResourceManager_SetProvider_InvalidatesOldProviderCache' "$ZWEB_EVIDENCE_ROOT/runtime/core-001-red.xml"
```

Expected: FAIL because the second load returns `firstAsset` from `_cache`.

- [ ] **Step 3: Clear the cache before replacing the provider**

Replace `SetProvider` with:

```csharp
public void SetProvider(IResourceProvider newProvider)
{
    if (newProvider == null)
        throw new ArgumentNullException(nameof(newProvider));

    lock (_cacheLock)
    {
        _cache.Clear();
    }

    _provider?.UnloadUnused();
    _provider = newProvider;
    IsReady = true;
}
```

- [ ] **Step 4: Run the focused test and full runtime suite**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Core.ResourceManagerProviderTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/core-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/core-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-core-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-core-001.log"
```

Expected: both commands exit `0`; provider replacement returns the second asset.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs Assets/Scripts/Core/Resource/ResourceManager.cs
git commit -m "fix: invalidate resource cache on provider switch"
```

---

### Task 2: CORE-002 Align AssetBundle Dependency and Unload Contracts

**Files:**
- Create: `Assets/Tests/Runtime/Core/AssetBundleProviderContractTests.cs`
- Modify: `Assets/Scripts/Core/Resource/AssetBundleProvider.cs:27-113`

**Interfaces:**
- Consumes: asset paths in the existing `bundleName/assetName` format and the manifest's stable dependency list.
- Produces: `protected virtual UniTask<AssetBundle> EnsureBundleReadyAsync(string)`, `protected virtual IReadOnlyList<string> GetDependencies(string)`, per-bundle reference counts, `protected virtual void UnloadBundle(string)`, and public `Unload(string assetPath)` whose input matches `IResourceProvider`.

- [ ] **Step 1: Write failing dependency-order and unload-key tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class AssetBundleProviderContractTests
    {
        private sealed class ProbeProvider : AssetBundleProvider
        {
            public readonly List<string> Calls = new();
            public readonly List<string> UnloadedBundles = new();

            public override UniTask LoadDependenciesAsync(string bundleName)
            {
                Calls.Add($"deps:{bundleName}");
                return UniTask.CompletedTask;
            }

            public override UniTask<AssetBundle> LoadBundleAsync(string bundleName)
            {
                Calls.Add($"bundle:{bundleName}");
                return UniTask.FromResult<AssetBundle>(null);
            }

            protected override IReadOnlyList<string> GetDependencies(string bundleName) =>
                bundleName == "units" || bundleName == "buildings"
                    ? new[] { "shared-materials" }
                    : System.Array.Empty<string>();

            public void Retain(string bundleName) => RetainBundleReferences(bundleName);
            public void Release(string bundleName) => ReleaseBundleReferences(bundleName);

            protected override void UnloadBundle(string bundleName) =>
                UnloadedBundles.Add(bundleName);
        }

        [Test]
        public async System.Threading.Tasks.Task AssetBundleProvider_LoadAndUnload_UsesDependenciesAndBundleIdentity()
        {
            var provider = new ProbeProvider();
            await provider.LoadAsync<GameObject>("units/barbarian");
            CollectionAssert.AreEqual(
                new[] { "deps:units", "bundle:units" },
                provider.Calls);
            provider.Unload("units/barbarian");
            Assert.That(provider.UnloadedBundles, Has.Member("units"));
        }

        [Test]
        public void Unload_ConvertsAssetPathToBundleIdentity()
        {
            var provider = new ProbeProvider();
            provider.Unload("units/barbarian");
            Assert.That(provider.UnloadedBundles, Has.Member("units"));
        }

        [Test]
        public void SharedDependency_UnloadsOnlyAfterEveryOwnerReleases()
        {
            var provider = new ProbeProvider();
            provider.Retain("units");
            provider.Retain("buildings");

            provider.Release("units");
            Assert.That(provider.UnloadedBundles, Has.Member("units"));
            Assert.That(provider.UnloadedBundles, Has.No.Member("shared-materials"));

            provider.Release("buildings");
            Assert.That(provider.UnloadedBundles, Has.Member("buildings"));
            Assert.That(provider.UnloadedBundles.Count(
                value => value == "shared-materials"), Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run the focused class and verify it does not compile**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Core.AssetBundleProviderContractTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/core-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/core-002-red.log"
```

Expected: nonzero exit with compiler errors because `LoadDependenciesAsync` and `LoadBundleAsync` are not virtual and `UnloadBundle` does not exist.

- [ ] **Step 3: Implement one path identity and dependency-aware load sequence**

Make `LoadBundleAsync` and `LoadDependenciesAsync` `public virtual`. Add `_bundleRefCounts` and `_retainedDependencies` dictionaries guarded by `_bundleLock`; each successful asset load retains the main bundle and every distinct, ordinal-sorted dependency exactly once, while every matching `Unload(assetPath)` releases that same graph. Replace the load/unload portions with:

```csharp
protected virtual async UniTask<AssetBundle> EnsureBundleReadyAsync(string bundleName)
{
    await LoadDependenciesAsync(bundleName);
    return await LoadBundleAsync(bundleName);
}

protected virtual IReadOnlyList<string> GetDependencies(string bundleName)
{
    if (_manifest == null) return System.Array.Empty<string>();
    string[] dependencies = _manifest.GetAllDependencies(bundleName);
    System.Array.Sort(dependencies, System.StringComparer.Ordinal);
    return dependencies;
}

protected void RetainBundleReferences(string bundleName)
{
    lock (_bundleLock)
    {
        if (!_retainedDependencies.TryGetValue(bundleName, out string[] dependencies))
        {
            dependencies = GetDependencies(bundleName)
                .Distinct(System.StringComparer.Ordinal).ToArray();
            _retainedDependencies[bundleName] = dependencies;
        }
        IncrementReference(bundleName);
        foreach (string dependency in dependencies)
            IncrementReference(dependency);
    }
}

public async UniTask<T> LoadAsync<T>(string path) where T : Object
{
    var parts = path.Split('/', 2);
    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
        string.IsNullOrWhiteSpace(parts[1]))
    {
        Debug.LogError($"[AssetBundleProvider] 路径格式错误: {path}，应为 bundleName/assetName");
        return null;
    }

    AssetBundle bundle = await EnsureBundleReadyAsync(parts[0]);
    if (bundle == null) return null;
    RetainBundleReferences(parts[0]);

    AssetBundleRequest request = bundle.LoadAssetAsync<T>(parts[1]);
    await request.ToUniTask();
    return request.asset as T;
}

public void Unload(string assetPath)
{
    if (string.IsNullOrWhiteSpace(assetPath)) return;
    int separator = assetPath.IndexOf('/');
    string bundleName = separator < 0 ? assetPath : assetPath.Substring(0, separator);
    ReleaseBundleReferences(bundleName);
}

protected void ReleaseBundleReferences(string bundleName)
{
    lock (_bundleLock)
    {
        if (!_bundleRefCounts.ContainsKey(bundleName))
        {
            UnloadBundle(bundleName);
            return;
        }
        string[] dependencies = _retainedDependencies.TryGetValue(bundleName, out var retained)
            ? retained : System.Array.Empty<string>();
        DecrementAndUnload(bundleName);
        foreach (string dependency in dependencies)
            DecrementAndUnload(dependency);
        if (!_bundleRefCounts.ContainsKey(bundleName))
            _retainedDependencies.Remove(bundleName);
    }
}

private void IncrementReference(string bundleName) =>
    _bundleRefCounts[bundleName] = _bundleRefCounts.TryGetValue(bundleName, out int count)
        ? checked(count + 1) : 1;

private void DecrementAndUnload(string bundleName)
{
    int count = _bundleRefCounts[bundleName] - 1;
    if (count > 0) { _bundleRefCounts[bundleName] = count; return; }
    _bundleRefCounts.Remove(bundleName);
    UnloadBundle(bundleName);
}

protected virtual void UnloadBundle(string bundleName)
{
    if (!_loadedBundles.TryGetValue(bundleName, out AssetBundle bundle)) return;
    bundle.Unload(false);
    _loadedBundles.Remove(bundleName);
}
```

Add `using System.Linq;` and `using System.Collections.Generic;`. `UnloadUnused()` unloads each loaded bundle once and clears `_loadedBundles`, `_bundleRefCounts`, `_retainedDependencies`, and `_manifest` in the same lock. If loading the main asset fails, do not retain any references. If asset extraction throws after retention, release the graph in `catch` before rethrowing.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Core.AssetBundleProviderContractTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/core-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/core-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-core-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-core-002.log"
```

Expected: both commands exit `0`; call order is dependencies then main bundle, unload receives the normalized main identity, and a dependency shared by two owners unloads exactly once after the second owner releases it.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Core/AssetBundleProviderContractTests.cs Assets/Scripts/Core/Resource/AssetBundleProvider.cs
git commit -m "fix: align asset bundle load and unload contracts"
```

---

### Task 3: BOOT-001 Enter the Initial FSM State Without a Fake Self-Transition

**Files:**
- Create: `Assets/Tests/Runtime/FSM/InitialTransitionTests.cs`
- Modify: `Assets/Scripts/FSM/Core/FSM.cs:14-131`
- Modify: `Assets/Scripts/FSM/Game/GameFSM.cs:19-23,117-164,203-215`
- Modify: `Assets/Scripts/FSM/Game/GameEvents.cs:1-9`

**Interfaces:**
- Consumes: registered `FSMState<T>` instances and the existing `TransitionGuard`.
- Produces: `FSMTransition<T>`, `FSM<T>.HasCurrentState`, and a first transition that does not invoke the normal guard.

- [ ] **Step 1: Write the failing initial-transition test**

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class InitialTransitionTests
    {
        private enum StateId { Launch, Ready }
        private sealed class ProbeState : FSMState<StateId>
        {
            public ProbeState(StateId id) : base(id) { }
        }

        [Test]
        public void GameFSM_InitialTransition_EntersLaunchExactlyOnce()
        {
            var fsm = new FSM<StateId>();
            fsm.AddState(new ProbeState(StateId.Launch));
            int guardCalls = 0;
            var transitions = new List<FSMTransition<StateId>>();
            fsm.TransitionGuard = (_, _) => { guardCalls++; return false; };
            fsm.OnStateChanged += transitions.Add;

            bool changed = fsm.ChangeState(StateId.Launch);

            Assert.That(changed, Is.True);
            Assert.That(fsm.HasCurrentState, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Launch));
            Assert.That(guardCalls, Is.Zero);
            Assert.That(transitions, Has.Count.EqualTo(1));
            Assert.That(transitions[0].HasOldState, Is.False);
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify the red state**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.InitialTransitionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-001-red.log"
```

Expected: nonzero exit because `FSMTransition<T>` and `HasCurrentState` do not exist; before that API is introduced, the current guard also rejects the initial `Launch` transition.

- [ ] **Step 3: Add an explicit transition payload and bypass the guard only when there is no current state**

Add before `FSM<T>`:

```csharp
public readonly struct FSMTransition<T> where T : System.Enum
{
    public bool HasOldState { get; }
    public T OldState { get; }
    public T NewState { get; }
    public object Data { get; }

    public FSMTransition(bool hasOldState, T oldState, T newState, object data)
    {
        HasOldState = hasOldState;
        OldState = oldState;
        NewState = newState;
        Data = data;
    }
}
```

Change the FSM properties/event and guard block to:

```csharp
public bool HasCurrentState => _currentState != null;
public T CurrentStateId => _currentState != null ? _currentState.StateId : default;
public event Action<FSMTransition<T>> OnStateChanged;

bool hasOldState = _currentState != null;
if (hasOldState && !CanTransitionTo(_currentState.StateId, stateId))
    return false;

var oldState = _currentState;
if (oldState != null)
    oldState.OnExit();

_previousState = oldState;
T oldStateId = oldState != null ? oldState.StateId : default;
_currentState = newState;
newState.OnEnter(data);
OnStateChanged?.Invoke(new FSMTransition<T>(
    hasOldState,
    oldStateId,
    stateId,
    data));
return true;
```

Update `GameStateChangedEvent` and `GameFSM.OnInternalStateChanged`:

```csharp
public struct GameStateChangedEvent
{
    public bool HasOldState;
    public GameState OldState;
    public GameState NewState;
    public object Data;
}

private void OnInternalStateChanged(FSMTransition<GameState> transition)
{
    EventBus.Publish(EventStateChanged, new GameStateChangedEvent
    {
        HasOldState = transition.HasOldState,
        OldState = transition.OldState,
        NewState = transition.NewState,
        Data = transition.Data,
    });
}
```

In `GameFSM.ChangeState`, run `CanTransition` only when `_fsm.HasCurrentState`; keep `Start()` calling `_fsm.ChangeState(GameState.Launch)`.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.InitialTransitionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-001.log"
```

Expected: both commands exit `0`; the initial event has `HasOldState == false` and the guard was not called.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/InitialTransitionTests.cs Assets/Scripts/FSM/Core/FSM.cs Assets/Scripts/FSM/Game/GameFSM.cs Assets/Scripts/FSM/Game/GameEvents.cs
git commit -m "fix: define initial fsm transition semantics"
```

---

### Task 4: FSM-001 Queue Reentrant State Changes Until the Current Transition Commits

**Files:**
- Create: `Assets/Tests/Runtime/FSM/ReentrantTransitionTests.cs`
- Modify: `Assets/Scripts/FSM/Core/FSM.cs:89-131`

**Interfaces:**
- Consumes: `FSMTransition<T>` from Task 3.
- Produces: ordered `ChangeState` processing where an `OnEnter` redirect is applied after the outer transition event.

- [ ] **Step 1: Write the failing reentrant-transition test**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class ReentrantTransitionTests
    {
        private enum StateId { A, B, C }
        private sealed class PlainState : FSMState<StateId>
        {
            public PlainState(StateId id) : base(id) { }
        }
        private sealed class RedirectState : FSMState<StateId>
        {
            public RedirectState() : base(StateId.B) { }
            public override void OnEnter(object data = null)
            {
                base.OnEnter(data);
                ChangeState(StateId.C);
            }
        }

        [Test]
        public void FSM_OnEnterRedirect_EmitsOnlyOrderedCommittedTransitions()
        {
            var fsm = new FSM<StateId>();
            fsm.AddStates(
                new PlainState(StateId.A),
                new RedirectState(),
                new PlainState(StateId.C));
            fsm.TransitionGuard = (_, _) => true;
            var observed = new List<string>();
            fsm.OnStateChanged += transition =>
                observed.Add($"{transition.OldState}>{transition.NewState}");
            Assert.That(fsm.ChangeState(StateId.A), Is.True);
            observed.Clear();

            Assert.That(fsm.ChangeState(StateId.B), Is.True);

            CollectionAssert.AreEqual(new[] { "A>B", "B>C" }, observed);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.C));
        }
    }
}
```

- [ ] **Step 2: Run the test and capture the reversed event order**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.ReentrantTransitionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/fsm-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/fsm-001-red.log"
```

Expected: FAIL with actual order `B>C, A>B`.

- [ ] **Step 3: Queue redirects while a transition is executing**

Add these fields and request type to `FSM<T>`:

```csharp
private readonly Queue<TransitionRequest> _pendingTransitions = new();
private bool _isTransitioning;

private readonly struct TransitionRequest
{
    public T StateId { get; }
    public object Data { get; }
    public TransitionRequest(T stateId, object data)
    {
        StateId = stateId;
        Data = data;
    }
}
```

Replace `ChangeState` with the queue and move the existing single transition body into `ApplyTransition`:

```csharp
public bool ChangeState(T stateId, object data = null)
{
    if (_isTransitioning)
    {
        _pendingTransitions.Enqueue(new TransitionRequest(stateId, data));
        return true;
    }

    bool firstResult = false;
    bool first = true;
    _pendingTransitions.Enqueue(new TransitionRequest(stateId, data));
    _isTransitioning = true;
    try
    {
        while (_pendingTransitions.Count > 0)
        {
            TransitionRequest request = _pendingTransitions.Dequeue();
            bool result = ApplyTransition(request.StateId, request.Data);
            if (first)
            {
                firstResult = result;
                first = false;
            }
        }
    }
    finally
    {
        _isTransitioning = false;
        _pendingTransitions.Clear();
    }
    return firstResult;
}

private bool ApplyTransition(T stateId, object data)
{
    if (!_states.TryGetValue(stateId, out FSMState<T> newState))
        return false;
    if (_currentState == newState)
        return false;

    bool hasOldState = _currentState != null;
    if (hasOldState && !CanTransitionTo(_currentState.StateId, stateId))
        return false;

    FSMState<T> oldState = _currentState;
    oldState?.OnExit();
    _previousState = oldState;
    T oldStateId = oldState != null ? oldState.StateId : default;
    _currentState = newState;
    newState.OnEnter(data);
    OnStateChanged?.Invoke(new FSMTransition<T>(hasOldState, oldStateId, stateId, data));
    return true;
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.ReentrantTransitionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/fsm-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/fsm-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-fsm-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-fsm-001.log"
```

Expected: both commands exit `0`; the event sequence is `A>B`, then `B>C`.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/ReentrantTransitionTests.cs Assets/Scripts/FSM/Core/FSM.cs
git commit -m "fix: serialize reentrant fsm transitions"
```

---

### Task 5: FSM-002 Give Every State an Exit-Cancelled Lifetime Token

**Files:**
- Create: `Assets/Tests/Runtime/FSM/StateLifetimeTests.cs`
- Modify: `Assets/Scripts/FSM/Core/FSMState.cs:1-60`

**Interfaces:**
- Consumes: every concrete state's existing `base.OnEnter(data)` and `base.OnExit()` calls.
- Produces: protected `CancellationToken LifetimeToken`, recreated on enter and cancelled on exit.

- [ ] **Step 1: Write the failing lifetime test**

```csharp
using System.Threading;
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class StateLifetimeTests
    {
        private enum StateId { Active, Done }
        private sealed class TokenState : FSMState<StateId>
        {
            public TokenState(StateId id) : base(id) { }
            public CancellationToken ExposedToken => LifetimeToken;
        }

        [Test]
        public void StateLifetime_ExitCancelsTokenAndReentryRenewsIt()
        {
            var active = new TokenState(StateId.Active);
            var done = new TokenState(StateId.Done);
            var fsm = new FSM<StateId>();
            fsm.AddStates(active, done);
            fsm.TransitionGuard = (_, _) => true;
            Assert.That(fsm.ChangeState(StateId.Active), Is.True);
            CancellationToken first = active.ExposedToken;

            Assert.That(fsm.ChangeState(StateId.Done), Is.True);
            Assert.That(first.IsCancellationRequested, Is.True);
            bool lateStartCommitted = false;
            if (!first.IsCancellationRequested)
                lateStartCommitted = true;
            Assert.That(lateStartCommitted, Is.False);

            Assert.That(fsm.ChangeState(StateId.Active), Is.True);
            Assert.That(active.ExposedToken.IsCancellationRequested, Is.False);
            Assert.That(active.ExposedToken, Is.Not.EqualTo(first));
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify the missing token API**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.StateLifetimeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/fsm-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/fsm-002-red.log"
```

Expected: nonzero exit with `CS0103` for `LifetimeToken`.

- [ ] **Step 3: Implement the state lifetime in the base class**

Add `using System.Threading;` and replace the base lifecycle methods with:

```csharp
private CancellationTokenSource _lifetimeSource;
protected CancellationToken LifetimeToken =>
    _lifetimeSource?.Token ?? CancellationToken.None;

public virtual void OnEnter(object data = null)
{
    _lifetimeSource?.Dispose();
    _lifetimeSource = new CancellationTokenSource();
    IsActive = true;
    EnterData = data;
}

public virtual void OnExit()
{
    IsActive = false;
    EnterData = null;
    if (_lifetimeSource == null) return;
    _lifetimeSource.Cancel();
    _lifetimeSource.Dispose();
    _lifetimeSource = null;
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.StateLifetimeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/fsm-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/fsm-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-fsm-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-fsm-002.log"
```

Expected: both commands exit `0`; the base-state token is cancelled and reentry owns a fresh token. This base test is necessary but is not the `FSM-002` acceptance proof: Simulation Task 15 must execute the real `BattleState_ExitBeforeStartCompletes_CancelsLateStart` red/green test, and runtime ledger closure consumes both commits and those concrete artifacts.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/StateLifetimeTests.cs Assets/Scripts/FSM/Core/FSMState.cs
git commit -m "feat: bind async work to state lifetime"
```

---

### Task 6: BOOT-002 Suspend and Resume a State Without Re-entering It

**Files:**
- Create: `Assets/Tests/Runtime/FSM/PauseResumeTests.cs`
- Modify: `Assets/Scripts/FSM/Core/FSMState.cs:29-46`
- Modify: `Assets/Scripts/FSM/Core/FSM.cs:14-174`
- Modify: `Assets/Scripts/FSM/Game/GameFSM.cs:78-106`
- Modify: `Assets/Scripts/FSM/Game/GameEvents.cs:19-33`

**Interfaces:**
- Consumes: `FSMTransition<T>` and state lifetime from Tasks 3 and 5.
- Produces: `FSM<T>.PushState(T, object)`, `PopState(object)`, `FSMState<T>.OnSuspend(object)`, and `OnResume(object)`.

- [ ] **Step 1: Write the failing typed-context pause test**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class PauseResumeTests
    {
        private enum StateId { Battle, Pause }
        private sealed class BattleContext { public int Seed; }
        private sealed class BattleProbe : FSMState<StateId>
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public int ResumeCount { get; private set; }
            public BattleProbe() : base(StateId.Battle) { }
            public override void OnEnter(object data = null) { base.OnEnter(data); EnterCount++; }
            public override void OnExit() { ExitCount++; base.OnExit(); }
            public override void OnResume(object data) { base.OnResume(data); ResumeCount++; }
        }
        private sealed class PauseProbe : FSMState<StateId>
        {
            public int ExitCount { get; private set; }
            public PauseProbe() : base(StateId.Pause) { }
            public override void OnExit() { ExitCount++; base.OnExit(); }
        }

        [Test]
        public void GameFSM_PauseResume_PreservesBattleContext()
        {
            var battle = new BattleProbe();
            var fsm = new FSM<StateId>();
            fsm.AddStates(battle, new PauseProbe());
            fsm.TransitionGuard = (_, _) => true;
            var context = new BattleContext { Seed = 42 };
            Assert.That(fsm.ChangeState(StateId.Battle, context), Is.True);

            Assert.That(fsm.PushState(StateId.Pause, "background"), Is.True);
            Assert.That(fsm.PopState(3.5f), Is.True);

            Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
            Assert.That(battle.EnterCount, Is.EqualTo(1));
            Assert.That(battle.ExitCount, Is.Zero);
            Assert.That(battle.ResumeCount, Is.EqualTo(1));
            Assert.That(battle.EnterData, Is.SameAs(context));
        }

        [Test]
        public void Clear_ExitsCurrentAndSuspendedStates()
        {
            var battle = new BattleProbe();
            var pause = new PauseProbe();
            var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
            fsm.AddStates(battle, pause);
            Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
            Assert.That(fsm.PushState(StateId.Pause), Is.True);

            fsm.Clear();

            Assert.That(battle.ExitCount, Is.EqualTo(1));
            Assert.That(pause.ExitCount, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify suspension APIs are absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.PauseResumeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-002-red.log"
```

Expected: nonzero exit with missing `PushState`, `PopState`, and `OnResume` members.

- [ ] **Step 3: Add suspend/resume hooks and a suspended-state stack**

Add to `FSMState<T>`:

```csharp
public virtual void OnSuspend(object data)
{
    IsActive = false;
}

public virtual void OnResume(object data)
{
    IsActive = true;
}
```

Add `_suspendedStates` and these methods to `FSM<T>`:

```csharp
private readonly Stack<FSMState<T>> _suspendedStates = new();

public bool PushState(T stateId, object data = null)
{
    if (_currentState == null || !_states.TryGetValue(stateId, out FSMState<T> overlay))
        return false;
    if (!CanTransitionTo(_currentState.StateId, stateId))
        return false;

    FSMState<T> suspended = _currentState;
    suspended.OnSuspend(data);
    _suspendedStates.Push(suspended);
    _previousState = suspended;
    _currentState = overlay;
    overlay.OnEnter(data);
    OnStateChanged?.Invoke(new FSMTransition<T>(
        true, suspended.StateId, overlay.StateId, data));
    return true;
}

public bool PopState(object data = null)
{
    if (_currentState == null || _suspendedStates.Count == 0)
        return false;

    FSMState<T> overlay = _currentState;
    FSMState<T> resumed = _suspendedStates.Pop();
    overlay.OnExit();
    _previousState = overlay;
    _currentState = resumed;
    resumed.OnResume(data);
    OnStateChanged?.Invoke(new FSMTransition<T>(
        true, overlay.StateId, resumed.StateId, data));
    return true;
}
```

Replace `Clear()` so every active or suspended lifetime is exited and cancelled before registration ownership is removed:

```csharp
public void Clear()
{
    _currentState?.OnExit();
    _currentState = null;
    while (_suspendedStates.Count > 0)
        _suspendedStates.Pop().OnExit();
    _previousState = null;
    _pendingTransitions.Clear();
    _isTransitioning = false;
    foreach (KeyValuePair<T, FSMState<T>> pair in _states)
        pair.Value.Owner = null;
    _states.Clear();
}
```

In `GameFSM.OnApplicationPause`, replace the Pause `ChangeState` call with `_fsm.PushState(GameState.Pause, GamePauseEvent.Background)` and replace resume `ChangeState` with `_fsm.PopState(new GameResumeEvent { PausedDuration = duration })`. Remove the MainVillage/Battle-only fallback because the actual suspended state is retained.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.PauseResumeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-002.log"
```

Expected: both commands exit `0`; Battle enters once, never exits during pause, and resumes with its original context intact.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/PauseResumeTests.cs Assets/Scripts/FSM/Core/FSMState.cs Assets/Scripts/FSM/Core/FSM.cs Assets/Scripts/FSM/Game/GameFSM.cs Assets/Scripts/FSM/Game/GameEvents.cs
git commit -m "fix: preserve state context across pause resume"
```

---

### Task 7: BOOT-003 Cancel Launch Work on State Exit

**Files:**
- Create: `Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs`
- Create: `Assets/Scripts/FSM/Game/IBootPipeline.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:14-240`
- Modify: `Assets/Scripts/FSM/Game/HotUpdateManager.cs:52-220`

**Interfaces:**
- Consumes: `FSMState<GameState>.LifetimeToken` from Task 5.
- Produces: `IBootPipeline.RunAsync(CancellationToken)` as a testable launch boundary; all default Launch awaits observe the same token.

- [ ] **Step 1: Write the failing exit-cancellation test**

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class LaunchCancellationTests
    {
        private sealed class BlockingPipeline : IBootPipeline
        {
            public bool Cancelled { get; private set; }
            public async UniTask<GameState?> RunAsync(CancellationToken token)
            {
                try
                {
                    await UniTask.Delay(60000, cancellationToken: token);
                    return GameState.Login;
                }
                catch (System.OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }
        }
        private sealed class VillageProbe : FSMState<GameState>
        {
            public VillageProbe() : base(GameState.MainVillage) { }
        }

        [UnityTest]
        public System.Collections.IEnumerator LaunchState_Exit_CancelsPipelineAndPreventsLateTransition()
        {
            var pipeline = new BlockingPipeline();
            var fsm = new FSM<GameState>();
            fsm.AddStates(new LaunchState(pipeline), new VillageProbe());
            fsm.TransitionGuard = (_, _) => true;
            Assert.That(fsm.ChangeState(GameState.Launch), Is.True);
            Assert.That(fsm.ChangeState(GameState.MainVillage), Is.True);
            yield return null;

            Assert.That(pipeline.Cancelled, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify the missing pipeline boundary**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.LaunchCancellationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-003-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-003-red.log"
```

Expected: nonzero exit because `IBootPipeline` and the injectable `LaunchState` constructor do not exist.

- [ ] **Step 3: Add the launch boundary and pass one token through every await**

Create `IBootPipeline.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ZWebGLFrameWork.FSM
{
    public interface IBootPipeline
    {
        UniTask<GameState?> RunAsync(CancellationToken token);
    }
}
```

Add to `LaunchState`:

```csharp
private readonly IBootPipeline _pipelineOverride;

public LaunchState() : this(null) { }
public LaunchState(IBootPipeline pipelineOverride) : base(GameState.Launch)
{
    _pipelineOverride = pipelineOverride;
}

public override void OnEnter(object data = null)
{
    base.OnEnter(data);
    EventBus.Subscribe("Boot_Retry", OnBootRetry);
    EventBus.Subscribe("Boot_SkipToOffline", OnBootSkip);
    RunInitPipeline(LifetimeToken).Forget();
}

private async UniTaskVoid RunInitPipeline(CancellationToken token)
{
    _pipelineRunning = true;
    try
    {
        if (_pipelineOverride != null)
        {
            GameState? target = await _pipelineOverride.RunAsync(token);
            token.ThrowIfCancellationRequested();
            if (target.HasValue) ChangeState(target.Value);
            return;
        }

        AppConfig cfg = GameLauncher.Config;
        if (cfg == null)
        {
            PublishError("AppConfig", "配置文件加载失败，请重新安装游戏", false);
            return;
        }

        float progress = InitCoreModules(cfg);
        if (!await HandleHotUpdate(cfg, progress, token)) return;
        GameState targetState = await PreloadAndChooseTarget(cfg, token);
        token.ThrowIfCancellationRequested();
        ChangeState(targetState);
    }
    catch (System.OperationCanceledException)
    {
    }
    catch (System.Exception ex)
    {
        if (!token.IsCancellationRequested)
            PublishError("LaunchPipeline", $"启动管线崩溃: {ex.Message}", false);
    }
    finally
    {
        _pipelineRunning = false;
    }
}
```

Change `HandleHotUpdate` and `PreloadAndChooseTarget` to accept `CancellationToken`; pass it to UniTask delay calls and use `AttachExternalCancellation(token)` for preload APIs that do not own cancellation. Make `OnExit` call `base.OnExit()` before closing panels so cancellation happens before cleanup. Add optional `CancellationToken token = default` to `HotUpdateManager.CheckVersionAsync`, `DownloadUpdatesAsync`, and `DownloadFileAsync`; call `token.ThrowIfCancellationRequested()` inside loops and dispose/abort the active `UnityWebRequest` in `finally`.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.LaunchCancellationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-003-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-003-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-003.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-003.log"
```

Expected: both commands exit `0`; state exit cancels the pipeline and no late Login transition occurs.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs Assets/Scripts/FSM/Game/IBootPipeline.cs Assets/Scripts/FSM/Game/States/LaunchState.cs Assets/Scripts/FSM/Game/HotUpdateManager.cs
git commit -m "fix: cancel launch pipeline on state exit"
```

---

### Task 8: BOOT-004 Stop Boot on Failed Download or Required Restart

**Files:**
- Create: `Assets/Tests/Runtime/FSM/HotUpdateDecisionTests.cs`
- Create: `Assets/Scripts/FSM/Game/HotUpdateDecision.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:114-145`

**Interfaces:**
- Consumes: `VersionCheckResult`, download success, and `HotUpdateManager.NeedRestart`.
- Produces: exhaustive `HotUpdateAction` values `Continue`, `Retry`, `ForceUpdate`, and `RestartRequired`.

- [ ] **Step 1: Write separate failing tests for both ignored branches**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class HotUpdateDecisionTests
    {
        [Test]
        public void LaunchState_HotUpdateFailure_DoesNotContinue()
        {
            Assert.That(
                HotUpdateDecision.Evaluate(VersionCheckResult.NeedUpdate, false, false),
                Is.EqualTo(HotUpdateAction.Retry));
        }

        [Test]
        public void LaunchState_NeedRestart_DoesNotContinue()
        {
            Assert.That(
                HotUpdateDecision.Evaluate(VersionCheckResult.NeedUpdate, true, true),
                Is.EqualTo(HotUpdateAction.RestartRequired));
        }
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the decision API is missing**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.HotUpdateDecisionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-004-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-004-red.log"
```

Expected: nonzero exit because `HotUpdateDecision` and `HotUpdateAction` do not exist.

- [ ] **Step 3: Implement and consume the exhaustive decision**

Create `HotUpdateDecision.cs`:

```csharp
namespace ZWebGLFrameWork.FSM
{
    public enum HotUpdateAction
    {
        Continue,
        Retry,
        ForceUpdate,
        RestartRequired,
    }

    public static class HotUpdateDecision
    {
        public static HotUpdateAction Evaluate(
            VersionCheckResult check,
            bool downloadSucceeded,
            bool needRestart)
        {
            if (check == VersionCheckResult.ForceUpdate)
                return HotUpdateAction.ForceUpdate;
            if (check == VersionCheckResult.Error)
                return HotUpdateAction.Retry;
            if (check == VersionCheckResult.NeedUpdate && !downloadSucceeded)
                return HotUpdateAction.Retry;
            if (needRestart)
                return HotUpdateAction.RestartRequired;
            return HotUpdateAction.Continue;
        }
    }
}
```

In `LaunchState.HandleHotUpdate`, always capture `downloadSucceeded`, evaluate once, and return `false` for Retry, ForceUpdate, and RestartRequired. For RestartRequired, close Loading, open `ForceUpdatePanel` with restart copy, and do not call `PreloadAndChooseTarget`.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.HotUpdateDecisionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-004-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-004-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-004.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-004.log"
```

Expected: both commands exit `0`; each ignored branch maps to a non-Continue action.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/HotUpdateDecisionTests.cs Assets/Scripts/FSM/Game/HotUpdateDecision.cs Assets/Scripts/FSM/Game/States/LaunchState.cs
git commit -m "fix: stop boot for failed or pending hot update"
```

---

### Task 9: BOOT-005 Return Explicit Scene-Load Failure and Wait for Activation

**Files:**
- Create: `Assets/Tests/Runtime/FSM/SceneLoadingTests.cs`
- Create: `Assets/Scripts/Core/Scene/SceneLoadResult.cs`
- Create: `Assets/Scripts/Core/Scene/SceneLoadRunner.cs`
- Modify: `Assets/Scripts/Core/Scene/SceneLoader.cs:53-99`
- Modify: `Assets/Scripts/FSM/Game/States/LoadingState.cs:1-91`

**Interfaces:**
- Consumes: `CancellationToken` from Task 5 and Unity `AsyncOperation` through a small adapter.
- Produces: `UniTask<SceneLoadResult> LoadSceneAsync(string, LoadSceneMode, CancellationToken)`; success is returned only after `AsyncOperation.isDone`.

- [ ] **Step 1: Write failing timeout and missing-scene tests**

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    public sealed class SceneLoadingTests : RuntimeTestBase
    {
        private sealed class ImmediateLoader : ISceneLoadService
        {
            private readonly SceneLoadResult _result;
            public ImmediateLoader(SceneLoadStatus status) =>
                _result = new SceneLoadResult(status, status.ToString());
            public UniTask<SceneLoadResult> LoadSceneAsync(string sceneName,
                UnityEngine.SceneManagement.LoadSceneMode mode, CancellationToken token) =>
                UniTask.FromResult(_result);
        }

        private sealed class BlockingLoader : ISceneLoadService
        {
            public bool Cancelled { get; private set; }
            public async UniTask<SceneLoadResult> LoadSceneAsync(string sceneName,
                UnityEngine.SceneManagement.LoadSceneMode mode, CancellationToken token)
            {
                try
                {
                    await UniTask.Delay(60000, cancellationToken: token);
                    return new SceneLoadResult(SceneLoadStatus.Succeeded);
                }
                catch (System.OperationCanceledException)
                {
                    Cancelled = true;
                    return new SceneLoadResult(SceneLoadStatus.Cancelled);
                }
            }
        }

        private sealed class ControlledOperation : ISceneLoadOperation
        {
            public float Progress { get; set; }
            public bool IsDone { get; set; }
            public bool AllowSceneActivation { get; set; }
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_MissingScene_PublishesFailureOnly()
        {
            yield return AssertFailureOnly(new ImmediateLoader(SceneLoadStatus.NotFound));
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_SceneTimeout_PublishesFailureOnly()
        {
            yield return AssertFailureOnly(new ImmediateLoader(SceneLoadStatus.Timeout));
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_DoesNotCompleteBeforeActivation()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 2f, CancellationToken.None, () => 0.1f);
            yield return null;
            Assert.That(operation.AllowSceneActivation, Is.True);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            operation.IsDone = true;
            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Succeeded)));
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_CancelBeforeActivation_DoesNotActivate()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 2f, cancelled.Token, () => 0.1f);

            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Cancelled)));
            Assert.That(operation.AllowSceneActivation, Is.False);
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadRunner_ActivationPhaseTimesOut()
        {
            var operation = new ControlledOperation { Progress = 0.9f, IsDone = false };
            using var cleanup = new CancellationTokenSource();
            UniTask<SceneLoadResult> task = SceneLoadRunner.RunAsync(
                operation, 1f, cleanup.Token, () => 0.6f);
            yield return null;
            yield return null;
            yield return null;
            bool completedBeforeCleanup = task.Status != UniTaskStatus.Pending;
            cleanup.Cancel();
            yield return task.ToCoroutine(result =>
                Assert.That(result.Status, Is.EqualTo(SceneLoadStatus.Timeout)));
            Assert.That(completedBeforeCleanup, Is.True,
                "activation phase must use the same timeout budget");
        }

        [UnityTest]
        public System.Collections.IEnumerator LoadingState_Exit_CancelsUnderlyingSceneLoad()
        {
            var loader = new BlockingLoader();
            var fsm = CreateFsm(loader);
            Assert.That(fsm.ChangeState(GameState.Loading, Payload()), Is.True);
            Assert.That(fsm.ChangeState(GameState.MainVillage), Is.True);
            yield return null;
            Assert.That(loader.Cancelled, Is.True);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }

        private static System.Collections.IEnumerator AssertFailureOnly(ISceneLoadService loader)
        {
            int failed = 0;
            int completed = 0;
            EventBus.Subscribe<SceneLoadStatus>("LoadingFailed", _ => failed++);
            EventBus.Subscribe("LoadingComplete", () => completed++);
            var fsm = CreateFsm(loader);
            Assert.That(fsm.ChangeState(GameState.Loading, Payload()), Is.True);
            yield return null;
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(completed, Is.Zero);
            Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.MainVillage));
        }

        private static FSM<GameState> CreateFsm(ISceneLoadService loader)
        {
            var fsm = new FSM<GameState> { TransitionGuard = (_, _) => true };
            fsm.AddStates(new LoadingState(loader),
                new ProbeState(GameState.MainVillage), new ProbeState(GameState.Battle));
            return fsm;
        }

        private static LoadingPayload Payload() => new LoadingPayload
        {
            TargetScene = "test-scene",
            TargetState = GameState.Battle,
        };

        private sealed class ProbeState : FSMState<GameState>
        {
            public ProbeState(GameState id) : base(id) { }
        }
    }
}
```

- [ ] **Step 2: Run the test and verify explicit results are absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.SceneLoadingTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-005-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-005-red.log"
```

Expected: nonzero exit because `SceneLoadResult`, `SceneLoadRunner`, and the cancellable overload do not exist.

- [ ] **Step 3: Implement result types, the operation runner, and result-aware LoadingState**

Create `SceneLoadResult.cs`:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Core
{
    public enum SceneLoadStatus { Succeeded, NotFound, Timeout, Cancelled, Failed }
    public readonly struct SceneLoadResult
    {
        public SceneLoadStatus Status { get; }
        public string Error { get; }
        public bool Succeeded => Status == SceneLoadStatus.Succeeded;
        public SceneLoadResult(SceneLoadStatus status, string error = null)
        {
            Status = status;
            Error = error;
        }
    }

    public interface ISceneLoadOperation
    {
        float Progress { get; }
        bool IsDone { get; }
        bool AllowSceneActivation { get; set; }
    }

    public interface ISceneLoadService
    {
        UniTask<SceneLoadResult> LoadSceneAsync(
            string sceneName, LoadSceneMode mode, CancellationToken token);
    }
}
```

Create `SceneLoadRunner.cs`:

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    public static class SceneLoadRunner
    {
        public static async UniTask<SceneLoadResult> RunAsync(
            ISceneLoadOperation operation,
            float timeoutSeconds,
            CancellationToken token,
            Func<float> deltaTime = null,
            Action<float> reportProgress = null)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            Func<float> elapsedStep = deltaTime ?? (() => Time.unscaledDeltaTime);
            float elapsed = 0f;
            while (true)
            {
                if (token.IsCancellationRequested)
                    return new SceneLoadResult(SceneLoadStatus.Cancelled);
                elapsed += elapsedStep();
                if (elapsed > timeoutSeconds)
                    return new SceneLoadResult(SceneLoadStatus.Timeout, "Scene load timed out");

                if (operation.Progress < 0.9f)
                {
                    reportProgress?.Invoke(Mathf.Clamp01(operation.Progress / 0.9f));
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    continue;
                }

                // Cancellation is checked before this first irreversible activation request.
                if (!operation.AllowSceneActivation)
                    operation.AllowSceneActivation = true;
                if (operation.IsDone)
                    break;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            reportProgress?.Invoke(1f);
            return new SceneLoadResult(SceneLoadStatus.Succeeded);
        }
    }
}
```

Add `using System;`, `using System.Threading;`, make `SceneLoader` implement `ISceneLoadService`, and replace its current load method with this adapter and overload:

```csharp
private sealed class UnitySceneLoadOperation : ISceneLoadOperation
{
    private readonly AsyncOperation _operation;
    public UnitySceneLoadOperation(AsyncOperation operation) =>
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
    public float Progress => _operation.progress;
    public bool IsDone => _operation.isDone;
    public bool AllowSceneActivation
    {
        get => _operation.allowSceneActivation;
        set => _operation.allowSceneActivation = value;
    }
}

public async UniTask<SceneLoadResult> LoadSceneAsync(
    string sceneName,
    LoadSceneMode mode = LoadSceneMode.Single,
    CancellationToken token = default)
{
    LoadingSceneName = sceneName;
    Progress = 0f;
    EventBus.Publish("SceneLoadStarted", sceneName);
    try
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, mode);
        if (operation == null)
            return new SceneLoadResult(SceneLoadStatus.NotFound,
                $"Scene is missing from Build Settings: {sceneName}");
        operation.allowSceneActivation = false;
        SceneLoadResult result = await SceneLoadRunner.RunAsync(
            new UnitySceneLoadOperation(operation), 30f, token,
            reportProgress: value => Progress = value);
        if (!result.Succeeded)
            Debug.LogError($"[SceneLoader] Load failed: {sceneName} ({result.Status})");
        return result;
    }
    catch (ArgumentException exception)
    {
        return new SceneLoadResult(SceneLoadStatus.NotFound, exception.Message);
    }
    catch (Exception exception)
    {
        return new SceneLoadResult(SceneLoadStatus.Failed, exception.Message);
    }
    finally
    {
        LoadingSceneName = null;
    }
}
```

Replace `LoadingState`'s coroutine fields and methods with:

```csharp
private readonly ISceneLoadService _sceneLoader;

public LoadingState() : this(SceneLoader.Instance) { }

public LoadingState(ISceneLoadService sceneLoader) : base(GameState.Loading)
{
    _sceneLoader = sceneLoader ?? throw new System.ArgumentNullException(nameof(sceneLoader));
}

public override void OnEnter(object data = null)
{
    base.OnEnter(data);
    Progress = 0f;
    if (!(data is LoadingPayload payload))
    {
        EventBus.Publish("LoadingFailed", SceneLoadStatus.Failed);
        ChangeState(GameState.MainVillage);
        return;
    }
    EventBus.Publish("LoadingStarted");
    LoadSequenceAsync(payload, LifetimeToken).Forget();
}

private async UniTaskVoid LoadSequenceAsync(
    LoadingPayload payload, System.Threading.CancellationToken token)
{
    try
    {
        SceneLoadResult result;
        if (string.IsNullOrEmpty(payload.TargetScene))
        {
            await UniTask.Delay(100, cancellationToken: token);
            result = new SceneLoadResult(SceneLoadStatus.Succeeded);
        }
        else
        {
            UniTask<SceneLoadResult> pending = _sceneLoader.LoadSceneAsync(
                payload.TargetScene, UnityEngine.SceneManagement.LoadSceneMode.Single, token);
            while (pending.Status == UniTaskStatus.Pending)
            {
                Progress = SceneLoader.HasInstance ? SceneLoader.Instance.Progress : Progress;
                await UniTask.Yield(PlayerLoopTiming.Update);
                token.ThrowIfCancellationRequested();
            }
            result = await pending;
        }

        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            Progress = 0f;
            EventBus.Publish("LoadingFailed", result.Status);
            ChangeState(GameState.MainVillage);
            return;
        }
        Progress = 1f;
        EventBus.Publish("LoadingComplete");
        ChangeState(payload.TargetState, payload.TargetData);
    }
    catch (System.OperationCanceledException)
    {
    }
}

public override void OnExit()
{
    base.OnExit();
    Progress = 1f;
}
```

Remove `using System.Collections`, `_activeCoroutine`, `LoadSequence`, and all `CoroutineManager` calls from `LoadingState`; keep `using Cysharp.Threading.Tasks`, `UnityEngine`, and `ZWebGLFrameWork.Core`.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.FSM.SceneLoadingTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/boot-005-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/boot-005-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-005.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-boot-005.log"
```

Expected: both commands exit `0`; the pre-activation cancellation never flips `AllowSceneActivation`, both loading and activation consume the same timeout budget, the stuck operation returns Timeout, and the missing scene never returns success.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/FSM/SceneLoadingTests.cs Assets/Scripts/Core/Scene/SceneLoadResult.cs Assets/Scripts/Core/Scene/SceneLoadRunner.cs Assets/Scripts/Core/Scene/SceneLoader.cs Assets/Scripts/FSM/Game/States/LoadingState.cs
git commit -m "fix: make scene loading failure explicit"
```

---

### Task 10: DATA-001 Make SaveManager First-Write-Safe and Preserve the Old File on Failure

**Files:**
- Create: `Assets/Tests/Runtime/Data/AtomicFileWriterTests.cs`
- Create: `Assets/Scripts/Data/AtomicFileWriter.cs`
- Modify: `Assets/Scripts/Data/SaveManager.cs:51-74`

**Interfaces:**
- Consumes: UTF-8 text and a destination path.
- Produces: `IAtomicTextWriter.Write(string, string)`, `AtomicFileWriter`, and `FileWriteResult` shared later by LocalStore.

- [ ] **Step 1: Write failing first-write and replace-failure tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AtomicFileWriterTests
    {
        private sealed class MemoryFiles : IFileOperations
        {
            public readonly Dictionary<string, string> Files = new();
            public bool ThrowOnReplace { get; set; }
            public bool Exists(string path) => Files.ContainsKey(path);
            public void WriteAllText(string path, string content) => Files[path] = content;
            public void Move(string source, string destination)
            {
                Files[destination] = Files[source];
                Files.Remove(source);
            }
            public void Replace(string source, string destination)
            {
                if (ThrowOnReplace) throw new System.IO.IOException("replace failed");
                Move(source, destination);
            }
            public void Delete(string path) => Files.Remove(path);
        }

        [Test]
        public void SaveManager_FirstWriteAndFailedReplace_AreAtomic()
        {
            int integrationSlot = SaveManager.MaxSaveSlots + 1000;
            string integrationPath = SaveManager.GetFilePath(integrationSlot);
            byte[] previous = File.Exists(integrationPath)
                ? File.ReadAllBytes(integrationPath) : null;
            try
            {
                File.Delete(integrationPath);
                File.Delete(integrationPath + ".tmp");
                Assert.That(SaveManager.Save(
                    new PlayerSaveData { PlayerName = "FirstWrite" }, integrationSlot), Is.True);
                Assert.That(File.Exists(integrationPath), Is.True);
                Assert.That(File.ReadAllText(integrationPath), Does.Contain("FirstWrite"));
                Assert.That(File.Exists(integrationPath + ".tmp"), Is.False);
            }
            finally
            {
                File.Delete(integrationPath);
                File.Delete(integrationPath + ".tmp");
                if (previous != null) File.WriteAllBytes(integrationPath, previous);
            }

            var firstWriteFiles = new MemoryFiles();
            var firstWrite = new AtomicFileWriter(firstWriteFiles);
            FileWriteResult created = firstWrite.Write("save.json", "new");
            Assert.That(created.Succeeded, Is.True);
            Assert.That(firstWriteFiles.Files["save.json"], Is.EqualTo("new"));
            Assert.That(firstWriteFiles.Files.ContainsKey("save.json.tmp"), Is.False);

            var replaceFiles = new MemoryFiles { ThrowOnReplace = true };
            replaceFiles.Files["save.json"] = "old";
            var replacement = new AtomicFileWriter(replaceFiles);
            FileWriteResult failed = replacement.Write("save.json", "new");
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(replaceFiles.Files["save.json"], Is.EqualTo("old"));
            Assert.That(replaceFiles.Files.ContainsKey("save.json.tmp"), Is.False);
        }
    }
}
```

- [ ] **Step 2: Run focused tests and verify the shared writer is absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.AtomicFileWriterTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-001-red.log"
```

Expected: nonzero exit because the named test exercises the current `SaveManager.Save` first-write path, which calls `File.Replace` without an existing destination; `IFileOperations`, `AtomicFileWriter`, and `FileWriteResult` are also absent.

- [ ] **Step 3: Implement the atomic writer and route SaveManager through it**

Create `AtomicFileWriter.cs`:

```csharp
using System;
using System.IO;

namespace ZWebGLFrameWork.Data
{
    public readonly struct FileWriteResult
    {
        public bool Succeeded { get; }
        public string Error { get; }
        public FileWriteResult(bool succeeded, string error = null)
        {
            Succeeded = succeeded;
            Error = error;
        }
    }

    public interface IAtomicTextWriter
    {
        FileWriteResult Write(string path, string content);
    }

    public interface IFileOperations
    {
        bool Exists(string path);
        void WriteAllText(string path, string content);
        void Move(string source, string destination);
        void Replace(string source, string destination);
        void Delete(string path);
    }

    public sealed class SystemFileOperations : IFileOperations
    {
        public bool Exists(string path) => File.Exists(path);
        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination) => File.Replace(source, destination, null);
        public void Delete(string path) => File.Delete(path);
    }

    public sealed class AtomicFileWriter : IAtomicTextWriter
    {
        private readonly IFileOperations _files;
        public AtomicFileWriter() : this(new SystemFileOperations()) { }
        public AtomicFileWriter(IFileOperations files) =>
            _files = files ?? throw new ArgumentNullException(nameof(files));

        public FileWriteResult Write(string path, string content)
        {
            string temporaryPath = path + ".tmp";
            try
            {
                _files.WriteAllText(temporaryPath, content);
                if (_files.Exists(path))
                    _files.Replace(temporaryPath, path);
                else
                    _files.Move(temporaryPath, path);
                return new FileWriteResult(true);
            }
            catch (Exception exception)
            {
                try { _files.Delete(temporaryPath); } catch { }
                return new FileWriteResult(false, exception.Message);
            }
        }
    }
}
```

Add one static writer to `SaveManager` and replace its write block:

```csharp
private static readonly IAtomicTextWriter SaveWriter = new AtomicFileWriter();

public static bool Save<T>(T data, int? slot = null)
{
    int targetSlot = slot ?? CurrentSlot;
    string json = JsonUtility.ToJson(data, true);
    FileWriteResult result = SaveWriter.Write(GetFilePath(targetSlot), json);
    if (result.Succeeded) return true;
    Debug.LogError($"[SaveManager] 保存失败: {result.Error}");
    return false;
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.AtomicFileWriterTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-001.log"
```

Expected: both commands exit `0`; `SaveManager.Save` succeeds against a real unused slot on first write, and the injected failed replacement leaves the old destination byte-for-byte intact.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/AtomicFileWriterTests.cs Assets/Scripts/Data/AtomicFileWriter.cs Assets/Scripts/Data/SaveManager.cs
git commit -m "fix: make save writes first-write safe"
```

---

### Task 11: DATA-002 Round-Trip Complete Building State

**Files:**
- Create: `Assets/Tests/Runtime/Data/BuildingSaveRoundTripTests.cs`
- Create: `Assets/Scripts/Data/SaveDataMapper.cs`
- Modify: `Assets/Scripts/Data/PlayerSaveData.cs:6-38`
- Modify: `Assets/Scripts/Data/Model/BuildingData.cs:43-53`
- Modify: `Assets/Scripts/Data/SaveManager.cs:76-118,174-210`
- Modify: `Assets/Scripts/Data/GameInitializer.cs:41-68`

**Interfaces:**
- Consumes: `PlayerData`, `VillageData`, and `BuildingData`.
- Produces: `SaveDataMapper.Capture(PlayerData, VillageData)` and `RestoreBuilding(BuildingSaveData)` preserving ID, level, state, position, and upgrade end time.

- [ ] **Step 1: Write the failing complete-building round-trip test**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class BuildingSaveRoundTripTests
    {
        [Test]
        public void SaveGame_BuildingRoundTrip_PreservesCompleteState()
        {
            var player = new PlayerData();
            var village = new VillageData();
            var building = BuildingData.Create(10, 7, 9);
            building.Id = "building-42";
            building.Level.Value = 6;
            building.State.Value = BuildingState.Upgrading;
            building.UpgradeEndTime.Value = 1924992000;
            Assert.That(village.AddBuilding(building), Is.True);

            PlayerSaveData save = SaveDataMapper.Capture(player, village);
            BuildingData restored = SaveDataMapper.RestoreBuilding(save.Buildings[0]);

            Assert.That(save.Version, Is.EqualTo(PlayerSaveData.CurrentVersion));
            Assert.That(save.VillageName, Is.EqualTo(village.Name.Value));
            Assert.That(save.MaxBuildings, Is.EqualTo(village.MaxBuildings.Value));
            Assert.That(restored.Id, Is.EqualTo("building-42"));
            Assert.That(restored.TypeId, Is.EqualTo(10));
            Assert.That(restored.GridX.Value, Is.EqualTo(7));
            Assert.That(restored.GridY.Value, Is.EqualTo(9));
            Assert.That(restored.Level.Value, Is.EqualTo(6));
            Assert.That(restored.State.Value, Is.EqualTo(BuildingState.Upgrading));
            Assert.That(restored.UpgradeEndTime.Value, Is.EqualTo(1924992000));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify missing DTO fields**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.BuildingSaveRoundTripTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-002-red.log"
```

Expected: nonzero exit because `SaveDataMapper` and the identity/state/timing DTO fields do not exist.

- [ ] **Step 3: Add the complete DTO mapper and use it from SaveManager**

Extend `BuildingSaveData`:

```csharp
public string Id;
public int TypeId;
public int GridX;
public int GridY;
public int Level;
public BuildingState State;
public long UpgradeEndTime;
```

Set the new DTO schema and village fields in `PlayerSaveData`:

```csharp
public const int CurrentVersion = 2;
public int Version = CurrentVersion;
public string VillageName = "My Village";
public int MaxBuildings = 50;
```

Remove `SaveManager`'s private schema literal and use `PlayerSaveData.CurrentVersion` wherever the current version is required.

Create `SaveDataMapper`:

```csharp
using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.Data
{
    public static class SaveDataMapper
    {
        public static PlayerSaveData Capture(PlayerData player, VillageData village)
        {
            var result = new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = player.Name.Value,
                PlayerLevel = player.Level.Value,
                TownHallLevel = player.TownHallLevel.Value,
                Gold = player.Gold.Value,
                Elixir = player.Elixir.Value,
                DarkElixir = player.DarkElixir.Value,
                Gems = player.Gems.Value,
                Trophies = player.Trophies.Value,
                Exp = player.Exp.Value,
                VillageName = village.Name.Value,
                MaxBuildings = village.MaxBuildings.Value,
                Buildings = new List<BuildingSaveData>(),
                SaveTime = DateTime.UtcNow.ToString("O"),
            };
            foreach (BuildingData building in village.Buildings)
            {
                result.Buildings.Add(new BuildingSaveData
                {
                    Id = building.Id,
                    TypeId = building.TypeId,
                    GridX = building.GridX.Value,
                    GridY = building.GridY.Value,
                    Level = building.Level.Value,
                    State = building.State.Value,
                    UpgradeEndTime = building.UpgradeEndTime.Value,
                });
            }
            return result;
        }

        public static BuildingData RestoreBuilding(BuildingSaveData saved)
        {
            return new BuildingData
            {
                Id = saved.Id,
                TypeId = saved.TypeId,
                GridX = { Value = saved.GridX },
                GridY = { Value = saved.GridY },
                Level = { Value = saved.Level },
                State = { Value = saved.State },
                UpgradeEndTime = { Value = saved.UpgradeEndTime },
            };
        }
    }
}
```

Add a single default DTO factory to `GameInitializer` and have the existing `CreateDefaultSave` save its result. Every building ID is stable and unique; no current-schema default relies on a missing-field default:

```csharp
public static PlayerSaveData CreateDefaultSaveData()
{
    return new PlayerSaveData
    {
        Version = PlayerSaveData.CurrentVersion,
        PlayerName = "Villager",
        PlayerLevel = 1,
        TownHallLevel = 1,
        Gold = 500,
        Elixir = 300,
        DarkElixir = 0,
        Gems = 50,
        Trophies = 0,
        Exp = 0,
        VillageName = "My Village",
        MaxBuildings = 50,
        Buildings = new List<BuildingSaveData>
        {
            new BuildingSaveData { Id = "default-town-hall", TypeId = 1, GridX = 20, GridY = 22, Level = 1, State = BuildingState.Normal, UpgradeEndTime = 0 },
            new BuildingSaveData { Id = "default-gold-mine", TypeId = 2, GridX = 16, GridY = 26, Level = 1, State = BuildingState.Normal, UpgradeEndTime = 0 },
            new BuildingSaveData { Id = "default-elixir-collector", TypeId = 3, GridX = 24, GridY = 26, Level = 1, State = BuildingState.Normal, UpgradeEndTime = 0 },
            new BuildingSaveData { Id = "default-cannon", TypeId = 5, GridX = 16, GridY = 18, Level = 1, State = BuildingState.Normal, UpgradeEndTime = 0 },
        },
        SaveTime = System.DateTime.UtcNow.ToString("O"),
    };
}
```

Replace SaveManager's hand-built DTO with `SaveDataMapper.Capture(player, village)` and replace `BuildingData.Create(b.TypeId, b.GridX, b.GridY)` during restore with `SaveDataMapper.RestoreBuilding(b)`.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.BuildingSaveRoundTripTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-002.log"
```

Expected: both commands exit `0`; every asserted building field survives mapping.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/BuildingSaveRoundTripTests.cs Assets/Scripts/Data/SaveDataMapper.cs Assets/Scripts/Data/PlayerSaveData.cs Assets/Scripts/Data/Model/BuildingData.cs Assets/Scripts/Data/SaveManager.cs Assets/Scripts/Data/GameInitializer.cs
git commit -m "fix: preserve complete building save state"
```

---

### Task 12: DATA-003 Save Every Persistent Change Without Advancing a Failed Baseline

**Files:**
- Create: `Assets/Tests/Runtime/Data/AutosaveCoordinatorTests.cs`
- Create: `Assets/Scripts/Data/AutosaveCoordinator.cs`
- Modify: `Assets/Scripts/FSM/Game/States/MainVillageState.cs:17-80`

**Interfaces:**
- Consumes: `Func<PlayerSaveData>` and `Func<bool>`.
- Produces: `AutosaveCoordinator.InitializeBaseline()` and `TrySaveIfChanged()`; the baseline changes only after a successful save.

- [ ] **Step 1: Write failing coverage, failure-baseline, and exit-equivalent tests**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AutosaveCoordinatorTests
    {
        [Test]
        public void MainVillageAutosave_AllPersistentChangesTrigger()
        {
            var data = new PlayerSaveData
            {
                Gold = 1,
                Elixir = 2,
                DarkElixir = 3,
                Gems = 4,
                Trophies = 5,
                Buildings = new System.Collections.Generic.List<BuildingSaveData>
                {
                    new BuildingSaveData { Id = "b1", TypeId = 1, Level = 1 }
                }
            };
            int saves = 0;
            var coordinator = new AutosaveCoordinator(() => data, () => { saves++; return true; });
            coordinator.InitializeBaseline();

            data.Gold++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Elixir++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.DarkElixir++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Gems++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Trophies++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Buildings.Add(new BuildingSaveData { Id = "b2", TypeId = 2, Level = 1 });
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Buildings[0].UpgradeEndTime = 1234;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            Assert.That(saves, Is.EqualTo(7));
        }

        [Test]
        public void MainVillageAutosave_FailureDoesNotAdvanceBaseline()
        {
            var data = new PlayerSaveData { Gold = 1 };
            int attempts = 0;
            var coordinator = new AutosaveCoordinator(
                () => data,
                () => { attempts++; return attempts > 1; });
            coordinator.InitializeBaseline();
            data = new PlayerSaveData { Gold = 2 };
            Assert.That(coordinator.TrySaveIfChanged(), Is.False);
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void MainVillageExit_SavesPendingChanges()
        {
            var data = new PlayerSaveData { Gold = 3 };
            int saves = 0;
            var coordinator = new AutosaveCoordinator(() => data, () => { saves++; return true; });
            coordinator.InitializeBaseline();
            data.Gold = 4;

            Assert.That(coordinator.FlushPending(), Is.True);
            Assert.That(saves, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run focused tests and verify the coordinator is absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.AutosaveCoordinatorTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-003-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-003-red.log"
```

Expected: nonzero exit because `AutosaveCoordinator` does not exist.

- [ ] **Step 3: Implement fingerprint-based autosave and use it on interval and exit**

Create `AutosaveCoordinator.cs`:

```csharp
using System;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public sealed class AutosaveCoordinator
    {
        private readonly Func<PlayerSaveData> _capture;
        private readonly Func<bool> _save;
        private string _savedFingerprint;

        public AutosaveCoordinator(Func<PlayerSaveData> capture, Func<bool> save)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public void InitializeBaseline() =>
            _savedFingerprint = Fingerprint(_capture());

        public bool TrySaveIfChanged()
        {
            string current = Fingerprint(_capture());
            if (current == _savedFingerprint) return false;
            if (!_save()) return false;
            _savedFingerprint = current;
            return true;
        }

        public bool FlushPending() => TrySaveIfChanged();

        private static string Fingerprint(PlayerSaveData data)
        {
            string saveTime = data.SaveTime;
            try
            {
                data.SaveTime = string.Empty;
                return JsonUtility.ToJson(data, false);
            }
            finally
            {
                data.SaveTime = saveTime;
            }
        }
    }
}
```

Replace `_lastGold` and `_lastElixir` with `private AutosaveCoordinator _autosave;`. Add this exact initialization at the end of `MainVillageState.OnEnter`:

```csharp
_autoSaveTimer = 0f;
_autosave = new AutosaveCoordinator(
    () => SaveDataMapper.Capture(DataManager.Instance.Player, DataManager.Instance.Village),
    () => SaveManager.SaveGame());
_autosave.InitializeBaseline();
```

Replace the autosave block in `OnUpdate` with:

```csharp
_autoSaveTimer += Time.deltaTime;
if (_autoSaveTimer >= 30f)
{
    _autoSaveTimer = 0f;
    _autosave?.TrySaveIfChanged();
}
```

Replace `OnExit` with:

```csharp
public override void OnExit()
{
    _autosave?.FlushPending();
    _autosave = null;
    EventBus.Unsubscribe("MainVillage_Attack", OnAttackClicked);
    _resourceProducer?.Stop();
    _resourceProducer = null;
    UIManager.Instance.Close<MainVillageHUD>();
    base.OnExit();
    Debug.Log("[GameFSM] Exit MainVillage");
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.AutosaveCoordinatorTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-003-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-003-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-003.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-003.log"
```

Expected: both commands exit `0`; every persistent field category saves, a failure is retried without advancing the baseline, and exit flushes a pending change.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/AutosaveCoordinatorTests.cs Assets/Scripts/Data/AutosaveCoordinator.cs Assets/Scripts/FSM/Game/States/MainVillageState.cs
git commit -m "fix: track complete autosave state"
```

---

### Task 13: DATA-004 Enter Offline Village Only After Create and Load Succeed

**Files:**
- Create: `Assets/Tests/Runtime/Data/OfflineSaveFlowTests.cs`
- Create: `Assets/Scripts/Data/OfflineSaveFlow.cs`
- Modify: `Assets/Scripts/Data/GameInitializer.cs:23-68`
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs:104-125`
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:171-182`
- Modify: `Assets/Scripts/UI/Panels/CharacterSelectPanel.cs:74-85`

**Interfaces:**
- Consumes: `IOfflineSaveGateway.Exists/Create/Load`.
- Produces: `OfflineSaveFlow.TryPrepareSlot(int)` and boolean `GameInitializer.CreateDefaultSave(int)`.

- [ ] **Step 1: Write separate failing create- and load-failure tests**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class OfflineSaveFlowTests
    {
        private sealed class FakeGateway : IOfflineSaveGateway
        {
            public bool ExistsResult;
            public bool CreateResult;
            public bool LoadResult;
            public bool Exists(int slot) => ExistsResult;
            public bool Create(int slot) => CreateResult;
            public bool Load(int slot) => LoadResult;
        }

        [Test]
        public void OfflineLogin_DefaultSaveCreationFailure_DoesNotEnterVillage()
        {
            var gateway = new FakeGateway { CreateResult = false, LoadResult = true };
            Assert.That(new OfflineSaveFlow(gateway).TryPrepareSlot(2), Is.False);
        }

        [Test]
        public void OfflineLogin_LoadFailure_DoesNotEnterVillage()
        {
            var gateway = new FakeGateway { ExistsResult = true, LoadResult = false };
            Assert.That(new OfflineSaveFlow(gateway).TryPrepareSlot(2), Is.False);
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify failures are currently ignored**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.OfflineSaveFlowTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-004-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-004-red.log"
```

Expected: nonzero exit because the gateway and flow do not exist.

- [ ] **Step 3: Implement one checked offline entry path and use it from all callers**

Create `OfflineSaveFlow.cs`:

```csharp
using System;

namespace ZWebGLFrameWork.Data
{
    public interface IOfflineSaveGateway
    {
        bool Exists(int slot);
        bool Create(int slot);
        bool Load(int slot);
    }

    public sealed class DefaultOfflineSaveGateway : IOfflineSaveGateway
    {
        public bool Exists(int slot) => SaveManager.Exists(slot);
        public bool Create(int slot) => GameInitializer.CreateDefaultSave(slot);
        public bool Load(int slot) => SaveManager.LoadToDataManager(slot);
    }

    public sealed class OfflineSaveFlow
    {
        private readonly IOfflineSaveGateway _gateway;
        public OfflineSaveFlow(IOfflineSaveGateway gateway) =>
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

        public bool TryPrepareSlot(int slot)
        {
            if (!_gateway.Exists(slot) && !_gateway.Create(slot))
                return false;
            return _gateway.Load(slot);
        }
    }
}
```

Change `GameInitializer.CreateDefaultSave` to call the Task 11 `CreateDefaultSaveData()` factory, return `SaveManager.Save(saveData, slot)`, and log success only when true. Do not rebuild a second default DTO in this method. Give LoginState and LaunchState a default `OfflineSaveFlow`; transition to MainVillage only when `TryPrepareSlot` returns true, otherwise publish the existing recoverable boot/login error. CharacterSelectPanel publishes `CharacterSelected` only after create succeeds.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.OfflineSaveFlowTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-004-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-004-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-004.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-004.log"
```

Expected: both commands exit `0`; neither create nor load failure reports a prepared slot.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/OfflineSaveFlowTests.cs Assets/Scripts/Data/OfflineSaveFlow.cs Assets/Scripts/Data/GameInitializer.cs Assets/Scripts/FSM/Game/States/LoginState.cs Assets/Scripts/FSM/Game/States/LaunchState.cs Assets/Scripts/UI/Panels/CharacterSelectPanel.cs
git commit -m "fix: gate offline entry on persistence success"
```

---

### Task 14: DATA-005 Apply, Clamp, and Save Battle Rewards Exactly Once

**Files:**
- Create: `Assets/Tests/Runtime/Data/BattleRewardServiceTests.cs`
- Create: `Assets/Scripts/FSM/Game/BattleRewardService.cs`
- Modify: `Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef`
- Modify: `Assets/Scripts/Data/Model/PlayerData.cs`
- Modify: `Assets/Scripts/Data/PlayerSaveData.cs`
- Modify: `Assets/Scripts/Data/SaveDataMapper.cs`
- Modify: `Assets/Scripts/Data/SaveManager.cs`
- Modify: `Assets/Scripts/Data/DataManager.cs`
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`

**Interfaces:**
- Cross-plan prerequisite: execute Simulation migration Task 15 first so `BattleState`, `CombatManager.StartBattleAsync(BattleParams, CancellationToken)`, `BattleParams.MatchId`, `ResultPanel`, and `ZWebGLFrameWork.SimulationCore.SimBattleResult` are canonical and Legacy result events are gone.
- Consumes: only canonical `SimBattleResult` reward identity/finality/loot fields; trust is an explicit enum, never a boolean.
- Produces: `BattleRewardService.Commit(string, SimBattleResult, BattleResultTrust, PlayerData, Func<bool>, Action<SimBattleResult>)`, persisted `PlayerData.AppliedBattleResultIds`, transactional rollback/retry, and result presentation after either a successful first save or recognition of an already-persisted duplicate.

- [ ] **Step 1: Write the four exact canonical-result acceptance tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class BattleRewardServiceTests
    {
        private static SimBattleResult Result(string matchId = "match-1", int gold = 10,
            int elixir = 5, string resultId = null, int winnerTeamId = SimTeam.Attacker,
            int stars = 3)
        {
            const int finalTick = 120;
            const ulong checksum = 0xA11CEUL;
            return new SimBattleResult(matchId,
                resultId ?? $"{matchId}:{finalTick}:{checksum:X16}", winnerTeamId, stars,
                gold, elixir, finalTick, checksum);
        }

        [Test]
        public void BattleResult_ValidCompletion_AppliesAndSavesRewardsOnce()
        {
            var player = new PlayerData();
            int initialGold = player.Gold.Value;
            int initialElixir = player.Elixir.Value;
            int saves = 0;
            int presentations = 0;
            var service = new BattleRewardService();

            BattleRewardCommitStatus status = service.Commit("match-1", Result(),
                BattleResultTrust.Accepted, player, () => { saves++; return true; },
                _ => presentations++);

            Assert.That(status, Is.EqualTo(BattleRewardCommitStatus.Applied));
            Assert.That(player.Gold.Value, Is.EqualTo(initialGold + 10));
            Assert.That(player.Elixir.Value, Is.EqualTo(initialElixir + 5));
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(presentations, Is.EqualTo(1));
            Assert.That(player.AppliedBattleResultIds, Has.Member(Result().ResultId));
        }

        [Test]
        public void BattleResult_DuplicateInvalidOrRejected_DoesNotReward()
        {
            var player = new PlayerData();
            int saves = 0;
            var service = new BattleRewardService();
            SimBattleResult accepted = Result();
            Assert.That(service.Commit("match-1", accepted, BattleResultTrust.Accepted,
                player, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Applied));

            PlayerSaveData persisted = SaveDataMapper.Capture(player, new VillageData());
            var restarted = new PlayerData();
            SaveDataMapper.RestoreRewardReceipts(persisted, restarted);
            var restartedService = new BattleRewardService();
            int originalGold = restarted.Gold.Value;
            int duplicatePresentations = 0;

            Assert.That(restartedService.Commit("match-1", accepted, BattleResultTrust.Accepted,
                restarted, () => { saves++; return true; }, _ => duplicatePresentations++),
                Is.EqualTo(BattleRewardCommitStatus.Duplicate));
            Assert.That(duplicatePresentations, Is.EqualTo(1),
                "an already-persisted result must reopen ResultPanel without saving rewards again");
            Assert.That(restartedService.Commit("match-1", Result(resultId: "forged"),
                BattleResultTrust.Accepted, restarted, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Invalid));
            Assert.That(restartedService.Commit("match-1", Result(winnerTeamId: 99, stars: 4),
                BattleResultTrust.Accepted, restarted, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Invalid));
            Assert.That(restartedService.Commit("match-1", Result("foreign"),
                BattleResultTrust.Accepted, restarted, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.ForeignMatch));
            Assert.That(restartedService.Commit("match-1", Result(), BattleResultTrust.Rejected,
                restarted, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Rejected));
            Assert.That(restartedService.Commit("match-1", Result(), BattleResultTrust.ReplayInvalid,
                restarted, () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.ReplayInvalid));
            Assert.That(restarted.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(saves, Is.EqualTo(1));
        }

        [Test]
        public void BattleResult_NearCap_Clamps()
        {
            var player = new PlayerData();
            player.Gold.Value = int.MaxValue - 3;
            player.Elixir.Value = int.MaxValue - 2;
            Assert.That(new BattleRewardService().Commit("match-1", Result(gold: 100, elixir: 100),
                BattleResultTrust.Accepted, player, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Applied));
            Assert.That(player.Gold.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.Elixir.Value, Is.EqualTo(int.MaxValue - 1));
        }

        [Test]
        public void BattleResult_ReturnWaitsForSave()
        {
            var player = new PlayerData();
            int initial = player.Gold.Value;
            var service = new BattleRewardService();
            var order = new List<string>();
            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => { order.Add("save-failed"); return false; }, _ => order.Add("ui")),
                Is.EqualTo(BattleRewardCommitStatus.SaveFailed));
            CollectionAssert.AreEqual(new[] { "save-failed" }, order);
            Assert.That(player.Gold.Value, Is.EqualTo(initial));
            Assert.That(player.AppliedBattleResultIds, Is.Empty);

            order.Clear();
            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => { order.Add("save"); return true; }, _ => order.Add("ui")),
                Is.EqualTo(BattleRewardCommitStatus.Applied));
            CollectionAssert.AreEqual(new[] { "save", "ui" }, order);
        }
    }
}
```

- [ ] **Step 2: Run the focused tests and verify no reward service exists**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.BattleRewardServiceTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-005-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-005-red.log"
```

Expected: nonzero exit because the canonical-result commit service, persisted receipt model, and explicit trust/status contracts do not exist.

- [ ] **Step 3: Persist result receipts in the save model**

Simulation Task 15 has already added the production FSM reference. Add `"ZWebGLFrameWork.SimulationCore"` only to the runtime test asmdef, then add `using System.Collections.Generic;` and this field to `PlayerData`:

```csharp
public readonly List<string> AppliedBattleResultIds = new List<string>();
```

Add this field to `PlayerSaveData`:

```csharp
public List<string> AppliedBattleResultIds = new List<string>();
```

Add this initializer member in `SaveDataMapper.Capture`:

```csharp
AppliedBattleResultIds = new List<string>(player.AppliedBattleResultIds),
```

Add this method to `SaveDataMapper`:

```csharp
public static void RestoreRewardReceipts(PlayerSaveData saved, PlayerData player)
{
    if (saved == null) throw new ArgumentNullException(nameof(saved));
    if (player == null) throw new ArgumentNullException(nameof(player));
    player.AppliedBattleResultIds.Clear();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (string resultId in saved.AppliedBattleResultIds ?? new List<string>())
    {
        if (!string.IsNullOrWhiteSpace(resultId) && seen.Add(resultId))
            player.AppliedBattleResultIds.Add(resultId);
    }
}
```

Call `SaveDataMapper.RestoreRewardReceipts(data, player);` immediately after scalar player restore in `SaveManager.LoadToDataManager`. Add `Player.AppliedBattleResultIds.Clear();` immediately after `Player.Reset();` in `DataManager.ResetAll`.

- [ ] **Step 4: Implement the explicit transactional commit service**

Create `BattleRewardService.cs`:

```csharp
using System;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.FSM
{
    public enum BattleResultTrust
    {
        Accepted,
        Rejected,
        ReplayInvalid,
    }

    public enum BattleRewardCommitStatus
    {
        Applied,
        Duplicate,
        Invalid,
        ForeignMatch,
        Rejected,
        ReplayInvalid,
        SaveFailed,
    }

    public sealed class BattleRewardService
    {
        private const int ResourceCap = int.MaxValue - 1;

        public BattleRewardCommitStatus Commit(
            string expectedMatchId,
            SimBattleResult result,
            BattleResultTrust trust,
            PlayerData player,
            Func<bool> save,
            Action<SimBattleResult> present)
        {
            if (trust == BattleResultTrust.Rejected)
                return BattleRewardCommitStatus.Rejected;
            if (trust == BattleResultTrust.ReplayInvalid)
                return BattleRewardCommitStatus.ReplayInvalid;
            if (!IsStructurallyValid(result))
                return BattleRewardCommitStatus.Invalid;
            if (!string.Equals(expectedMatchId, result.MatchId, StringComparison.Ordinal))
                return BattleRewardCommitStatus.ForeignMatch;
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (player.AppliedBattleResultIds.Contains(result.ResultId))
            {
                present?.Invoke(result);
                return BattleRewardCommitStatus.Duplicate;
            }

            int oldGold = player.Gold.Value;
            int oldElixir = player.Elixir.Value;
            player.Gold.Value = SaturatingAdd(oldGold, result.LootGold);
            player.Elixir.Value = SaturatingAdd(oldElixir, result.LootElixir);
            player.AppliedBattleResultIds.Add(result.ResultId);
            bool saved;
            try { saved = save(); }
            catch { saved = false; }
            if (!saved)
            {
                player.Gold.Value = oldGold;
                player.Elixir.Value = oldElixir;
                player.AppliedBattleResultIds.Remove(result.ResultId);
                return BattleRewardCommitStatus.SaveFailed;
            }

            present?.Invoke(result);
            return BattleRewardCommitStatus.Applied;
        }

        private static bool IsStructurallyValid(SimBattleResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.MatchId) ||
                string.IsNullOrWhiteSpace(result.ResultId) || result.FinalTick < 0 ||
                result.FinalChecksum == 0UL || result.LootGold < 0 || result.LootElixir < 0 ||
                result.Stars < 0 || result.Stars > 3 ||
                (result.WinnerTeamId != SimTeam.None &&
                 result.WinnerTeamId != SimTeam.Attacker &&
                 result.WinnerTeamId != SimTeam.Defender))
                return false;
            string expected = $"{result.MatchId}:{result.FinalTick}:{result.FinalChecksum:X16}";
            return string.Equals(result.ResultId, expected, StringComparison.Ordinal);
        }

        private static int SaturatingAdd(int current, int delta)
        {
            long sum = (long)current + delta;
            return (int)Math.Min(ResourceCap, sum);
        }
    }
}
```

- [ ] **Step 5: Commit and present the canonical session result in BattleState**

Remove every legacy combat-result event subscription/handler and any generated battle ID. Add `using System;`, `using ZWebGLFrameWork.SimulationCore;`, and these fields to the Simulation-migrated `BattleState`:

```csharp
private readonly BattleRewardService _rewardService = new BattleRewardService();
private string _expectedMatchId;
```

After validating `BattleParams parameters` in `OnEnter`, set `_expectedMatchId = parameters.MatchId;` and call `RunBattleAsync(parameters).Forget();`. Add:

```csharp
private async UniTaskVoid RunBattleAsync(BattleParams parameters)
{
    try
    {
        SimBattleResult result = await CombatManager.Instance.StartBattleAsync(
            parameters, LifetimeToken);
        LifetimeToken.ThrowIfCancellationRequested();
        BattleRewardCommitStatus status = _rewardService.Commit(
            _expectedMatchId, result, BattleResultTrust.Accepted,
            DataManager.Instance.Player, () => SaveManager.SaveGame(),
            committed => UIManager.Instance.Open<ResultPanel>(committed));
        if (status != BattleRewardCommitStatus.Applied &&
            status != BattleRewardCommitStatus.Duplicate)
        {
            EventBus.Publish("BattleRewardRejected", status);
            Debug.LogError($"[BattleState] Result commit rejected: {status}");
        }
    }
    catch (OperationCanceledException)
    {
    }
}
```

For a new result, the presentation callback runs only after `SaveManager.SaveGame()` returns true, so failed persistence neither opens `ResultPanel` nor enables its return command. For a valid duplicate already present in persisted receipts, the callback reopens `ResultPanel` without changing resources, appending a receipt, or calling save. Rollback removes a newly-added receipt, so the same canonical `ResultId` can be retried after a failed save.

- [ ] **Step 6: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.BattleRewardServiceTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-005-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-005-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-005.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-005.log"
```

Expected: both commands exit `0`; valid canonical results persist once, duplicate/invalid/foreign/rejected/replay-invalid results never reward, near-cap values clamp, new-result UI follows successful persistence, and a persisted duplicate reopens ResultPanel without another save.

- [ ] **Step 7: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/BattleRewardServiceTests.cs Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef Assets/Scripts/FSM/Game/BattleRewardService.cs Assets/Scripts/FSM/Game/States/BattleState.cs Assets/Scripts/Data/Model/PlayerData.cs Assets/Scripts/Data/PlayerSaveData.cs Assets/Scripts/Data/SaveDataMapper.cs Assets/Scripts/Data/SaveManager.cs Assets/Scripts/Data/DataManager.cs
git commit -m "fix: apply battle rewards transactionally once"
```

---

### Task 15: DATA-006 Reject Unknown or Invalid Saves Before Mutating Runtime State

**Files:**
- Create: `Assets/Tests/Runtime/Data/SaveValidationTests.cs`
- Modify: `Assets/Scripts/Data/SaveDataMapper.cs`
- Modify: `Assets/Scripts/Data/PlayerSaveData.cs`
- Modify: `Assets/Scripts/Data/Model/VillageData.cs:22-45`
- Modify: `Assets/Scripts/Data/DataManager.cs`
- Modify: `Assets/Scripts/Data/SaveManager.cs:147-210`

**Interfaces:**
- Consumes: `PlayerSaveData`, expected schema version, persisted reward receipts from Task 14, exact `BuildingConfig` rows, a supplied Unix time, and explicit `44x44` grid bounds.
- Produces: detached `SaveDataMapper.TryMigrateToCurrent(PlayerSaveData, int, out PlayerSaveData, out string)`, `TryBuildValidatedState(PlayerSaveData, int, Func<int, BuildingConfig>, int, int, long, out ValidatedSaveState, out string)`, and one `DataManager.ReplaceValidatedState` model-swap notification after complete validation.

- [ ] **Step 1: Write failing unknown-version and invalid-building transaction tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class SaveValidationTests : RuntimeTestBase
    {
        [Test]
        public void SaveGame_UnknownOrInvalidSchema_LeavesRuntimeStateUnchanged()
        {
            var player = new PlayerData();
            var village = new VillageData();
            village.Name.Value = "Original Village";
            village.MaxBuildings.Value = 40;
            village.AddBuilding(BuildingData.Create(1, 1, 1));
            int originalGold = player.Gold.Value;
            var save = new PlayerSaveData
            {
                Version = 99,
                PlayerName = "Mutated",
                PlayerLevel = 2,
                TownHallLevel = 2,
                Gold = 1,
                VillageName = "Mutated Village",
                MaxBuildings = 1,
            };
            bool migrated = SaveDataMapper.TryMigrateToCurrent(
                save, 0, out _, out string error);
            Assert.That(migrated, Is.False);
            Assert.That(error, Does.Contain("version"));
            Assert.That(player.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(village.Name.Value, Is.EqualTo("Original Village"));
            Assert.That(village.MaxBuildings.Value, Is.EqualTo(40));
            Assert.That(village.Buildings, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidBuilding_LeavesPlayerAndVillageUnchanged()
        {
            var player = new PlayerData();
            var village = new VillageData();
            village.AddBuilding(BuildingData.Create(1, 1, 1));
            int originalGold = player.Gold.Value;
            var save = new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = "Valid",
                PlayerLevel = 1,
                TownHallLevel = 1,
                Gold = 1,
                VillageName = "Valid Village",
                MaxBuildings = 40,
                Buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData
                    {
                        Id = "bad",
                        TypeId = 999,
                        Level = 1,
                        State = BuildingState.Upgrading,
                        UpgradeEndTime = 0,
                    }
                }
            };
            bool applied = SaveDataMapper.TryBuildValidatedState(save,
                PlayerSaveData.CurrentVersion, _ => null, 44, 44, 1_700_000_000,
                out _, out _);
            Assert.That(applied, Is.False);
            Assert.That(player.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(village.Buildings, Has.Count.EqualTo(1));
        }

        [Test]
        public void MalformedRowsRangesTimestampsAndFootprints_AreRejectedBeforeModelSwap()
        {
            const long now = 1_700_000_000;
            BuildingConfig Resolve(int id) => id == 1 ? new BuildingConfig
            {
                Id = 1, MaxLevel = 5, SizeX = 2, SizeY = 2,
            } : null;
            BuildingSaveData Building(string id, int x, int y, int level = 1,
                BuildingState state = BuildingState.Normal, long upgradeEnd = 0) =>
                new BuildingSaveData
                {
                    Id = id, TypeId = 1, GridX = x, GridY = y, Level = level,
                    State = state, UpgradeEndTime = upgradeEnd,
                };
            PlayerSaveData Valid(params BuildingSaveData[] rows) => new PlayerSaveData
            {
                Version = PlayerSaveData.CurrentVersion,
                PlayerName = "Valid",
                PlayerLevel = 1,
                TownHallLevel = 1,
                Gold = 10,
                Elixir = 10,
                DarkElixir = 0,
                Gems = 1,
                Trophies = 0,
                Exp = 0,
                VillageName = "Village",
                MaxBuildings = 40,
                SaveTime = System.DateTimeOffset.FromUnixTimeSeconds(now).ToString("O"),
                Buildings = rows.ToList(),
            };

            PlayerSaveData nullRow = Valid(Building("a", 0, 0));
            nullRow.Buildings.Add(null);
            PlayerSaveData highResource = Valid(Building("a", 0, 0));
            highResource.Gold = int.MaxValue;
            PlayerSaveData highPlayerLevel = Valid(Building("a", 0, 0));
            highPlayerLevel.PlayerLevel = 1001;
            PlayerSaveData highBuildingLevel = Valid(Building("a", 0, 0, level: 6));
            PlayerSaveData outOfBounds = Valid(Building("a", 43, 43));
            PlayerSaveData overlapping = Valid(
                Building("a", 0, 0), Building("b", 1, 1));
            PlayerSaveData badUpgradeTime = Valid(Building("a", 0, 0, state:
                BuildingState.Upgrading, upgradeEnd: now + 31_536_001));
            PlayerSaveData badSaveTime = Valid(Building("a", 0, 0));
            badSaveTime.SaveTime = "2026-99-99";

            foreach (PlayerSaveData malformed in new[]
            {
                nullRow, highResource, highPlayerLevel, highBuildingLevel,
                outOfBounds, overlapping, badUpgradeTime, badSaveTime,
            })
            {
                Assert.That(SaveDataMapper.TryBuildValidatedState(malformed,
                    PlayerSaveData.CurrentVersion, Resolve, 44, 44, now,
                    out _, out _), Is.False);
            }
        }

        [Test]
        public void ValidatedState_ModelSwapPublishesOnlyAfterBothModelsAreVisible()
        {
            var manager = CreateOwnedObject("DataManager").AddComponent<DataManager>();
            var player = new PlayerData();
            player.Name.SetSilent("Atomic Player");
            var village = new VillageData();
            village.Name.SetSilent("Atomic Village");
            int notifications = 0;
            EventBus.Subscribe(DataManager.EventDataReady, () =>
            {
                notifications++;
                Assert.That(manager.Player.Name.Value, Is.EqualTo("Atomic Player"));
                Assert.That(manager.Village.Name.Value, Is.EqualTo("Atomic Village"));
            });

            manager.ReplaceValidatedState(new ValidatedSaveState(player, village));

            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void DefaultV2AndLegacyV1Saves_NormalizeThenValidateDeterministically()
        {
            string defaultJson = UnityEngine.JsonUtility.ToJson(
                GameInitializer.CreateDefaultSaveData());
            PlayerSaveData current = UnityEngine.JsonUtility.FromJson<PlayerSaveData>(defaultJson);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                current, 0, out PlayerSaveData normalizedDefault, out string defaultError),
                Is.True, defaultError);

            BuildingConfig ResolveDefault(int typeId) =>
                typeId == 1 || typeId == 2 || typeId == 3 || typeId == 5
                    ? new BuildingConfig { Id = typeId, MaxLevel = 20, SizeX = 1, SizeY = 1 }
                    : null;
            Assert.That(SaveDataMapper.TryBuildValidatedState(normalizedDefault,
                PlayerSaveData.CurrentVersion, ResolveDefault, 44, 44,
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                out ValidatedSaveState validated, out string applyError), Is.True, applyError);
            Assert.That(validated.Village.Buildings, Has.Count.EqualTo(4));
            Assert.That(validated.Village.Buildings.Select(building => building.Id).Distinct().Count(),
                Is.EqualTo(4));

            const string legacyJson =
                "{\"Version\":1,\"PlayerName\":\"Legacy\",\"PlayerLevel\":1," +
                "\"TownHallLevel\":1,\"Gold\":10,\"Elixir\":20,\"DarkElixir\":0," +
                "\"Gems\":1,\"Trophies\":0,\"Exp\":0," +
                "\"Buildings\":[{\"TypeId\":1,\"GridX\":2,\"GridY\":3,\"Level\":1}]}";
            PlayerSaveData legacy = UnityEngine.JsonUtility.FromJson<PlayerSaveData>(legacyJson);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                legacy, 2, out PlayerSaveData first, out string firstError), Is.True, firstError);
            Assert.That(SaveDataMapper.TryMigrateToCurrent(
                legacy, 2, out PlayerSaveData second, out string secondError), Is.True, secondError);
            Assert.That(first.Buildings[0].Id, Is.EqualTo("legacy-02-0000"));
            Assert.That(second.Buildings[0].Id, Is.EqualTo(first.Buildings[0].Id));
            Assert.That(first.Buildings[0].State, Is.EqualTo(BuildingState.Normal));
            Assert.That(first.Buildings[0].UpgradeEndTime, Is.Zero);
        }
    }
}
```

Before the red run, add these compile-neutral migration seams. They deliberately preserve the unsafe acceptance behavior: every version/row is reported as accepted and model replacement publishes nothing. Step 3 replaces these bodies completely; do not commit the seams separately.

```csharp
// SaveDataMapper.cs, temporary red-phase bodies
public static bool TryMigrateToCurrent(PlayerSaveData source, int slot,
    out PlayerSaveData migrated, out string error)
{
    migrated = source;
    error = null;
    return true;
}

public static bool TryBuildValidatedState(PlayerSaveData source, int expectedVersion,
    System.Func<int, BuildingConfig> resolveConfig, int gridWidth, int gridHeight,
    long nowUnixSeconds, out ValidatedSaveState state, out string error)
{
    state = new ValidatedSaveState(new PlayerData(), new VillageData());
    error = null;
    return true;
}
```

```csharp
// DataManager.cs, temporary red-phase body
public void ReplaceValidatedState(ValidatedSaveState state) { }
```

Use the final top-level `ValidatedSaveState` declaration shown in Step 3 for both phases; only the method bodies above are temporary.

- [ ] **Step 2: Run tests and verify current restore mutates before validation completes**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.SaveValidationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-006-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-006-red.log"
```

Expected: Unity writes non-empty NUnit XML; `SaveGame_UnknownOrInvalidSchema_LeavesRuntimeStateUnchanged` fails because unknown version `99` is accepted, malformed rows are accepted, and the no-op model swap emits no notification.

- [ ] **Step 3: Migrate supported v1 data, then stage and validate every value before applying**

Task 11 already added `VillageName`, `MaxBuildings`, and `CurrentVersion = 2`. Add this single supported migration to `SaveDataMapper`; it creates a detached v2 DTO, assigns deterministic legacy building IDs from slot and row index, and never mutates the parsed v1 object:

```csharp
public static bool TryMigrateToCurrent(
    PlayerSaveData saved,
    int slot,
    out PlayerSaveData current,
    out string error)
{
    current = null;
    error = null;
    if (saved == null) { error = "save is null"; return false; }
    if (saved.Version == PlayerSaveData.CurrentVersion)
    {
        current = saved;
        return true;
    }
    if (saved.Version != 1) { error = "unsupported version"; return false; }

    var buildings = new List<BuildingSaveData>();
    var legacyBuildings = saved.Buildings ?? new List<BuildingSaveData>();
    for (int index = 0; index < legacyBuildings.Count; index++)
    {
        BuildingSaveData building = legacyBuildings[index];
        if (building == null) { error = "legacy building is null"; return false; }
        buildings.Add(new BuildingSaveData
        {
            Id = $"legacy-{slot:D2}-{index:D4}",
            TypeId = building.TypeId,
            GridX = building.GridX,
            GridY = building.GridY,
            Level = building.Level,
            State = BuildingState.Normal,
            UpgradeEndTime = 0,
        });
    }

    current = new PlayerSaveData
    {
        Version = PlayerSaveData.CurrentVersion,
        PlayerName = saved.PlayerName,
        PlayerLevel = saved.PlayerLevel,
        TownHallLevel = saved.TownHallLevel,
        Gold = saved.Gold,
        Elixir = saved.Elixir,
        DarkElixir = saved.DarkElixir,
        Gems = saved.Gems,
        Trophies = saved.Trophies,
        Exp = saved.Exp,
        VillageName = string.IsNullOrWhiteSpace(saved.VillageName)
            ? "My Village" : saved.VillageName,
        MaxBuildings = saved.MaxBuildings > 0 ? saved.MaxBuildings : 50,
        Buildings = buildings,
        AppliedBattleResultIds = new List<string>(),
        SaveTime = saved.SaveTime,
    };
    return true;
}
```

Keep these Task 11 members in `SaveDataMapper.Capture`:

```csharp
VillageName = village.Name.Value,
MaxBuildings = village.MaxBuildings.Value,
```

Add `using System;`, `using System.Collections.Generic;`, and `using System.Globalization;`. Keep `ValidatedSaveState` as a top-level type in `ZWebGLFrameWork.Data`, then replace the temporary red-phase methods and the mutating apply helper with this detached validated-state builder:

```csharp
public sealed class ValidatedSaveState
{
    public PlayerData Player { get; }
    public VillageData Village { get; }

    public ValidatedSaveState(PlayerData player, VillageData village)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Village = village ?? throw new ArgumentNullException(nameof(village));
    }
}

public static bool TryBuildValidatedState(
    PlayerSaveData saved,
    int expectedVersion,
    Func<int, BuildingConfig> resolveBuilding,
    int gridColumns,
    int gridRows,
    long nowUnixSeconds,
    out ValidatedSaveState state,
    out string error)
{
    const int MaxPlayerLevel = 1000;
    const int MaxTownHallLevel = 100;
    const int MaxResource = int.MaxValue - 1;
    const int MaxTrophies = 1_000_000;
    const int MaxBuildings = 500;
    const long MaxUpgradeDurationSeconds = 31_536_000;
    state = null;
    error = null;
    if (saved == null) { error = "save is null"; return false; }
    if (resolveBuilding == null || gridColumns < 1 || gridRows < 1 || nowUnixSeconds < 0)
    { error = "validation dependency is invalid"; return false; }
    if (saved.Version != expectedVersion) { error = "unsupported version"; return false; }
    if (string.IsNullOrWhiteSpace(saved.PlayerName) || saved.PlayerName.Length > 64 ||
        saved.PlayerLevel < 1 || saved.PlayerLevel > MaxPlayerLevel ||
        saved.TownHallLevel < 1 || saved.TownHallLevel > MaxTownHallLevel ||
        saved.Gold < 0 || saved.Gold > MaxResource ||
        saved.Elixir < 0 || saved.Elixir > MaxResource ||
        saved.DarkElixir < 0 || saved.DarkElixir > MaxResource ||
        saved.Gems < 0 || saved.Gems > MaxResource ||
        saved.Trophies < 0 || saved.Trophies > MaxTrophies ||
        saved.Exp < 0 || saved.Exp > MaxResource)
    { error = "invalid player range"; return false; }
    if (string.IsNullOrWhiteSpace(saved.VillageName) || saved.VillageName.Length > 64 ||
        saved.MaxBuildings < 1 || saved.MaxBuildings > MaxBuildings)
    { error = "invalid village"; return false; }
    if (!DateTimeOffset.TryParseExact(saved.SaveTime, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTimeOffset saveTime) ||
        saveTime.Offset != TimeSpan.Zero || saveTime.ToUnixTimeSeconds() < 0 ||
        saveTime.ToUnixTimeSeconds() > nowUnixSeconds + 300)
    { error = "invalid save time"; return false; }

    var staged = new List<BuildingData>();
    var buildingIds = new HashSet<string>(StringComparer.Ordinal);
    var occupiedCells = new HashSet<long>();
    foreach (BuildingSaveData building in saved.Buildings ?? new List<BuildingSaveData>())
    {
        if (building == null) { error = "building is null"; return false; }
        BuildingConfig config = resolveBuilding(building.TypeId);
        bool validTiming = building.State == BuildingState.Upgrading
            ? building.UpgradeEndTime > 0 &&
                building.UpgradeEndTime <= nowUnixSeconds + MaxUpgradeDurationSeconds
            : building.UpgradeEndTime == 0;
        if (string.IsNullOrWhiteSpace(building.Id) || !buildingIds.Add(building.Id) ||
            building.Id.Length > 128 || config == null || config.SizeX < 1 || config.SizeY < 1 ||
            building.Level < 1 || building.Level > config.MaxLevel ||
            building.GridX < 0 || building.GridY < 0 ||
            building.GridX + config.SizeX > gridColumns ||
            building.GridY + config.SizeY > gridRows ||
            !Enum.IsDefined(typeof(BuildingState), building.State) || !validTiming ||
            config.Id != building.TypeId)
        { error = "invalid building"; return false; }
        for (int x = building.GridX; x < building.GridX + config.SizeX; x++)
        for (int y = building.GridY; y < building.GridY + config.SizeY; y++)
        {
            long cell = ((long)x << 32) | (uint)y;
            if (!occupiedCells.Add(cell)) { error = "overlapping building"; return false; }
        }
        staged.Add(RestoreBuilding(building));
    }
    if (staged.Count > saved.MaxBuildings)
    { error = "building limit exceeded"; return false; }

    var receipts = new List<string>();
    var receiptIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (string resultId in saved.AppliedBattleResultIds ?? new List<string>())
    {
        if (string.IsNullOrWhiteSpace(resultId) || resultId.Length > 256 ||
            !receiptIds.Add(resultId) || receipts.Count >= 10_000)
        { error = "invalid result receipt"; return false; }
        receipts.Add(resultId);
    }

    var player = new PlayerData();
    player.Name.SetSilent(saved.PlayerName);
    player.Level.SetSilent(saved.PlayerLevel);
    player.TownHallLevel.SetSilent(saved.TownHallLevel);
    player.Gold.SetSilent(saved.Gold);
    player.Elixir.SetSilent(saved.Elixir);
    player.DarkElixir.SetSilent(saved.DarkElixir);
    player.Gems.SetSilent(saved.Gems);
    player.Trophies.SetSilent(saved.Trophies);
    player.Exp.SetSilent(saved.Exp);
    player.AppliedBattleResultIds.AddRange(receipts);
    var village = new VillageData();
    village.Name.SetSilent(saved.VillageName);
    village.MaxBuildings.SetSilent(saved.MaxBuildings);
    foreach (BuildingData building in staged)
        village.AddBuilding(building);
    state = new ValidatedSaveState(player, village);
    return true;
}
```

`RestoreBuilding` creates only detached `BuildingData` and uses `SetSilent` for every observable. Add this exact model-swap method to `DataManager`; no externally reachable model changes and no reactive callback fires before both detached models are complete:

```csharp
public void ReplaceValidatedState(ValidatedSaveState state)
{
    if (state == null) throw new System.ArgumentNullException(nameof(state));
    Player = state.Player;
    Village = state.Village;
    EventBus.Publish(EventDataReady);
}
```

Replace `SaveManager.LoadToDataManager` with:

```csharp
public static bool LoadToDataManager(int? slot = null)
{
    int targetSlot = slot ?? CurrentSlot;
    PlayerSaveData parsed = Load<PlayerSaveData>(targetSlot);
    if (parsed == null) return false;
    if (!SaveDataMapper.TryMigrateToCurrent(
        parsed, targetSlot, out PlayerSaveData data, out string migrationError))
    {
        Debug.LogError($"[SaveManager] Rejected slot {targetSlot}: {migrationError}");
        return false;
    }
    DataManager manager = DataManager.Instance;
    if (manager?.Player == null || manager.Village == null) return false;
    bool valid = SaveDataMapper.TryBuildValidatedState(
        data,
        PlayerSaveData.CurrentVersion,
        typeId => ConfigTableManager.Instance.IsReady
            ? ConfigTableManager.Instance.GetBuilding(typeId) : null,
        44,
        44,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        out ValidatedSaveState state,
        out string error);
    if (valid)
    {
        manager.ReplaceValidatedState(state);
        return true;
    }
    Debug.LogError($"[SaveManager] Rejected slot {targetSlot}: {error}");
    return false;
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.SaveValidationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-006-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-006-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-006.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-006.log"
```

Expected: both commands exit `0`; null rows, malformed UTC timestamps, upper-cap violations, invalid levels, out-of-bounds or overlapping footprints, and unbounded upgrade timestamps are rejected; no rejected save changes a reachable model, while a valid save publishes one notification after both replacement models are visible.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/SaveValidationTests.cs Assets/Scripts/Data/SaveDataMapper.cs Assets/Scripts/Data/PlayerSaveData.cs Assets/Scripts/Data/Model/VillageData.cs Assets/Scripts/Data/DataManager.cs Assets/Scripts/Data/SaveManager.cs
git commit -m "fix: validate save transaction before apply"
```

---

### Task 16: DATA-007 Make LocalStore Atomic, Observable on Failure, and Primitive-Safe

**Files:**
- Create: `Assets/Tests/Runtime/Data/LocalStoreBackendTests.cs`
- Create: `Assets/Scripts/Data/LocalStoreBackend.cs`
- Modify: `Assets/Scripts/Data/LocalStore.cs:18-219`

**Interfaces:**
- Consumes: `IAtomicTextWriter` from Task 10 and an explicit file path.
- Produces: instance `LocalStoreBackend` plus `LocalStoreWriteResult`/`LocalStoreWriteCode` from backend and static façade methods `Set`, `Remove`, and `ClearAll`; missing keys and persistence failures remain distinguishable.

- [ ] **Step 1: Write failing primitive round-trip and write-failure rollback tests**

```csharp
using System;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class LocalStoreBackendTests : RuntimeTestBase
    {
        [Serializable]
        private sealed class Profile
        {
            public int Level;
        }

        private sealed class ToggleWriter : IAtomicTextWriter
        {
            private readonly IAtomicTextWriter _inner = new AtomicFileWriter();
            public bool Fail { get; set; }
            public FileWriteResult Write(string path, string content) => Fail
                ? new FileWriteResult(false, "disk full")
                : _inner.Write(path, content);
        }

        [Test]
        public void LocalStore_FirstWriteFailureAndSupportedTypes_AreObservableAndAtomic()
        {
            string path = System.IO.Path.Combine(CreateTempDirectory("local-store"), "store.json");
            var writer = new ToggleWriter();
            var first = new LocalStoreBackend(path, writer);
            Assert.That(first.Set("name", "Alice").Code, Is.EqualTo(LocalStoreWriteCode.Success));
            Assert.That(first.Set("level", 7).Code, Is.EqualTo(LocalStoreWriteCode.Success));
            Assert.That(first.Set("profile", new Profile { Level = 9 }).Code,
                Is.EqualTo(LocalStoreWriteCode.Success));

            var reloaded = new LocalStoreBackend(path, new AtomicFileWriter());
            Assert.That(reloaded.Get("name", string.Empty), Is.EqualTo("Alice"));
            Assert.That(reloaded.Get("level", 0), Is.EqualTo(7));
            Assert.That(reloaded.Get("profile", new Profile()).Level, Is.EqualTo(9));

            writer.Fail = true;
            Assert.That(first.Set("unsaved", 1).Code,
                Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Has("unsaved"), Is.False);
            Assert.That(first.Remove("missing").Code, Is.EqualTo(LocalStoreWriteCode.NotFound));
            Assert.That(first.Remove("name").Code,
                Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Get("name", string.Empty), Is.EqualTo("Alice"));
            int countBeforeClear = first.Count;
            Assert.That(first.ClearAll().Code, Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Count, Is.EqualTo(countBeforeClear));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify the backend and result contract are absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.LocalStoreBackendTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-007-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-007-red.log"
```

Expected: nonzero exit because `LocalStoreBackend` does not exist and current static `Set` cannot return failure.

- [ ] **Step 3: Implement an instance backend and make static calls delegate to it**

Create the complete `LocalStoreBackend.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public enum LocalStoreWriteCode
    {
        Success,
        NotFound,
        WriteFailed,
    }

    public readonly struct LocalStoreWriteResult
    {
        public LocalStoreWriteCode Code { get; }
        public string Error { get; }
        public bool Succeeded => Code == LocalStoreWriteCode.Success;
        public LocalStoreWriteResult(LocalStoreWriteCode code, string error = null)
        {
            Code = code;
            Error = error;
        }
    }

    public sealed class LocalStoreBackend
    {
        [Serializable]
        private sealed class ValueBox<T> { public T Value; }

        [Serializable]
        private sealed class StoreEntry
        {
            public string TypeName;
            public string JsonData;
        }

        [Serializable]
        private sealed class StoreFileData
        {
            public List<string> Keys = new List<string>();
            public List<string> TypeNames = new List<string>();
            public List<string> JsonValues = new List<string>();

            public static StoreFileData FromDictionary(
                IReadOnlyDictionary<string, StoreEntry> entries)
            {
                var data = new StoreFileData();
                foreach (KeyValuePair<string, StoreEntry> pair in entries)
                {
                    data.Keys.Add(pair.Key);
                    data.TypeNames.Add(pair.Value.TypeName);
                    data.JsonValues.Add(pair.Value.JsonData);
                }
                return data;
            }

            public Dictionary<string, StoreEntry> ToDictionary()
            {
                var result = new Dictionary<string, StoreEntry>(Keys.Count);
                int count = Math.Min(Keys.Count, Math.Min(TypeNames.Count, JsonValues.Count));
                for (int index = 0; index < count; index++)
                {
                    result[Keys[index]] = new StoreEntry
                    {
                        TypeName = TypeNames[index],
                        JsonData = JsonValues[index],
                    };
                }
                return result;
            }
        }

        private readonly string _filePath;
        private readonly IAtomicTextWriter _writer;
        private Dictionary<string, StoreEntry> _entries;
        private bool _loaded;

        public LocalStoreBackend(string filePath, IAtomicTextWriter writer)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("Path is required", nameof(filePath))
                : filePath;
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public int Count { get { EnsureLoaded(); return _entries.Count; } }

        public LocalStoreWriteResult Set<T>(string key, T value)
        {
            ValidateKey(key);
            EnsureLoaded();
            if (value == null) return Remove(key);
            bool hadPrevious = _entries.TryGetValue(key, out StoreEntry previous);
            _entries[key] = new StoreEntry
            {
                TypeName = typeof(T).AssemblyQualifiedName,
                JsonData = JsonUtility.ToJson(new ValueBox<T> { Value = value }),
            };
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            if (hadPrevious) _entries[key] = previous;
            else _entries.Remove(key);
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            return TryGet(key, out T value) ? value : defaultValue;
        }

        public bool TryGet<T>(string key, out T value)
        {
            ValidateKey(key);
            EnsureLoaded();
            value = default;
            if (!_entries.TryGetValue(key, out StoreEntry entry) ||
                entry.TypeName != typeof(T).AssemblyQualifiedName)
                return false;
            try
            {
                ValueBox<T> box = JsonUtility.FromJson<ValueBox<T>>(entry.JsonData);
                if (box == null) return false;
                value = box.Value;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalStore] Read failed for {key}: {exception.Message}");
                return false;
            }
        }

        public bool Has(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            return _entries.ContainsKey(key);
        }

        public LocalStoreWriteResult Remove(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            if (!_entries.TryGetValue(key, out StoreEntry previous))
                return new LocalStoreWriteResult(LocalStoreWriteCode.NotFound);
            _entries.Remove(key);
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            _entries[key] = previous;
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public LocalStoreWriteResult ClearAll()
        {
            EnsureLoaded();
            var previous = new Dictionary<string, StoreEntry>(_entries);
            _entries.Clear();
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            _entries = previous;
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public void ReloadFromDisk()
        {
            _loaded = false;
            _entries = null;
            EnsureLoaded();
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_filePath))
                {
                    _entries = new Dictionary<string, StoreEntry>();
                    return;
                }
                StoreFileData data = JsonUtility.FromJson<StoreFileData>(
                    File.ReadAllText(_filePath));
                _entries = data?.ToDictionary() ?? new Dictionary<string, StoreEntry>();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LocalStore] Load failed: {exception.Message}");
                _entries = new Dictionary<string, StoreEntry>();
            }
        }

        private FileWriteResult Flush()
        {
            StoreFileData data = StoreFileData.FromDictionary(_entries);
            return _writer.Write(_filePath, JsonUtility.ToJson(data, false));
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required", nameof(key));
        }
    }
}
```

Replace `LocalStore.cs` with this façade (retain its namespace imports for `Path` and `Application`):

```csharp
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public static class LocalStore
    {
        private static readonly LocalStoreBackend Backend = new LocalStoreBackend(
            Path.Combine(Application.persistentDataPath, "local_store.json"),
            new AtomicFileWriter());

        public static int Count => Backend.Count;
        public static LocalStoreWriteResult Set<T>(string key, T value) =>
            Backend.Set(key, value);
        public static T Get<T>(string key, T defaultValue = default) =>
            Backend.Get(key, defaultValue);
        public static bool TryGet<T>(string key, out T value) => Backend.TryGet(key, out value);
        public static bool Has(string key) => Backend.Has(key);
        public static LocalStoreWriteResult Remove(string key) => Backend.Remove(key);
        public static LocalStoreWriteResult ClearAll() => Backend.ClearAll();
        public static void ReloadFromDisk() => Backend.ReloadFromDisk();
    }
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Data.LocalStoreBackendTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/data-007-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/data-007-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-007.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-data-007.log"
```

Expected: both commands exit `0`; string/int survive a new backend instance, failed writes leave no in-memory lie, and callers distinguish `NotFound` from `WriteFailed` without parsing logs.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Data/LocalStoreBackendTests.cs Assets/Scripts/Data/LocalStoreBackend.cs Assets/Scripts/Data/LocalStore.cs
git commit -m "fix: make local store atomic and primitive safe"
```

---

### Task 17: UI-001 Create Exactly One Compatible EventSystem

**Files:**
- Create: `Assets/Tests/Runtime/UI/UIEventSystemTests.cs`
- Modify: `Assets/Scripts/UI/Core/UIManager.cs:1-40`

**Interfaces:**
- Consumes: Unity uGUI `EventSystem`, `BaseInputModule`, and `StandaloneInputModule`.
- Produces: `UIManager.Initialize()` guarantees one persistent `EventSystem` with an input module before any runtime panel opens.

- [ ] **Step 1: Write failing creation, reuse, and pointer-click tests**

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UIEventSystemTests : RuntimeTestBase
    {
        private sealed class RoutedPointerTestModule : BaseInputModule
        {
            private Vector2 _position;
            private bool _pendingClick;

            public void QueueClick(Vector2 position)
            {
                _position = position;
                _pendingClick = true;
            }

            public override void Process()
            {
                if (!_pendingClick) return;
                _pendingClick = false;
                var pointer = new PointerEventData(eventSystem)
                {
                    button = PointerEventData.InputButton.Left,
                    position = _position,
                };
                var hits = new List<RaycastResult>();
                eventSystem.RaycastAll(pointer, hits);
                Assert.That(hits, Is.Not.Empty, "the real Launcher button must be raycastable");
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                    hits[0].gameObject);
                Assert.That(target, Is.Not.Null);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
            }
        }

        [UnityTest]
        public IEnumerator UIManager_Initialize_CreatesSingleEventSystem()
        {
            CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            Assert.That(systems, Has.Length.EqualTo(1));
            Assert.That(systems[0].GetComponent<BaseInputModule>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator UIManager_WithExistingEventSystem_DoesNotDuplicate()
        {
            var existing = CreateOwnedObject("ExistingEventSystem");
            var expected = existing.AddComponent<EventSystem>();
            existing.AddComponent<StandaloneInputModule>();

            CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            Assert.That(systems, Has.Length.EqualTo(1));
            Assert.That(systems[0], Is.SameAs(expected));
        }

        [UnityTest]
        public IEnumerator LauncherButton_PointerClick_InvokesAction()
        {
            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;
            manager.Open<LoginPanel>();
            yield return null;

            LoginPanel launcherPanel = Object.FindObjectOfType<LoginPanel>(true);
            Assert.That(launcherPanel, Is.Not.Null);
            RectTransform offlineButton = launcherPanel.transform.Find("BtnOffline")
                as RectTransform;
            Assert.That(offlineButton, Is.Not.Null);
            Canvas.ForceUpdateCanvases();

            int routedActions = 0;
            EventBus.Subscribe(LoginEvents.OfflineLogin, () => routedActions++);
            foreach (BaseInputModule existing in
                EventSystem.current.GetComponents<BaseInputModule>())
                existing.enabled = false;
            var module = EventSystem.current.gameObject.AddComponent<RoutedPointerTestModule>();
            module.QueueClick(RectTransformUtility.WorldToScreenPoint(
                null, offlineButton.TransformPoint(offlineButton.rect.center)));
            module.Process();

            Assert.That(routedActions, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify initialization has no EventSystem contract**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UIEventSystemTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-001-red.log"
```

Expected: nonzero exit because the first test finds zero event systems and the click test has no current event system.

- [ ] **Step 3: Normalize the EventSystem before creating layer canvases**

Add `using UnityEngine.EventSystems;` to `UIManager.cs`, add `private GameObject _ownedEventSystem;`, call `EnsureEventSystem();` immediately after `_stack = new UIStack();`, and add this exact instance method:

```csharp
private void EnsureEventSystem()
{
    EventSystem[] systems = FindObjectsOfType<EventSystem>(true);
    EventSystem primary = EventSystem.current;
    if (primary == null && systems.Length > 0)
        primary = systems[0];

    if (primary == null)
    {
        var eventObject = new GameObject("EventSystem");
        _ownedEventSystem = eventObject;
        primary = eventObject.AddComponent<EventSystem>();
        eventObject.AddComponent<StandaloneInputModule>();
    }
    else if (primary.GetComponent<BaseInputModule>() == null)
    {
        primary.gameObject.AddComponent<StandaloneInputModule>();
    }

    primary.transform.SetParent(null);
    DontDestroyOnLoad(primary.gameObject);

    foreach (EventSystem candidate in systems)
    {
        if (candidate != null && candidate != primary)
            Destroy(candidate.gameObject);
    }
}
```

Extend `UIManager.OnDestroy` so it destroys `_ownedEventSystem` only when that object was created by this manager, clears the field, and then calls the existing base cleanup. It must never destroy a reused scene-owned EventSystem. `RuntimeTestBase` independently destroys any surviving EventSystem during teardown so a failed assertion cannot contaminate the next fixture.

The complete `Initialize` method becomes:

```csharp
private void Initialize()
{
    _stack = new UIStack();
    EnsureEventSystem();
    CreateUIRoot();
    CreateLayerCanvases();

    if (GetComponent<BuildingInfoController>() == null)
        gameObject.AddComponent<BuildingInfoController>();
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UIEventSystemTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-001.log"
```

Expected: both commands exit `0`; initialization creates or reuses one compatible system, teardown cannot leak it into the next fixture, and the input-module/raycast route clicks the actual `LoginPanel/BtnOffline` control and invokes one Launcher action.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/UI/UIEventSystemTests.cs Assets/Scripts/UI/Core/UIManager.cs
git commit -m "fix: create one runtime event system"
```

---

### Task 18: UI-002 Build Panel Hierarchy Once and Rebind Current Data

**Files:**
- Create: `Assets/Tests/Runtime/UI/RebindProbePanel.cs`
- Create: `Assets/Tests/Runtime/UI/UIPanelRebindTests.cs`
- Modify: `Assets/Scripts/UI/Core/UIBase.cs:35-116`
- Modify: `Assets/Scripts/UI/Core/UIManager.cs:91-120`
- Modify: `Assets/Scripts/UI/Panels/BattleHUD.cs`
- Modify: `Assets/Scripts/UI/Panels/BuildingInfoPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/CharacterSelectPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/DeployPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/ErrorPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/ForceUpdatePanel.cs`
- Modify: `Assets/Scripts/UI/Panels/LoadingPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/LoginPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/MainVillageHUD.cs`
- Modify: `Assets/Scripts/UI/Panels/ResultPanel.cs`
- Modify: `Assets/Scripts/UI/Panels/ToastPanel.cs`

**Interfaces:**
- Consumes: existing `OnOpen`, `OnClose`, pause/resume, and panel cache behavior.
- Produces: `UIBase.BuildOnce(System.Action)`, `UIBase.OnRebind(object)`, and `UIBase.DoRebind(object)`; `UIManager.Open<T>` rebinds already-open panels without subscribing twice.

- [ ] **Step 1: Write a probe panel and failing reopen regression**

Create `RebindProbePanel.cs`:

```csharp
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class RebindProbePanel : UIBase
    {
        public int BuildCount { get; private set; }
        public int BindCount { get; private set; }
        public int CallbackCount { get; private set; }
        public string BoundValue { get; private set; }

        protected override void OnOpen(object data)
        {
            BuildOnce(() =>
            {
                BuildCount++;
                new UnityEngine.GameObject("StaticChild").transform.SetParent(transform, false);
            });
            Bind(data);
            EventBus.Subscribe("RebindProbeEvent", OnProbeEvent);
        }

        protected override void OnRebind(object data) => Bind(data);

        protected override void OnClose()
        {
            EventBus.Unsubscribe("RebindProbeEvent", OnProbeEvent);
        }

        private void Bind(object data)
        {
            BindCount++;
            BoundValue = data as string;
        }

        private void OnProbeEvent() => CallbackCount++;
    }
}
```

Create `UIPanelRebindTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UIPanelRebindTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator Panel_Reopen_ReusesHierarchyAndRebindsData()
        {
            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel panel = manager.Open<RebindProbePanel>("first");
            manager.Close(panel);
            panel = manager.Open<RebindProbePanel>("second");
            RebindProbePanel sameOpenPanel = manager.Open<RebindProbePanel>("third");
            EventBus.Publish("RebindProbeEvent");

            Assert.That(sameOpenPanel, Is.SameAs(panel));
            Assert.That(panel.BuildCount, Is.EqualTo(1));
            Assert.That(panel.transform.childCount, Is.EqualTo(1));
            Assert.That(panel.BindCount, Is.EqualTo(3));
            Assert.That(panel.BoundValue, Is.EqualTo("third"));
            Assert.That(panel.CallbackCount, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify the lifecycle API is absent**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UIPanelRebindTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-002-red.log"
```

Expected: nonzero exit because `BuildOnce` and `OnRebind` do not exist; current `Open` also ignores new data when an open panel is not paused.

- [ ] **Step 3: Add build-once and rebind phases to UIBase**

Add `using System;`, add `private bool _hierarchyBuilt;`, and add these members to `UIBase`:

```csharp
protected void BuildOnce(Action buildHierarchy)
{
    if (_hierarchyBuilt) return;
    if (buildHierarchy == null) throw new ArgumentNullException(nameof(buildHierarchy));
    buildHierarchy();
    _hierarchyBuilt = true;
}

internal void DoRebind(object data)
{
    if (!IsOpen) return;
    OpenData = data;
    OnRebind(data);
}

protected virtual void OnRebind(object data) { }
```

Replace `DoResume` with:

```csharp
internal void DoResume(object data = null)
{
    if (!IsOpen || !IsPaused) return;

    IsPaused = false;
    CanvasGroup.interactable = true;
    if (data != null)
        DoRebind(data);
    OnResume();
}
```

Replace the already-open branch in `UIManager.Open<T>` with:

```csharp
if (_panelCache.TryGetValue(type, out var existing) && existing.IsOpen)
{
    if (existing.IsPaused)
        existing.DoResume(data);
    else
        existing.DoRebind(data);
    return existing as T;
}
```

- [ ] **Step 4: Convert every production panel to build once and bind separately**

Use these exact `OnOpen` replacements for panels whose data is already refreshed outside `BuildUI`:

```csharp
// BattleHUD
protected override void OnOpen(object data)
{
    _battleStartTime = Time.time;
    BuildOnce(BuildUI);
}

// CharacterSelectPanel
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    RefreshSlots();
}

// DeployPanel: keep the existing field initialization before these two lines.
BuildOnce(BuildUI);
EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);

// ErrorPanel
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    EventBus.Subscribe<BootErrorEvent>(BootErrorEvent.EventKey, OnBootError);
}

// LoadingPanel
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    TryLoadLogo();
    EventBus.Subscribe<BootProgressEvent>(BootProgressEvent.EventKey, OnBootProgress);
}

// MainVillageHUD
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    BindObservables();
}
```

Remove `TryLoadLogo();` from `LoadingPanel.BuildUI`; `OnOpen` now reloads the destroyed logo sprite into the existing `_logoImage`. Replace the data-aware lifecycle methods in `ForceUpdatePanel`, `LoginPanel`, and `ToastPanel` with:

```csharp
// ForceUpdatePanel
protected override void OnOpen(object data)
{
    BindStoreUrl(data);
    BuildOnce(BuildUI);
}

protected override void OnRebind(object data) => BindStoreUrl(data);

private void BindStoreUrl(object data)
{
    _storeUrl = data as string ?? "market://details?id=xxx";
}

// LoginPanel
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    BindError(data);
    EventBus.Subscribe<string>(LoginEvents.LoginError, ShowError);
}

protected override void OnRebind(object data) => BindError(data);

private void BindError(object data)
{
    if (data is string error)
        ShowError(error);
}

// ToastPanel
protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    BindToast(data);
}

protected override void OnRebind(object data) => BindToast(data);

private void BindToast(object data)
{
    string message = data as string ?? string.Empty;
    float duration = 2f;
    if (data is System.Tuple<string, float> tuple)
    {
        message = tuple.Item1;
        duration = tuple.Item2;
    }

    _messageText.text = message;
    if (_autoCloseCoroutine != null)
        StopCoroutine(_autoCloseCoroutine);
    _autoCloseCoroutine = StartCoroutine(AutoClose(duration));
}
```

Change `ToastPanel.BuildUI(string message)` to `BuildUI()`, initialize `_messageText.text = string.Empty`, and leave the rest of its hierarchy code unchanged.

Simulation Task 15 has already added the UI assembly reference and canonical result import. Preserve those dependencies while replacing ResultPanel's data-dependent lifecycle and builder with these exact members; retain its constructor:

```csharp
private Text _titleText;
private Text _starsText;
private Text _lootText;

protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    BindResult(data);
}

protected override void OnRebind(object data) => BindResult(data);

private void BuildUI()
{
    var bg = gameObject.AddComponent<Image>();
    bg.color = new Color(0, 0, 0, 0.75f);

    var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
    card.transform.SetParent(transform, false);
    card.GetComponent<Image>().color = new Color(0.15f, 0.12f, 0.1f, 1f);
    var rect = card.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.2f, 0.25f);
    rect.anchorMax = new Vector2(0.8f, 0.75f);
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;

    _titleText = CreateText("Title", string.Empty, 40, new Vector2(0.5f, 0.85f),
        card.transform, TextAnchor.MiddleCenter, Color.white, 300, 60);
    _starsText = CreateText("Stars", string.Empty, 36, new Vector2(0.5f, 0.65f),
        card.transform, TextAnchor.MiddleCenter, Color.yellow, 300, 50);
    _lootText = CreateText("Loot", string.Empty, 22, new Vector2(0.5f, 0.45f),
        card.transform, TextAnchor.MiddleCenter, Color.white, 350, 40);
    CreateButton("BtnReturn", "Return", new Vector2(0.25f, 0.15f),
        new Vector2(0.75f, 0.30f), card.transform,
        new Color(0.35f, 0.6f, 0.3f),
        () => EventBus.Publish("Battle_ReturnToVillage"));
}

private void BindResult(object data)
{
    if (!(data is SimBattleResult result)) return;
    bool win = result.WinnerTeamId == SimTeam.Attacker;
    _titleText.text = win ? "Victory" : "Defeat";
    _titleText.color = win ? Color.yellow : Color.gray;
    _starsText.text = new string('*', result.Stars) + new string('-', 3 - result.Stars);
    _lootText.text = $"Gold: {result.LootGold}   Elixir: {result.LootElixir}";
}
```

Replace `BuildingInfoPanel`'s `OnOpen`, `Refresh`, and data-dependent builder with the following members. Keep its constructor and the `_interaction` lookup; Task 19 corrects that lookup's base lifecycle separately.

```csharp
private BuildingData _building;
private Button _upgradeButton;

protected override void OnOpen(object data)
{
    BuildOnce(BuildUI);
    BindBuilding(data as BuildingData);
}

protected override void OnRebind(object data) => BindBuilding(data as BuildingData);

private void BuildUI()
{
    var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
    card.transform.SetParent(transform, false);
    card.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.07f, 0.92f);
    var rect = card.GetComponent<RectTransform>();
    rect.anchorMin = new Vector2(0.15f, 0.35f);
    rect.anchorMax = new Vector2(0.85f, 0.65f);
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;

    _nameText = CreateText("Name", string.Empty, 28, new Vector2(0.5f, 0.85f),
        card.transform, TextAnchor.MiddleCenter, Color.white, 300, 40);
    _levelText = CreateText("Level", string.Empty, 20, new Vector2(0.5f, 0.68f),
        card.transform, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f), 250, 30);
    _statusText = CreateText("Status", string.Empty, 18, new Vector2(0.5f, 0.52f),
        card.transform, TextAnchor.MiddleCenter, Color.white, 200, 26);
    _upgradeCostText = CreateText("Cost", string.Empty, 16, new Vector2(0.5f, 0.38f),
        card.transform, TextAnchor.MiddleCenter, Color.white, 250, 24);
    _upgradeButton = CreateButton("BtnUpgrade", "Upgrade", new Vector2(0.3f, 0.12f),
        new Vector2(0.7f, 0.28f), card.transform, new Color(0.3f, 0.55f, 0.3f), () =>
        {
            if (_interaction == null) return;
            _interaction.UpgradeSelected();
            BuildingData selected = SelectionManager.Instance?.SelectedBuilding;
            if (selected != null) BindBuilding(selected);
        });
    CreateButton("BtnClose", "X", new Vector2(0.88f, 0.88f),
        new Vector2(0.97f, 0.97f), card.transform,
        new Color(0.5f, 0.2f, 0.2f), Close);
}

private void BindBuilding(BuildingData building)
{
    _building = building;
    if (_building == null)
    {
        _nameText.text = string.Empty;
        _levelText.text = string.Empty;
        _statusText.text = string.Empty;
        _upgradeCostText.text = string.Empty;
        _upgradeButton.gameObject.SetActive(false);
        return;
    }

    BuildingConfig config = ConfigTableManager.Instance.GetBuilding(_building.TypeId);
    int level = _building.Level.Value;
    int nextLevel = level + 1;
    _nameText.text = config != null ? config.Name : $"Building {_building.TypeId}";
    _levelText.text = $"Lv.{level}  HP {config?.GetHP(level) ?? 0}";
    _statusText.text = _building.IsUpgrading ? "Upgrading" : "Idle";
    bool canUpgrade = !_building.IsUpgrading && config != null && nextLevel <= config.MaxLevel;
    _upgradeCostText.text = canUpgrade
        ? $"Cost {config.GetBuildCost(nextLevel)}  Time {config.GetBuildTime(nextLevel)}s"
        : config == null ? string.Empty : "Max level";
    _upgradeButton.gameObject.SetActive(canUpgrade);
}

public void Refresh(BuildingData building) => BindBuilding(building);
```

- [ ] **Step 5: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UIPanelRebindTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-002.log"
```

Expected: both commands exit `0`; close/reopen and active rebind keep one child hierarchy, bind the newest data, and deliver one event callback.

- [ ] **Step 6: Commit the change**

```bash
git add Assets/Tests/Runtime/UI/RebindProbePanel.cs Assets/Tests/Runtime/UI/UIPanelRebindTests.cs Assets/Scripts/UI/Core/UIBase.cs Assets/Scripts/UI/Core/UIManager.cs Assets/Scripts/UI/Panels
git commit -m "fix: separate panel build and data binding"
```

---

### Task 19: UI-003 Run BuildingInfoPanel Through UIBase Awake

**Files:**
- Create: `Assets/Tests/Runtime/UI/BuildingInfoPanelLifecycleTests.cs`
- Modify: `Assets/Scripts/UI/Panels/BuildingInfoPanel.cs:29-32`

**Interfaces:**
- Consumes: `UIBase.Awake()` component and initial visibility setup.
- Produces: a real `protected override Awake()` that initializes `Canvas`, `CanvasGroup`, animation state, and the scene interaction reference.

- [ ] **Step 1: Write the failing base-initialization test**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class BuildingInfoPanelLifecycleTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator BuildingInfoPanel_Awake_InitializesUIBase()
        {
            var panelObject = CreateOwnedObject("BuildingInfoPanel");
            var panel = panelObject.AddComponent<BuildingInfoPanel>();
            yield return null;

            Assert.That(panel.Canvas, Is.Not.Null);
            Assert.That(panel.CanvasGroup, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.False);
        }
    }
}
```

- [ ] **Step 2: Run the test and verify the private Awake hides the base method**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.BuildingInfoPanelLifecycleTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-003-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-003-red.log"
```

Expected: nonzero exit because `Canvas` and `CanvasGroup` remain null and the object remains active.

- [ ] **Step 3: Override Awake and invoke the base lifecycle**

Replace the private method with:

```csharp
protected override void Awake()
{
    base.Awake();
    _interaction = Object.FindObjectOfType<BuildingInteraction>();
}
```

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.BuildingInfoPanelLifecycleTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-003-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-003-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-003.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-003.log"
```

Expected: both commands exit `0`; the derived panel has base UI components and begins hidden.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/UI/BuildingInfoPanelLifecycleTests.cs Assets/Scripts/UI/Panels/BuildingInfoPanel.cs
git commit -m "fix: preserve building panel base lifecycle"
```

---

### Task 20: UI-004 Retain Closed Layer Panels in the Cache

**Files:**
- Create: `Assets/Tests/Runtime/UI/UICloseLayerTests.cs`
- Modify: `Assets/Scripts/UI/Core/UIManager.cs:144-159`

**Interfaces:**
- Consumes: `UIStack.ClearFrom(UILayer)` and the existing closed-panel reuse branch in `GetOrCreatePanel<T>`.
- Produces: `CloseLayer` closes matching panels but retains one cached inactive instance per panel type.

- [ ] **Step 1: Write the failing duplicate-instance regression**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.UI
{
    public sealed class UICloseLayerTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator UIManager_CloseLayer_DoesNotLeakOrDuplicateCachedPanel()
        {
            var manager = CreateOwnedObject("UIManager").AddComponent<UIManager>();
            yield return null;

            RebindProbePanel first = manager.Open<RebindProbePanel>("first");
            manager.CloseLayer(UILayer.Normal);
            RebindProbePanel second = manager.Open<RebindProbePanel>("second");

            Assert.That(second, Is.SameAs(first));
            Assert.That(Object.FindObjectsOfType<RebindProbePanel>(true), Has.Length.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify cache eviction creates a second object**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UICloseLayerTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-004-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-004-red.log"
```

Expected: nonzero exit because `CloseLayer` removes the cache entry without destroying the first panel, then `Open` creates another.

- [ ] **Step 3: Close the stack without evicting reusable panels**

Replace `CloseLayer` with:

```csharp
public void CloseLayer(UILayer layer)
{
    _stack.ClearFrom(layer);
}
```

Remove `using System.Collections.Generic` only if Task 20 leaves no other generic collection use; `UIManager` still owns three generic dictionaries, so the import remains required.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.UI.UICloseLayerTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/ui-004-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/ui-004-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-004.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-ui-004.log"
```

Expected: both commands exit `0`; repeated close/open cycles reuse one inactive cached instance and one listener set.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/UI/UICloseLayerTests.cs Assets/Scripts/UI/Core/UIManager.cs
git commit -m "fix: retain panels closed by layer"
```

---

### Task 21: GAME-001 Resolve Every Cell in a Building Footprint

**Files:**
- Create: `Assets/Tests/Runtime/Gameplay/VillageOccupancyTests.cs`
- Modify: `Assets/Scripts/Data/Model/VillageData.cs:7-64`

**Interfaces:**
- Consumes: `BuildingConfig.SizeX`, `BuildingConfig.SizeY`, and `ConfigTableManager.GetBuilding(int)`.
- Produces: injectable `VillageData(System.Func<int, BuildingConfig>)`; `GetBuildingAt` and `GridManager.IsAreaOccupied` resolve every occupied footprint cell.

- [ ] **Step 1: Write the failing multi-cell footprint test**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class VillageOccupancyTests
    {
        [Test]
        public void VillageOccupancy_MultiCellFootprintBlocksEveryCell()
        {
            var config = new BuildingConfig { Id = 10, SizeX = 3, SizeY = 2 };
            var village = new VillageData(id => id == config.Id ? config : null);
            BuildingData building = BuildingData.Create(10, 5, 7);
            village.AddBuilding(building);

            for (int x = 5; x < 8; x++)
            for (int y = 7; y < 9; y++)
                Assert.That(village.GetBuildingAt(x, y), Is.SameAs(building));

            Assert.That(village.IsOccupied(8, 7), Is.False);
            Assert.That(village.IsOccupied(5, 9), Is.False);
        }
    }
}
```

- [ ] **Step 2: Run the test and verify only the origin resolves**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.VillageOccupancyTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-001-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-001-red.log"
```

Expected: nonzero exit because the injectable constructor is absent and current lookup compares only `(GridX, GridY)`.

- [ ] **Step 3: Make the footprint resolver authoritative for village lookup**

Add `using System;`, add the resolver field and constructors, and replace `GetBuildingAt` with:

```csharp
private readonly Func<int, BuildingConfig> _getBuildingConfig;

public VillageData()
    : this(typeId => ConfigTableManager.Instance.GetBuilding(typeId))
{
}

public VillageData(Func<int, BuildingConfig> getBuildingConfig)
{
    _getBuildingConfig = getBuildingConfig
        ?? throw new ArgumentNullException(nameof(getBuildingConfig));
}

public BuildingData GetBuildingAt(int gridX, int gridY)
{
    foreach (BuildingData building in Buildings)
    {
        BuildingConfig config = _getBuildingConfig(building.TypeId);
        int width = Math.Max(1, config?.SizeX ?? 1);
        int height = Math.Max(1, config?.SizeY ?? 1);
        int originX = building.GridX.Value;
        int originY = building.GridY.Value;
        if (gridX >= originX && gridX < originX + width &&
            gridY >= originY && gridY < originY + height)
            return building;
    }

    return null;
}
```

Keep `IsOccupied(int, int)` delegating to `GetBuildingAt`; `GridManager.IsAreaOccupied` already delegates each tested cell to that method and needs no second footprint implementation.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.VillageOccupancyTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-001-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-001-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-001.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-001.log"
```

Expected: both commands exit `0`; all six cells resolve to the same building and cells outside the footprint stay free.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Gameplay/VillageOccupancyTests.cs Assets/Scripts/Data/Model/VillageData.cs
git commit -m "fix: resolve full building footprints"
```

---

### Task 22: GAME-002 Recalculate Production on Add, Remove, and Upgrade

**Files:**
- Create: `Assets/Tests/Runtime/Gameplay/ResourceProducerLifecycleTests.cs`
- Modify: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs`

**Interfaces:**
- Consumes: `VillageData.OnBuildingAdded`, `VillageData.OnBuildingRemoved`, and `Observable<int>.OnChanged` for building levels.
- Produces: injectable `ResourceProducer(VillageData, PlayerData, System.Func<int, BuildingConfig>)`; `Start` attaches once and `Stop` detaches every village and level subscription.

- [ ] **Step 1: Write the failing reactive-rate test**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerLifecycleTests
    {
        [Test]
        public void ResourceProducer_AddRemoveUpgrade_RecalculatesAndStopUnsubscribes()
        {
            var goldConfig = new BuildingConfig
            {
                Id = 2,
                Name = "Gold Mine",
                Category = "Resource",
            };
            var village = new VillageData(_ => goldConfig);
            var player = new PlayerData();
            BuildingData first = BuildingData.Create(2, 0, 0);
            village.AddBuilding(first);
            var producer = new ResourceProducer(village, player, _ => goldConfig);

            producer.Start();
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));

            BuildingData second = BuildingData.Create(2, 3, 0);
            second.Level.Value = 2;
            village.AddBuilding(second);
            Assert.That(producer.GoldPerSecond, Is.EqualTo(1.5f));

            second.Level.Value = 3;
            Assert.That(producer.GoldPerSecond, Is.EqualTo(2f));

            village.RemoveBuilding(second.Id);
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));

            producer.Stop();
            first.Level.Value = 5;
            Assert.That(producer.GoldPerSecond, Is.EqualTo(0.5f));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify production only calculates at Start**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerLifecycleTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-002-red.log"
```

Expected: nonzero exit because the injectable constructor is absent and add/remove/level changes do not trigger recalculation.

- [ ] **Step 3: Own and release all production subscriptions**

Replace the producer's dependency fields and constructors with:

```csharp
private readonly VillageData _village;
private readonly PlayerData _player;
private readonly System.Func<int, BuildingConfig> _getBuildingConfig;
private bool _isRunning;

public ResourceProducer(VillageData village)
    : this(village, DataManager.Instance.Player,
        typeId => ConfigTableManager.Instance.GetBuilding(typeId))
{
}

public ResourceProducer(
    VillageData village,
    PlayerData player,
    System.Func<int, BuildingConfig> getBuildingConfig)
{
    _village = village ?? throw new System.ArgumentNullException(nameof(village));
    _player = player ?? throw new System.ArgumentNullException(nameof(player));
    _getBuildingConfig = getBuildingConfig
        ?? throw new System.ArgumentNullException(nameof(getBuildingConfig));
}
```

Replace `Start` and `Stop`, add the callbacks below, and change `RecalculateRates` to use `_getBuildingConfig(building.TypeId)` instead of `ConfigTableManager.Instance.GetBuilding(building.TypeId)`:

```csharp
public void Start()
{
    if (_isRunning) return;
    _isRunning = true;
    _village.OnBuildingAdded += OnBuildingAdded;
    _village.OnBuildingRemoved += OnBuildingRemoved;
    foreach (BuildingData building in _village.Buildings)
        building.Level.OnChanged += OnBuildingLevelChanged;
    RecalculateRates();
}

public void Stop()
{
    if (!_isRunning) return;
    _isRunning = false;
    _village.OnBuildingAdded -= OnBuildingAdded;
    _village.OnBuildingRemoved -= OnBuildingRemoved;
    foreach (BuildingData building in _village.Buildings)
        building.Level.OnChanged -= OnBuildingLevelChanged;
}

private void OnBuildingAdded(BuildingData building)
{
    building.Level.OnChanged += OnBuildingLevelChanged;
    RecalculateRates();
}

private void OnBuildingRemoved(BuildingData building)
{
    building.Level.OnChanged -= OnBuildingLevelChanged;
    RecalculateRates();
}

private void OnBuildingLevelChanged(int oldLevel, int newLevel)
{
    RecalculateRates();
}
```

Replace the first three lines of `Tick` after the running guard with the injected player dependency:

```csharp
if (!_isRunning) return;
if (_player == null) return;

_goldAccum += GoldPerSecond * deltaTime;
_elixirAccum += ElixirPerSecond * deltaTime;
_darkElixirAccum += DarkElixirPerSecond * deltaTime;
```

Replace all remaining `player.Gold`, `player.Elixir`, and `player.DarkElixir` references in `Tick` with `_player.Gold`, `_player.Elixir`, and `_player.DarkElixir` respectively.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerLifecycleTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-002.log"
```

Expected: both commands exit `0`; each active mutation updates the rate and mutations after `Stop` leave the last rate unchanged.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Gameplay/ResourceProducerLifecycleTests.cs Assets/Scripts/Gameplay/Economy/ResourceProducer.cs
git commit -m "fix: react to production building changes"
```

---

### Task 23: GAME-003 Use Explicit Resource Types for Production

**Files:**
- Create: `Assets/Tests/Runtime/Gameplay/ResourceProducerTypeTests.cs`
- Modify: `Assets/Scripts/Data/Config/BuildingConfig.cs`
- Modify: `Assets/Resources/Config/Buildings.json`
- Modify: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs:85-112`

**Interfaces:**
- Consumes: `BuildingConfig.Category == "Resource"` and the injectable producer dependencies from Task 22.
- Produces: `ResourceType` enum and `BuildingConfig.ResourceType`; production no longer infers currency from localized names.

- [ ] **Step 1: Write the failing dark-elixir classification test**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerTypeTests
    {
        [Test]
        public void ResourceProducer_DarkElixirDrill_ProducesDarkElixirOnly()
        {
            var config = new BuildingConfig
            {
                Id = 10,
                Name = "Dark Elixir Drill",
                Category = "Resource",
                ResourceType = ResourceType.DarkElixir,
            };
            var village = new VillageData(_ => config);
            village.AddBuilding(BuildingData.Create(10, 0, 0));
            var player = new PlayerData();
            player.Gold.Value = 0;
            player.Elixir.Value = 0;
            player.DarkElixir.Value = 0;
            var producer = new ResourceProducer(village, player, _ => config);

            producer.Start();
            producer.Tick(2f);

            Assert.That(player.Gold.Value, Is.Zero);
            Assert.That(player.Elixir.Value, Is.Zero);
            Assert.That(player.DarkElixir.Value, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run the test and verify the explicit type does not exist**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerTypeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-003-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-003-red.log"
```

Expected: nonzero exit because `ResourceType` and `BuildingConfig.ResourceType` do not exist.

- [ ] **Step 3: Add the enum and assign every resource configuration**

Add before `BuildingConfig` and add the field after `Category`:

```csharp
public enum ResourceType
{
    None = 0,
    Gold = 1,
    Elixir = 2,
    DarkElixir = 3,
}

public ResourceType ResourceType;
```

In `Buildings.json`, add the exact numeric field to each object after `Category`; use `0` for IDs 1, 4, 5, 6, 7, 8, and 9, and use these producer values:

```json
[
  {
    "Id": 2,
    "Category": "Resource",
    "ResourceType": 1
  },
  {
    "Id": 3,
    "Category": "Resource",
    "ResourceType": 2
  },
  {
    "Id": 10,
    "Category": "Resource",
    "ResourceType": 3
  }
]
```

The blocks above identify the existing objects and exact fields to insert; retain every other existing property in each object unchanged.

- [ ] **Step 4: Replace name matching with an exhaustive resource switch**

Replace the classification block inside `RecalculateRates` with:

```csharp
float baseRate = building.Level.Value * 0.5f;
switch (config.ResourceType)
{
    case ResourceType.Gold:
        goldRate += baseRate;
        break;
    case ResourceType.Elixir:
        elixirRate += baseRate;
        break;
    case ResourceType.DarkElixir:
        darkElixirRate += baseRate;
        break;
    case ResourceType.None:
        break;
    default:
        throw new System.ArgumentOutOfRangeException(
            nameof(config.ResourceType), config.ResourceType, "Unknown resource type");
}
```

- [ ] **Step 5: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerTypeTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-003-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-003-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-003.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-003.log"
```

Expected: both commands exit `0`; a dark-elixir drill increases only `DarkElixir` and production behavior is independent of `Name`.

- [ ] **Step 6: Commit the change**

```bash
git add Assets/Tests/Runtime/Gameplay/ResourceProducerTypeTests.cs Assets/Scripts/Data/Config/BuildingConfig.cs Assets/Resources/Config/Buildings.json Assets/Scripts/Gameplay/Economy/ResourceProducer.cs
git commit -m "fix: type resource production explicitly"
```

---

### Task 24: GAME-004 Rebind Scene-Bound Gameplay Services on Every Scene Change

**Files:**
- Create: `Assets/Tests/Runtime/Gameplay/SceneServiceRebindingTests.cs`
- Create: `Assets/Scripts/Gameplay/Scene/SceneServiceRegistry.cs`
- Modify: `Assets/Scripts/Gameplay/Control/InputManager.cs`
- Modify: `Assets/Scripts/Gameplay/Control/SelectionManager.cs`
- Modify: `Assets/Scripts/Gameplay/Building/BuildingPlacer.cs`
- Modify: `Assets/Scripts/Gameplay/Building/BuildingInteraction.cs`
- Modify: `Assets/Scripts/UI/Panels/BuildingInfoPanel.cs`

**Interfaces:**
- Consumes: `SceneManager.sceneLoaded`, `SceneManager.sceneUnloaded`, and scene-root component discovery.
- Produces: immutable `SceneServices`; persistent `SceneServiceRegistry.Current`; `Changed` publishes either the new scene's services or `SceneServices.Empty` before old references can be reused.

- [ ] **Step 1: Write the failing two-scene rebind and release test**

```csharp
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class SceneServiceRebindingTests : RuntimeTestBase
    {
        private sealed class ServiceSet
        {
            public Camera Camera;
            public CameraController CameraController;
            public GridRenderer GridRenderer;
            public BuildingPlacer Placer;
            public BuildingInteraction Interaction;
        }

        [UnityTest]
        public IEnumerator SceneChange_AllGameplayServices_RebindAndReleaseOldScene()
        {
            CreateOwnedObject("DataManager").AddComponent<DataManager>();
            CreateOwnedObject("GridManager").AddComponent<GridManager>();
            var registry = CreateOwnedObject("SceneServiceRegistry")
                .AddComponent<SceneServiceRegistry>();
            var input = CreateOwnedObject("InputManager").AddComponent<InputManager>();
            var selection = CreateOwnedObject("SelectionManager").AddComponent<SelectionManager>();
            var panel = CreateOwnedObject("BuildingInfoPanel").AddComponent<BuildingInfoPanel>();

            Scene firstScene = SceneManager.CreateScene("SceneBindings_First");
            Scene secondScene = SceneManager.CreateScene("SceneBindings_Second");
            ServiceSet first = CreateServices(firstScene, "First");
            ServiceSet second = CreateServices(secondScene, "Second");
            yield return null;

            registry.Rebind(firstScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<Camera>(first.Placer, "_mainCamera"), Is.SameAs(first.Camera));
            Assert.That(GetField<BuildingPlacer>(first.Interaction, "_placer"), Is.SameAs(first.Placer));
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.SameAs(first.Interaction));

            // Rebind directly: production Single-scene loads can publish the new scene
            // before the old sceneUnloaded callback reaches every consumer.
            registry.Rebind(secondScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<Camera>(second.Placer, "_mainCamera"), Is.SameAs(second.Camera));
            Assert.That(GetField<BuildingPlacer>(second.Interaction, "_placer"), Is.SameAs(second.Placer));
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.SameAs(second.Interaction));
            Assert.That(GetField<Camera>(first.Placer, "_mainCamera"), Is.Null);
            Assert.That(GetField<BuildingPlacer>(first.Interaction, "_placer"), Is.Null);

            registry.Release(secondScene);
            Assert.That(GetField<Camera>(input, "_mainCamera"), Is.Null);
            Assert.That(GetField<Camera>(selection, "_mainCamera"), Is.Null);
            Assert.That(GetField<BuildingInteraction>(panel, "_interaction"), Is.Null);

            yield return SceneManager.UnloadSceneAsync(firstScene);
            yield return SceneManager.UnloadSceneAsync(secondScene);
        }

        private static ServiceSet CreateServices(Scene scene, string suffix)
        {
            var root = new GameObject($"Services_{suffix}");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.tag = "MainCamera";
            var camera = root.AddComponent<Camera>();
            var cameraController = root.AddComponent<CameraController>();
            var gridRenderer = root.AddComponent<GridRenderer>();
            var placer = root.AddComponent<BuildingPlacer>();
            var interaction = root.AddComponent<BuildingInteraction>();
            return new ServiceSet
            {
                Camera = camera,
                CameraController = cameraController,
                GridRenderer = gridRenderer,
                Placer = placer,
                Interaction = interaction,
            };
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return field.GetValue(target) as T;
        }
    }
}
```

- [ ] **Step 2: Run the test and verify consumers retain their first Start/Awake references**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.SceneServiceRebindingTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-004-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-004-red.log"
```

Expected: nonzero exit because `SceneServiceRegistry`, `Rebind`, and `Release` do not exist; current consumers only resolve references in their first `Start` or `Awake`.

- [ ] **Step 3: Create the single scene-service registry**

Create `SceneServiceRegistry.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Gameplay
{
    public readonly struct SceneServices
    {
        public static SceneServices Empty => default;

        public Scene Scene { get; }
        public Camera MainCamera { get; }
        public CameraController CameraController { get; }
        public GridManager Grid { get; }
        public GridRenderer GridRenderer { get; }
        public BuildingPlacer Placer { get; }
        public BuildingInteraction Interaction { get; }

        public SceneServices(
            Scene scene,
            Camera mainCamera,
            CameraController cameraController,
            GridManager grid,
            GridRenderer gridRenderer,
            BuildingPlacer placer,
            BuildingInteraction interaction)
        {
            Scene = scene;
            MainCamera = mainCamera;
            CameraController = cameraController;
            Grid = grid;
            GridRenderer = gridRenderer;
            Placer = placer;
            Interaction = interaction;
        }
    }

    public sealed class SceneServiceRegistry : SingletonDontDestroy<SceneServiceRegistry>
    {
        public SceneServices Current { get; private set; }
        public event Action<SceneServices> Changed;

        protected override void Awake()
        {
            bool duplicate = HasInstance && Instance != this;
            base.Awake();
            if (duplicate) return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded)
                Rebind(active);
        }

        public void Rebind(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("Scene must be valid and loaded", nameof(scene));

            if (Current.Scene.IsValid() && Current.Scene != scene)
            {
                Current = SceneServices.Empty;
                Changed?.Invoke(Current);
            }

            Camera mainCamera = FindMainCamera(scene);
            Current = new SceneServices(
                scene,
                mainCamera,
                mainCamera != null ? mainCamera.GetComponent<CameraController>() : null,
                GridManager.HasInstance ? GridManager.Instance : null,
                FindInScene<GridRenderer>(scene),
                FindInScene<BuildingPlacer>(scene),
                FindInScene<BuildingInteraction>(scene));
            Changed?.Invoke(Current);
        }

        public void Release(Scene scene)
        {
            if (Current.Scene != scene) return;
            Current = SceneServices.Empty;
            Changed?.Invoke(Current);
        }

        protected override void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Changed = null;
            base.OnDestroy();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Rebind(scene);
        private void OnSceneUnloaded(Scene scene) => Release(scene);

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag("MainCamera"))
                        return camera;
                }
            }
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: Bind InputManager and SelectionManager to registry snapshots**

Add `private SceneServiceRegistry _sceneServices;` to each class. Replace `InputManager.Start`, add its destroy override and binder, and retain the rest of its gesture code:

```csharp
private void Start()
{
    _sceneServices = SceneServiceRegistry.Instance;
    _sceneServices.Changed += BindSceneServices;
    BindSceneServices(_sceneServices.Current);
}

protected override void OnDestroy()
{
    if (_sceneServices != null)
        _sceneServices.Changed -= BindSceneServices;
    base.OnDestroy();
}

private void BindSceneServices(SceneServices services)
{
    _mainCamera = services.MainCamera;
    _cameraController = services.CameraController;
}
```

Replace `SelectionManager.Start` and its private `OnDestroy`, then guard `OnGesture` before creating a ray:

```csharp
private void Start()
{
    _sceneServices = SceneServiceRegistry.Instance;
    _sceneServices.Changed += BindSceneServices;
    BindSceneServices(_sceneServices.Current);
    EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
}

protected override void OnDestroy()
{
    if (_sceneServices != null)
        _sceneServices.Changed -= BindSceneServices;
    EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
    base.OnDestroy();
}

private void BindSceneServices(SceneServices services)
{
    _mainCamera = services.MainCamera;
    _grid = services.Grid;
    if (_mainCamera == null || _grid == null)
        ClearSelection();
}
```

The top of `SelectionManager.OnGesture` becomes:

```csharp
private void OnGesture(GestureData gesture)
{
    if (gesture.Type != GestureType.Tap || _mainCamera == null || _grid == null)
        return;

    var ray = _mainCamera.ScreenPointToRay(gesture.ScreenPosition);
```

- [ ] **Step 5: Bind placer, interaction, and persistent panel to the same snapshot**

Add a `SceneServiceRegistry _sceneServices` field to `BuildingPlacer`, replace its `Start` and `OnDestroy`, and add the binder:

```csharp
private void Start()
{
    _sceneServices = SceneServiceRegistry.Instance;
    _sceneServices.Changed += BindSceneServices;
    BindSceneServices(_sceneServices.Current);
    EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
}

private void OnDestroy()
{
    if (_sceneServices != null)
        _sceneServices.Changed -= BindSceneServices;
    EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
}

private void BindSceneServices(SceneServices services)
{
    if (services.Placer != null && services.Placer != this) return;
    _grid = services.Grid;
    _gridRenderer = services.GridRenderer;
    _mainCamera = services.MainCamera;
    if (_mainCamera == null || _grid == null)
        CancelPlacement();
}
```

Add `if (_mainCamera == null || _grid == null) return;` at the top of both `UpdatePlacementPreview` and `TryPlace`.

Add a `SceneServiceRegistry _sceneServices` field to `BuildingInteraction`, replace its `Start` and `OnDestroy`, and add the binder:

```csharp
private void Start()
{
    _sceneServices = SceneServiceRegistry.Instance;
    _sceneServices.Changed += BindSceneServices;
    BindSceneServices(_sceneServices.Current);
    EventBus.Subscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
    EventBus.Subscribe(SelectionManager.EventSelectionCleared, OnDeselected);
    EventBus.Subscribe<GestureData>(InputManager.EventGesture, OnGesture);
}

private void OnDestroy()
{
    if (_sceneServices != null)
        _sceneServices.Changed -= BindSceneServices;
    EventBus.Unsubscribe<SelectionEvent>(SelectionManager.EventSelectionChanged, OnSelectionChanged);
    EventBus.Unsubscribe(SelectionManager.EventSelectionCleared, OnDeselected);
    EventBus.Unsubscribe<GestureData>(InputManager.EventGesture, OnGesture);
}

private void BindSceneServices(SceneServices services)
{
    if (services.Interaction != null && services.Interaction != this) return;
    _selection = SelectionManager.HasInstance ? SelectionManager.Instance : null;
    _placer = services.Placer;
    _gridRenderer = services.GridRenderer;
    if (_placer == null)
        IsEditMode = false;
}
```

Guard `OnGesture`, both `UpgradeSelected` overloads, `InstantUpgrade`, and `MoveBuilding` with `if (_selection == null) return;` before dereferencing `_selection`.

Add `private SceneServiceRegistry _sceneServices;` to `BuildingInfoPanel`. Extend the Task 19 `Awake` and add `OnDestroy` and the binder:

```csharp
protected override void Awake()
{
    base.Awake();
    _sceneServices = SceneServiceRegistry.Instance;
    _sceneServices.Changed += BindSceneServices;
    BindSceneServices(_sceneServices.Current);
}

private void OnDestroy()
{
    if (_sceneServices != null)
        _sceneServices.Changed -= BindSceneServices;
}

private void BindSceneServices(SceneServices services)
{
    _interaction = services.Interaction;
}
```

- [ ] **Step 6: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.SceneServiceRebindingTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-004-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-004-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-004.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-004.log"
```

Expected: both commands exit `0`; direct first-to-second rebinding publishes `SceneServices.Empty` before the replacement, old scene-owned consumers clear their references, persistent consumers bind only the second scene, and releasing it clears the final snapshot.

- [ ] **Step 7: Commit the change**

```bash
git add Assets/Tests/Runtime/Gameplay/SceneServiceRebindingTests.cs Assets/Scripts/Gameplay/Scene/SceneServiceRegistry.cs Assets/Scripts/Gameplay/Control/InputManager.cs Assets/Scripts/Gameplay/Control/SelectionManager.cs Assets/Scripts/Gameplay/Building/BuildingPlacer.cs Assets/Scripts/Gameplay/Building/BuildingInteraction.cs Assets/Scripts/UI/Panels/BuildingInfoPanel.cs
git commit -m "fix: rebind gameplay services across scenes"
```

---

### Task 25: GAME-005 Saturate Resource Addition Before Narrowing to Int

**Files:**
- Create: `Assets/Tests/Runtime/Gameplay/ResourceProducerSaturationTests.cs`
- Modify: `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs:61-77`

**Interfaces:**
- Consumes: typed production and injected player/config dependencies from Tasks 22-23.
- Produces: `ResourceProducer.SaturatingAdd(int, int)` performs addition in `long` and caps all three currencies at `int.MaxValue - 1`.

- [ ] **Step 1: Write the failing near-cap test for all resources**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Runtime.Tests.Gameplay
{
    public sealed class ResourceProducerSaturationTests
    {
        [Test]
        public void ResourceProducer_NearCap_SaturatesWithoutOverflow()
        {
            var configs = new Dictionary<int, BuildingConfig>
            {
                [2] = Producer(2, ResourceType.Gold),
                [3] = Producer(3, ResourceType.Elixir),
                [10] = Producer(10, ResourceType.DarkElixir),
            };
            var village = new VillageData(id => configs[id]);
            foreach (int typeId in configs.Keys)
            {
                BuildingData building = BuildingData.Create(typeId, typeId, 0);
                building.Level.Value = 10;
                village.AddBuilding(building);
            }

            var player = new PlayerData();
            player.Gold.Value = int.MaxValue - 2;
            player.Elixir.Value = int.MaxValue - 2;
            player.DarkElixir.Value = int.MaxValue - 2;
            var producer = new ResourceProducer(village, player, id => configs[id]);

            producer.Start();
            producer.Tick(1f);

            Assert.That(player.Gold.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.Elixir.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.DarkElixir.Value, Is.EqualTo(int.MaxValue - 1));
        }

        private static BuildingConfig Producer(int id, ResourceType type) =>
            new BuildingConfig { Id = id, Category = "Resource", ResourceType = type };
    }
}
```

- [ ] **Step 2: Run the test and verify int addition wraps before Math.Min**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerSaturationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-005-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-005-red.log"
```

Expected: nonzero exit because each `current + add` overflows to a negative `int` before the cap comparison.

- [ ] **Step 3: Widen first and use one helper for every currency**

Add this method to `ResourceProducer`:

```csharp
private static int SaturatingAdd(int current, int amount)
{
    long sum = (long)current + amount;
    return (int)System.Math.Min(sum, _maxResourceCap);
}
```

Replace the three assignments in `Tick` with:

```csharp
_player.Gold.Value = SaturatingAdd(_player.Gold.Value, goldAdd);
_player.Elixir.Value = SaturatingAdd(_player.Elixir.Value, elixirAdd);
_player.DarkElixir.Value = SaturatingAdd(_player.DarkElixir.Value, darkElixirAdd);
```

Keep each assignment inside its existing `if (add > 0)` block and keep the corresponding accumulator subtraction immediately after it.

- [ ] **Step 4: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Gameplay.ResourceProducerSaturationTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/game-005-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/game-005-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-005.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-game-005.log"
```

Expected: both commands exit `0`; Gold, Elixir, and DarkElixir all end at the cap and never become negative.

- [ ] **Step 5: Commit the change**

```bash
git add Assets/Tests/Runtime/Gameplay/ResourceProducerSaturationTests.cs Assets/Scripts/Gameplay/Economy/ResourceProducer.cs
git commit -m "fix: saturate resource production safely"
```

---

### Task 26: SCENE-002 Assemble an Interactive Village from Launcher

**Files:**
- Create: `Assets/Tests/Runtime/Scenes/LauncherVillageCompositionTests.cs`
- Create: `Assets/Scripts/Gameplay/Scene/VillageRuntimeBootstrap.cs`
- Create: `Assets/Scripts/Gameplay/Building/VillageViewRenderer.cs`
- Modify: `Assets/Scripts/FSM/Game/GameLauncher.cs:78-88`

**Interfaces:**
- Consumes: actual build scene zero, the real `GameLauncher`/offline save/FSM flow, the active Launcher's tagged main camera, `DataManager.Village`, `GridManager`, the Task 24 scene registry, and existing building configs when loaded.
- Produces: `GameLauncher.EnsureCreatedForActiveScene()`, `VillageRuntimeBootstrap.EnsureCreated()`; one scene-bound grid renderer, placer, interaction service, and village view renderer; one collidable `BuildingView` per building.

- [ ] **Step 1: Write the failing Launcher composition and selection test**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Scenes
{
    public sealed class LauncherVillageCompositionTests : RuntimeTestBase
    {
        [UnityTest]
        public IEnumerator LauncherPlayMode_EntersInteractiveVillage()
        {
            Scene previous = SceneManager.GetActiveScene();
            int previousSlot = SaveManager.CurrentSlot;
            int testSlot = SaveManager.MaxSaveSlots + 2000;
            string testSave = SaveManager.GetFilePath(testSlot);
            Assert.That(SceneUtility.GetScenePathByBuildIndex(0),
                Is.EqualTo("Assets/Scence/Launcher.unity"));
            SaveManager.CurrentSlot = testSlot;
            SaveManager.Delete(testSlot);
            RegisterCleanup(() =>
            {
                SaveManager.Delete(testSlot);
                SaveManager.CurrentSlot = previousSlot;
            });

            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
            Assert.That(load, Is.Not.Null);
            yield return load;
            Scene launcher = SceneManager.GetSceneByBuildIndex(0);
            Assert.That(launcher.IsValid() && launcher.isLoaded, Is.True);
            TrackLoadedScene(launcher);
            SceneManager.SetActiveScene(launcher);
            GameLauncher.EnsureCreatedForActiveScene();

            float deadline = Time.realtimeSinceStartup + 20f;
            while (GameFSM.Instance.CurrentState != GameState.MainVillage &&
                Time.realtimeSinceStartup < deadline)
            {
                if (GameFSM.Instance.CurrentState == GameState.Login)
                    EventBus.Publish(LoginEvents.OfflineLogin);
                yield return null;
            }
            yield return null;

            Assert.That(SceneManager.GetActiveScene().buildIndex, Is.Zero);
            Assert.That(Object.FindObjectOfType<GameLauncher>(), Is.Not.Null);
            Assert.That(SaveManager.Exists(testSlot), Is.True,
                "the real offline flow must create and load its persistent slot");
            Assert.That(GameFSM.Instance.CurrentState, Is.EqualTo(GameState.MainVillage));
            VillageRuntimeBootstrap bootstrap = FindInScene<VillageRuntimeBootstrap>(launcher);
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.gameObject.scene, Is.EqualTo(launcher));
            Camera camera = FindInScene<Camera>(launcher);
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.GetComponent<CameraController>(), Is.Not.Null);
            Assert.That(FindInScene<GridRenderer>(launcher), Is.Not.Null);
            Assert.That(FindInScene<BuildingPlacer>(launcher), Is.Not.Null);
            Assert.That(FindInScene<BuildingInteraction>(launcher), Is.Not.Null);
            Assert.That(FindInScene<VillageViewRenderer>(launcher), Is.Not.Null);
            BuildingView view = FindInScene<BuildingView>(launcher);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.GetComponent<Collider>(), Is.Not.Null);
            string buildingId = view.BuildingId;

            CameraController controller = camera.GetComponent<CameraController>();
            controller.enabled = false;
            camera.transform.position = new Vector3(0.5f, 8f, -8f);
            camera.transform.LookAt(view.transform.position);
            Physics.SyncTransforms();
            Vector3 screenPosition = camera.WorldToScreenPoint(view.transform.position);
            EventBus.Publish(InputManager.EventGesture, new GestureData
            {
                Type = GestureType.Tap,
                ScreenPosition = screenPosition,
            });

            Assert.That(SelectionManager.Instance.SelectedBuildingId, Is.EqualTo(buildingId));

            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(launcher);
            SaveManager.Delete(testSlot);
            SaveManager.CurrentSlot = previousSlot;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}
```

- [ ] **Step 2: Run the test and verify Launcher has no village composition owner**

Run:

```bash
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Scenes.LauncherVillageCompositionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/scene-002-red.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/scene-002-red.log"
```

Expected: nonzero exit because the real build-index-zero Launcher/offline/GameLauncher path does not yet create `VillageRuntimeBootstrap`, `VillageViewRenderer`, and interactive building services; a synthetic scene or direct `EnsureCreated` call cannot satisfy this test.

- [ ] **Step 3: Render and maintain one collidable view per village building**

Create `VillageViewRenderer.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Gameplay
{
    public sealed class VillageViewRenderer : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> _views =
            new Dictionary<string, GameObject>();
        private VillageData _village;

        public int ViewCount => _views.Count;

        private void Start()
        {
            Bind(DataManager.Instance.Village);
        }

        public void Bind(VillageData village)
        {
            Unbind();
            _village = village;
            if (_village == null) return;

            _village.OnBuildingAdded += AddView;
            _village.OnBuildingRemoved += RemoveView;
            foreach (BuildingData building in _village.Buildings)
                AddView(building);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (_village != null)
            {
                _village.OnBuildingAdded -= AddView;
                _village.OnBuildingRemoved -= RemoveView;
            }
            _village = null;

            foreach (GameObject view in _views.Values)
            {
                if (view != null) Destroy(view);
            }
            _views.Clear();
        }

        private void AddView(BuildingData building)
        {
            if (building == null || string.IsNullOrEmpty(building.Id) ||
                _views.ContainsKey(building.Id))
                return;

            BuildingConfig config = ConfigTableManager.HasInstance
                ? ConfigTableManager.Instance.GetBuilding(building.TypeId)
                : null;
            int width = Math.Max(1, config?.SizeX ?? 1);
            int height = Math.Max(1, config?.SizeY ?? 1);
            GridManager grid = GridManager.Instance;

            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = $"Building_{building.Id}";
            view.transform.SetParent(transform, false);
            view.transform.position = grid.GridToWorld(
                building.GridX.Value,
                building.GridY.Value,
                width * 0.5f,
                height * 0.5f) + Vector3.up * 0.5f;
            view.transform.localScale = new Vector3(
                width * grid.CellSize * 0.9f,
                1f,
                height * grid.CellSize * 0.9f);
            view.AddComponent<BuildingView>().BuildingId = building.Id;
            _views.Add(building.Id, view);
        }

        private void RemoveView(BuildingData building)
        {
            if (building == null || !_views.TryGetValue(building.Id, out GameObject view))
                return;
            _views.Remove(building.Id);
            if (view != null) Destroy(view);
        }
    }
}
```

- [ ] **Step 4: Add one idempotent owner for Launcher scene services**

Create `VillageRuntimeBootstrap.cs`:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZWebGLFrameWork.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class VillageRuntimeBootstrap : MonoBehaviour
    {
        public static VillageRuntimeBootstrap EnsureCreated()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                VillageRuntimeBootstrap existing =
                    root.GetComponentInChildren<VillageRuntimeBootstrap>(true);
                if (existing != null) return existing;
            }

            var rootObject = new GameObject("VillageRuntime");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            return rootObject.AddComponent<VillageRuntimeBootstrap>();
        }

        private void Awake()
        {
            Camera mainCamera = FindMainCamera(gameObject.scene);
            if (mainCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
                mainCamera = cameraObject.AddComponent<Camera>();
            }
            EnsureComponent<CameraController>(mainCamera.gameObject);

            _ = GridManager.Instance;
            _ = InputManager.Instance;
            _ = SelectionManager.Instance;
            EnsureComponent<GridRenderer>(gameObject);
            EnsureComponent<BuildingPlacer>(gameObject);
            EnsureComponent<BuildingInteraction>(gameObject);
            EnsureComponent<VillageViewRenderer>(gameObject);
        }

        private void Start()
        {
            SceneServiceRegistry.Instance.Rebind(gameObject.scene);
        }

        private static T EnsureComponent<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }

        private static Camera FindMainCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.CompareTag("MainCamera")) return camera;
                }
            }
            return null;
        }
    }
}
```

- [ ] **Step 5: Make GameLauncher assemble services before starting the FSM**

Add `using ZWebGLFrameWork.Gameplay;`, make `AutoCreate` delegate to this idempotent real-scene entry point, and replace `GameLauncher.Start` with:

```csharp
public static GameLauncher EnsureCreatedForActiveScene()
{
    Scene active = SceneManager.GetActiveScene();
    if (!active.IsValid() || active.name != "Launcher")
        throw new System.InvalidOperationException("Active build scene must be Launcher");
    GameLauncher existing = FindObjectOfType<GameLauncher>();
    if (existing != null) return existing;
    return new GameObject("GameLauncher").AddComponent<GameLauncher>();
}

private void Start()
{
    VillageRuntimeBootstrap.EnsureCreated();
    var fsm = GameFSM.Instance;
    IsReady = true;
    Debug.Log($"[GameLauncher] GameFSM ready; current state: {fsm.CurrentState}");
}
```

- [ ] **Step 6: Run focused and full runtime tests**

Run:

```bash
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests.Scenes.LauncherVillageCompositionTests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/scene-002-green.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/scene-002-green.log"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testFilter "ZWebGLFrameWork.Runtime.Tests" -testResults "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-scene-002.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/runtime-after-scene-002.log"
```

Expected: both commands exit `0`; the scene at actual build index zero is `Assets/Scence/Launcher.unity`, the real GameLauncher and persistent offline flow reach MainVillage, that scene gains one camera controller, grid renderer, input/selection path, placer, interaction service, and building renderer, and a routed tap selects a building restored from the real test slot.

- [ ] **Step 7: Commit the change**

```bash
git add Assets/Tests/Runtime/Scenes/LauncherVillageCompositionTests.cs Assets/Scripts/Gameplay/Scene/VillageRuntimeBootstrap.cs Assets/Scripts/Gameplay/Building/VillageViewRenderer.cs Assets/Scripts/FSM/Game/GameLauncher.cs
git commit -m "fix: assemble interactive launcher village"
```

---

## Final PlayMode Verification and Runtime Handoff

- [ ] **Run the complete unfiltered PlayMode suite with the pinned editor**

```bash
set -euo pipefail
mkdir -p "$ZWEB_EVIDENCE_ROOT/runtime/final"
"$UNITY" -batchmode -nographics -projectPath "$(pwd)" -runTests -testPlatform PlayMode -testResults "$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.xml" -logFile "$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.log"
test -s "$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.xml"
test -s "$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.log"
```

Expected: Unity exits `0`; the final XML reports `result="Passed"`, zero failed tests, a nonzero `testcasecount`, and cases from both `ZWebGLFrameWork.Runtime.Tests` and `ZWebGLFrameWork.Combat.PlayModeTests` required by the shared PlayMode manifest.

- [ ] **Validate structured results and all register-owned acceptance names**

```bash
set -euo pipefail
FINAL_XML="$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.xml"
FINAL_LOG="$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.log"
test -s "$FINAL_XML"
test -s "$FINAL_LOG"
python3 scripts/ci/validate-unity-results.py --mode PlayMode \
  --manifest ci/required-tests.json --results "$ZWEB_EVIDENCE_ROOT/runtime/final"
ruby -rjson -rrexml/document -e 'ownership=JSON.parse(File.read("docs/superpowers/plans/2026-07-17-project-repair-ownership.json")); ids=ownership.fetch("plans").fetch("runtime-repair").fetch("issues"); register=File.read("docs/superpowers/specs/2026-07-16-project-repair-issue-register.md"); required=[]; register.each_line{|line| next unless line =~ /^\|\s*([A-Z]+-\d{3})\s*\|/ && ids.include?($1); line.scan(/`([A-Za-z][A-Za-z0-9_]+)`/){|match| required << match[0] if match[0].include?("_")}}; required.uniq!; abort("expected 38 runtime acceptance names, got #{required.length}") unless required.length==38; document=REXML::Document.new(File.read(ARGV.fetch(0))); passed=[]; REXML::XPath.each(document, "//test-case") {|node| next unless node.attributes["result"]=="Passed"; raw=node.attributes["methodname"] || node.attributes["name"] || node.attributes["fullname"] || ""; passed << raw.split("(",2).first.split(".").last}; missing=required-passed.uniq; abort("runtime acceptance cases not passed: #{missing.join(", ")}") unless missing.empty?; puts "runtime acceptance executed: 38, passed: 38, missing: 0"' "$FINAL_XML"
rg -n '<test-run .*testcasecount="[1-9][0-9]*" .*result="Passed"' "$FINAL_XML"
fatal_matches="$ZWEB_EVIDENCE_ROOT/runtime/final/fatal-log-matches.txt"
set +e
rg -n 'error CS[0-9]+|Scripts have compiler errors|Unhandled Exception|NullReferenceException|MissingReferenceException|AssertionException|Assertion failed|Aborting batchmode due to failure|Fatal error|Crash!!!' "$FINAL_LOG" > "$fatal_matches"
fatal_status=$?
set -e
test "$fatal_status" -eq 1
test ! -s "$fatal_matches"
```

Expected: the CI validator exits `0`; the XML-derived scan prints `runtime acceptance executed: 38, passed: 38, missing: 0`; XML is passing and the expanded fatal-log scan writes an empty file. Final green tests may not deliberately emit these fatal signatures, so there are no broad or wildcard allowlist exclusions; any future exact expected line requires a dedicated regression and exact anchored exclusion.

- [ ] **Verify the pre-ledger tree and resolve every actual fix commit**

```bash
set -euo pipefail
git diff --check
test -z "$(git status --porcelain --untracked-files=all)"
ruby -e 'plan=File.read("docs/superpowers/plans/2026-07-17-runtime-repair.md"); subjects=plan.scan(/^close_runtime_issue\s+[A-Z]+-\d{3}\s+\S+\s+"([^"]+)"/).flatten; subjects << "test: add runtime play mode assembly"; abort("expected 27 unique task subjects") unless subjects.length==27 && subjects.uniq.length==27; history=`git log --format=%s`.lines.map(&:chomp); bad=subjects.map{|subject| [subject, history.count(subject)]}.reject{|_,count| count==1}; abort("task commit subject counts: #{bad.inspect}") unless bad.empty?; puts "runtime task commits: 27 unique subjects, each resolved once"'
```

Expected: both tree checks pass with no output; the subject audit proves Task 0 plus 26 exact reviewed implementation commits each resolve once even when other workstream commits are interleaved.

- [ ] **Close exactly the 26 runtime ledger entries with executed evidence and real commits**

This step requires CI Task 1's shared ledger CLI and runs only after the Simulation integration prerequisite for Task 14 and Task 26 is green.

```bash
set -euo pipefail
LEDGER="docs/superpowers/specs/2026-07-16-project-repair-evidence.json"
GATE="$ZWEB_EVIDENCE_ROOT/runtime/final/runtime-final.xml"
CLOSED_AT="$(date -u +%F)"
test -s "$GATE"

close_runtime_issue() {
  issue="$1"
  stem="$2"
  subject="$3"
  extra_fix_subject="${4:-}"
  red="${5:-$ZWEB_EVIDENCE_ROOT/runtime/$stem-red.log}"
  green="${6:-$ZWEB_EVIDENCE_ROOT/runtime/$stem-green.xml}"
  test -s "$red"
  test -s "$green"
  fix="$(git log --format='%H%x09%s' | awk -F '\t' -v wanted="$subject" '$2 == wanted { print $1 }')"
  test -n "$fix"
  test "$(printf '%s\n' "$fix" | wc -l | tr -d ' ')" -eq 1
  test "$(git show -s --format=%s "$fix")" = "$subject"
  fix_args=(--fix "$fix")
  if [ -n "$extra_fix_subject" ]; then
    extra_fix="$(git log --format='%H%x09%s' | awk -F '\t' -v wanted="$extra_fix_subject" '$2 == wanted { print $1 }')"
    test -n "$extra_fix"
    test "$(printf '%s\n' "$extra_fix" | wc -l | tr -d ' ')" -eq 1
    fix_args+=(--fix "$extra_fix")
  fi
  node scripts/audit/evidence-ledger.mjs close \
    --ledger "$LEDGER" \
    --issue "$issue" \
    --red "$red" \
    "${fix_args[@]}" \
    --green "$green" \
    --gate "$GATE" \
    --mode Fixed \
    --date "$CLOSED_AT"
}

close_runtime_issue CORE-001 core-001 "fix: invalidate resource cache on provider switch"
close_runtime_issue CORE-002 core-002 "fix: align asset bundle load and unload contracts"
close_runtime_issue BOOT-001 boot-001 "fix: define initial fsm transition semantics"
close_runtime_issue BOOT-002 boot-002 "fix: preserve state context across pause resume"
close_runtime_issue BOOT-003 boot-003 "fix: cancel launch pipeline on state exit"
close_runtime_issue BOOT-004 boot-004 "fix: stop boot for failed or pending hot update"
close_runtime_issue BOOT-005 boot-005 "fix: make scene loading failure explicit"
close_runtime_issue FSM-001 fsm-001 "fix: serialize reentrant fsm transitions"
close_runtime_issue FSM-002 fsm-002 "feat: bind async work to state lifetime" \
  "feat(combat): route production battle through session" \
  /tmp/zweb-sim-migration/task-15/battle-cancel-red.xml \
  /tmp/zweb-sim-migration/task-15/battle-cancel-green.xml
close_runtime_issue DATA-001 data-001 "fix: make save writes first-write safe"
close_runtime_issue DATA-002 data-002 "fix: preserve complete building save state"
close_runtime_issue DATA-003 data-003 "fix: track complete autosave state"
close_runtime_issue DATA-004 data-004 "fix: gate offline entry on persistence success"
close_runtime_issue DATA-005 data-005 "fix: apply battle rewards transactionally once"
close_runtime_issue DATA-006 data-006 "fix: validate save transaction before apply"
close_runtime_issue DATA-007 data-007 "fix: make local store atomic and primitive safe"
close_runtime_issue UI-001 ui-001 "fix: create one runtime event system"
close_runtime_issue UI-002 ui-002 "fix: separate panel build and data binding"
close_runtime_issue UI-003 ui-003 "fix: preserve building panel base lifecycle"
close_runtime_issue UI-004 ui-004 "fix: retain panels closed by layer"
close_runtime_issue GAME-001 game-001 "fix: resolve full building footprints"
close_runtime_issue GAME-002 game-002 "fix: react to production building changes"
close_runtime_issue GAME-003 game-003 "fix: type resource production explicitly"
close_runtime_issue GAME-004 game-004 "fix: rebind gameplay services across scenes"
close_runtime_issue GAME-005 game-005 "fix: saturate resource production safely"
close_runtime_issue SCENE-002 scene-002 "fix: assemble interactive launcher village"

node scripts/audit/evidence-ledger.mjs validate --ledger "$LEDGER"
```

Expected: exactly 26 `closed` messages print; every entry has nonempty red/fix/green/gate receipts; validator exits `0` while other workstream entries may remain Open.

- [ ] **Commit ledger receipts and enforce a strictly empty final tree**

```bash
set -euo pipefail
git add docs/superpowers/specs/2026-07-16-project-repair-evidence.json \
  docs/superpowers/evidence/artifacts
git diff --cached --check
git commit -m "test: record runtime closure evidence"
node scripts/audit/evidence-ledger.mjs validate \
  --ledger docs/superpowers/specs/2026-07-16-project-repair-evidence.json
test -z "$(git status --porcelain --untracked-files=all)"
```

Expected: the evidence commit succeeds, ledger validation passes, and the final strict status check emits no output. No CI, Network, SimulationCore, Replay, Server, Legacy Combat, build, or asset entry is closed by this batch.

## Self-Review Record

This plan was reviewed against the issue register, ownership manifest, current runtime source signatures, the Simulation migration's canonical result contract, and the CI plan's ledger/result-validator interfaces.

| Review | Command/result |
|---|---|
| Spec ownership | Ownership/task/closure parity scan: `owned=26 task_ids=26 closure_ids=26`. |
| Acceptance names | Register-to-test-method scan: `acceptance_expected=38 acceptance_methods=38 missing=0`. The count is the literal register expansion for these 26 IDs. |
| Placeholder scan | The forbidden placeholder expression below returns no matches. |
| External artifacts | `rg -n 'TestResults/'` returns no matches; all raw evidence uses `$ZWEB_EVIDENCE_ROOT/runtime`. |
| Canonical rewards | Scoped scan finds no legacy result type, boolean authority flag, random battle ID, or in-memory-only dedupe in the reward task. |
| Type/interface consistency | Scene loading uses one `ISceneLoadService` signature end-to-end; save validation declares village/receipt DTO fields before applying them; LocalStore backend/facade methods match; FSM/UI/test asmdefs add direct SimulationCore references before consuming `SimBattleResult`; `SceneServiceRegistry` snapshots match all five consumers. |
| Markdown integrity | Fence scan: `code_fences=386 balanced=true` at review time; rerun after any plan edit. |

Run these scans after any amendment to the plan:

```bash
PLAN="docs/superpowers/plans/2026-07-17-runtime-repair.md"
! sed '/^## Self-Review Record/,$d' "$PLAN" | \
  rg -n 'TBD|TODO|implement later|fill in|appropriate error|similar to|Write tests for the above|\.\.\.'
! sed '/^## Self-Review Record/,$d' "$PLAN" | rg -n 'TestResults/'
! sed -n '/^### Task 14:/,/^### Task 15:/p' "$PLAN" | \
  rg -n 'CombatEndEvent|bool authoritative|Guid\.NewGuid|_appliedBattleIds|_battleId'
ruby -rjson -e 'ownership=JSON.parse(File.read("docs/superpowers/plans/2026-07-17-project-repair-ownership.json")); owned=ownership.fetch("plans").fetch("runtime-repair").fetch("issues"); plan=File.read(ARGV.fetch(0)); tasks=plan.scan(/^### Task \d+: ([A-Z]+-\d{3})\b/).flatten; closes=plan.scan(/^close_runtime_issue ([A-Z]+-\d{3})\b/).flatten; abort("runtime ownership mismatch") unless tasks.sort==owned.sort && closes.sort==owned.sort; puts "owned=#{owned.length} task_ids=#{tasks.length} closure_ids=#{closes.length}"' "$PLAN"
ruby -rjson -e 'ownership=JSON.parse(File.read("docs/superpowers/plans/2026-07-17-project-repair-ownership.json")); ids=ownership.fetch("plans").fetch("runtime-repair").fetch("issues"); register=File.read("docs/superpowers/specs/2026-07-16-project-repair-issue-register.md"); plan=File.read(ARGV.fetch(0)); names=[]; register.each_line{|line| next unless line =~ /^\|\s*([A-Z]+-\d{3})\s*\|/ && ids.include?($1); line.scan(/`([A-Za-z][A-Za-z0-9_]+)`/){|match| names << match[0] if match[0].include?("_")}}; actual=names.select{|name| plan.match?(/public\s+(?:async\s+)?(?:[\w\.<>]+)\s+#{Regexp.escape(name)}\s*\(/)}; missing=names-actual; abort("missing: #{missing.join(", ")}") unless missing.empty? && actual.uniq.length==names.length; puts "acceptance_expected=#{names.length} acceptance_methods=#{actual.uniq.length} missing=0"' "$PLAN"
ruby -e 'fences=File.readlines(ARGV.fetch(0)).count{|line| line.start_with?("```")}; abort("unbalanced code fences") unless fences.even?; puts "code_fences=#{fences} balanced=true"' "$PLAN"
```

Expected: both negated `rg` checks and the scoped canonical-reward check produce no matches; ownership prints 26/26/26; acceptance prints 38/38/0; fence count is even.
