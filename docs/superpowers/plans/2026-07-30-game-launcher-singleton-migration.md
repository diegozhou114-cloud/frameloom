# GameLauncher Singleton Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to execute this plan task-by-task.

**Goal:** Move `GameLauncher` to the shared cross-scene singleton lifecycle while preserving Launcher-scene creation rules and configuration ownership.

**Architecture:** `SingletonDontDestroy<GameLauncher>` owns the GameObject singleton reference and duplicate rejection. `GameLauncher` owns only startup configuration state, clearing `Config` and `IsReady` when its primary instance is destroyed. `EnsureCreatedForActiveScene` remains scene-aware and does not call the lazy singleton accessor.

**Tech Stack:** Unity 2022.3.62f3c1, C#, NUnit PlayMode tests.

## Global Constraints

- Client framework only; do not modify `Server/`.
- Do not implement YooAsset, AssetBundle delivery, or production content.
- Run one Unity Editor batch process at a time and verify it exits after every run.
- Run focused RED/GREEN plus full EditMode and PlayMode before commit and normal push to `gitee/master`.

---

### Task 1: Unify GameLauncher Ownership

**Files:**
- Modify: `Assets/Scripts/FSM/Game/GameLauncher.cs:18-91`
- Modify: `Assets/Tests/Runtime/FSM/GameLauncherBackgroundThrottleTests.cs:9-118`

**Interfaces:**
- Consumes: `SingletonDontDestroy<T>.Awake()`, `IsPrimaryInstance`, and `OnDestroy()`.
- Produces: unchanged `GameLauncher.Config`, `GameLauncher.IsReady`, and `EnsureCreatedForActiveScene()` public contracts with shared singleton lifecycle ownership.

- [ ] **Step 1: Write failing migration tests**

```csharp
[Test]
public void GameLauncher_UsesSharedDontDestroySingletonBase()
{
    Assert.That(typeof(GameLauncher).BaseType,
        Is.EqualTo(typeof(SingletonDontDestroy<GameLauncher>)));
}
```

Keep the destruction regression test and make it assert that the generic base's `_instance` is first the created launcher and then CLR `null`; also assert `Config == null` and `IsReady == false` after destruction.

- [ ] **Step 2: Run the focused RED test**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests.FSM.GameLauncherBackgroundThrottleTests -testResults /private/tmp/gamelauncher-migration-red.xml -logFile /private/tmp/gamelauncher-migration-red.log
```

Expected: the inheritance assertion fails before production code is changed.

- [ ] **Step 3: Write the minimum production migration**

```csharp
public class GameLauncher : SingletonDontDestroy<GameLauncher>
{
    protected override void Awake()
    {
        base.Awake();
        if (!IsPrimaryInstance) return;
        LoadAndApplyConfig();
        VerifyConfigApplied();
    }

    private void Start()
    {
        if (!IsPrimaryInstance) return;
        // existing bootstrap and FSM initialization
    }

    protected override void OnDestroy()
    {
        if (IsPrimaryInstance)
        {
            Config = null;
            IsReady = false;
        }
        base.OnDestroy();
    }
}
```

Retain `EnsureCreatedForActiveScene()` unchanged and remove only the obsolete private `_instance` field and manual duplicate logic.

- [ ] **Step 4: Run focused GREEN verification**

Run the command from Step 2 with `/private/tmp/gamelauncher-migration-green.xml` and confirm all fixture tests pass.

- [ ] **Step 5: Run full verification and commit**

Run full EditMode and PlayMode suites. Restore only Unity-generated `.meta` and `Packages/packages-lock.json` rewrites. Require a clean `git diff --check`, review the diff, commit the migration and tests, then push normally to `gitee/master`.
