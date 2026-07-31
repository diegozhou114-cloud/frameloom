# Business Resource Hot-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate fixed framework resources from replaceable business resources and introduce a client-only hot-update boundary without adding Huatuo, YooAsset, or server code.

**Architecture:** `ResourceManager` owns fixed framework and replaceable business provider/cache pairs. Layer-aware load APIs route by `ResourceLayer`, while existing unlayered calls remain framework loads. An `IBusinessContentUpdater` produces an explicit result; launch continues locally when none is configured and stops only for a configured updater failure.

**Tech Stack:** Unity 2022.3.62f3c1, C#, UniTask, NUnit PlayMode tests.

## Global Constraints

- Framework content always uses the local `ResourcesProvider`; no public API replaces it.
- Only `ResourceLayer.Business` has a provider replacement API.
- Do not add YooAsset, Huatuo, AssetBundle, Addressables, networking, server code, or download protocols.
- Preserve serialized `AppConfig.EnableHotUpdate` and `ResourceProviderType` values.
- Use exactly one Unity batch process at a time and terminate it after every test run.
- Remove Unity-generated metadata or package-lock churn before each commit unless deliberately changed.
- Commit and push each independently passing task to `gitee/master`.

---

### Task 1: Layered Resource Routing

**Files:**
- Create: `Assets/Scripts/Core/Resource/ResourceLayer.cs`
- Modify: `Assets/Scripts/Core/Resource/ResourceManager.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:94-99,208-213`
- Modify: `Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs`

**Interfaces:**
- Consumes: `IResourceProvider.Load`, `LoadAsync`, `Unload`, and `UnloadUnused`.
- Produces: `ResourceLayer`, `Load<T>(ResourceLayer, string)`, `LoadAsync<T>(ResourceLayer, string)`, `Unload(ResourceLayer, string)`, and `SetBusinessProvider(IResourceProvider)`.

- [ ] **Step 1: Write the failing runtime tests**

Replace the legacy generic-provider-switch tests with independently named tests that use two distinct `FakeProvider` instances:

```csharp
[Test]
public async Task ResourceManager_BusinessLoadsUseBusinessProvider()
{
    var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
    var businessAsset = ScriptableObject.CreateInstance<AssetMarker>();
    try
    {
        var businessProvider = new FakeProvider(businessAsset);
        Assert.That(manager.Init(), Is.True);
        manager.SetBusinessProvider(businessProvider);

        Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
            Is.SameAs(businessAsset));
    }
    finally { Object.DestroyImmediate(businessAsset); }
}

[Test]
public async Task ResourceManager_BusinessProviderSwapKeepsFrameworkProvider()
{
    var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
    var firstBusinessAsset = ScriptableObject.CreateInstance<AssetMarker>();
    var secondBusinessAsset = ScriptableObject.CreateInstance<AssetMarker>();
    try
    {
        Assert.That(manager.Init(), Is.True);
        IResourceProvider frameworkProvider = manager.FrameworkProvider;
        var firstProvider = new FakeProvider(firstBusinessAsset);
        var secondProvider = new FakeProvider(secondBusinessAsset);
        manager.SetBusinessProvider(firstProvider);
        Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
            Is.SameAs(firstBusinessAsset));

        manager.SetBusinessProvider(secondProvider);

        Assert.That(firstProvider.UnloadUnusedCalled, Is.True);
        Assert.That(manager.FrameworkProvider, Is.SameAs(frameworkProvider));
        Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
            Is.SameAs(secondBusinessAsset));
    }
    finally
    {
        Object.DestroyImmediate(firstBusinessAsset);
        Object.DestroyImmediate(secondBusinessAsset);
    }
}
```

Add a reflection assertion that `ResourceManager` has no public instance `SetFrameworkProvider` method and no writable public `FrameworkProvider` property.

- [ ] **Step 2: Run the focused test to verify it fails**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests.Core.ResourceManagerProviderTests -testResults /private/tmp/business-resource-routing-red.xml -logFile /private/tmp/business-resource-routing-red.log
```

Expected: `FAIL` because `ResourceLayer`, `Init()`, `SetBusinessProvider`, and the layer-aware load APIs do not exist.

- [ ] **Step 3: Write the minimal resource-routing implementation**

Create the layer enum:

```csharp
namespace ZWebGLFrameWork.Core
{
    public enum ResourceLayer
    {
        Framework,
        Business,
    }
}
```

Refactor `ResourceManager` to keep private `_frameworkProvider` and `_businessProvider` fields, both initialized to `ResourcesProvider`. Remove the custom-provider `Init` parameter plus public `Provider` and `SetProvider` members. Add:

```csharp
public void SetBusinessProvider(IResourceProvider newProvider)
{
    if (newProvider == null) throw new ArgumentNullException(nameof(newProvider));
    // Clear only business cache entries, increment the business version,
    // then release unused assets on the previous business provider.
}

public UniTask<T> LoadAsync<T>(ResourceLayer layer, string path)
    where T : UnityEngine.Object;
public T Load<T>(ResourceLayer layer, string path)
    where T : UnityEngine.Object;
```

Use a private cache key that contains both `ResourceLayer` and `path`; retain provider-version checking separately per layer so a replaced business provider cannot cache an in-flight old result. Make the existing unlayered load and unload methods delegate to `ResourceLayer.Framework`. Make `ClearCache` clear both layers and call `UnloadUnused` on both providers, once each when they are distinct. Update `LaunchState` to call parameterless `ResourceManager.Init()` and retain its existing legacy enum warning in `CreateResourceProvider` only as a configuration validation/logging method.

- [ ] **Step 4: Run the focused test to verify it passes**

Run the Step 2 command again.

Expected: `PASS`, including the business-routing, replacement, in-flight invalidation, and framework immutability tests.

- [ ] **Step 5: Run affected regression tests**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests.FSM.LaunchCancellationTests -testResults /private/tmp/business-resource-launch-regression.xml -logFile /private/tmp/business-resource-launch-regression.log
```

Expected: `PASS`, including legacy `ResourceProviderType` fallback coverage.

- [ ] **Step 6: Commit and push the independently passing task**

```bash
git add Assets/Scripts/Core/Resource/ResourceLayer.cs Assets/Scripts/Core/Resource/ResourceManager.cs Assets/Scripts/FSM/Game/States/LaunchState.cs Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs
git commit -m "feat: separate framework and business resources"
git push gitee master
```

### Task 2: Business Update Result and Launch Policy

**Files:**
- Create: `Assets/Scripts/Core/Resource/BusinessContentUpdate.cs`
- Modify: `Assets/Scripts/Core/Resource/ResourceManager.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LaunchState.cs:139-155`
- Modify: `Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs`
- Modify: `Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs`

**Interfaces:**
- Consumes: `ResourceManager.SetBusinessProvider(IResourceProvider)` from Task 1 and `AppConfig.EnableHotUpdate`.
- Produces: `IBusinessContentUpdater.PrepareAsync(CancellationToken)`, `BusinessContentUpdateResult`, and `ResourceManager.PrepareBusinessContentAsync(CancellationToken)`.

- [ ] **Step 1: Write the failing runtime tests**

Add a fake updater that returns a supplied result, then add these tests:

```csharp
[Test]
public async Task ResourceManager_DefaultBusinessUpdaterReportsNotConfigured()
{
    var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
    Assert.That(manager.Init(), Is.True);

    BusinessContentUpdateResult result =
        await manager.PrepareBusinessContentAsync(CancellationToken.None);

    Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.NotConfigured));
}

[Test]
public async Task ResourceManager_UpdatedBusinessContentInstallsOnlyBusinessProvider()
{
    var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
    var businessAsset = ScriptableObject.CreateInstance<AssetMarker>();
    try
    {
        Assert.That(manager.Init(), Is.True);
        manager.SetBusinessContentUpdater(new FakeBusinessUpdater(
            BusinessContentUpdateResult.Updated(new FakeProvider(businessAsset))));

        BusinessContentUpdateResult result =
            await manager.PrepareBusinessContentAsync(CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
        Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "content/item"),
            Is.SameAs(businessAsset));
        Assert.That(manager.FrameworkProvider, Is.TypeOf<ResourcesProvider>());
    }
    finally { Object.DestroyImmediate(businessAsset); }
}
```

In `LaunchCancellationTests`, reflect a private static launch policy method and add:

```csharp
[Test]
public void LaunchState_BusinessContentUpdatePolicy_OnlyStopsForFailure()
{
    MethodInfo policy = typeof(LaunchState).GetMethod("CanContinueAfterBusinessUpdate",
        BindingFlags.Static | BindingFlags.NonPublic);

    Assert.That((bool)policy.Invoke(null, new object[]
        { BusinessContentUpdateResult.NotConfigured() }), Is.True);
    Assert.That((bool)policy.Invoke(null, new object[]
        { BusinessContentUpdateResult.NoUpdate() }), Is.True);
    Assert.That((bool)policy.Invoke(null, new object[]
        { BusinessContentUpdateResult.Failed("unavailable") }), Is.False);
}
```

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests.Core.ResourceManagerProviderTests -testResults /private/tmp/business-update-contract-red.xml -logFile /private/tmp/business-update-contract-red.log
```

Expected: `FAIL` because the updater contract and preparation API do not exist.

- [ ] **Step 3: Write the minimal update-boundary implementation**

Create these public contract types in `BusinessContentUpdate.cs`:

```csharp
public enum BusinessContentUpdateStatus
{
    NotConfigured,
    NoUpdate,
    Updated,
    Failed,
}

public interface IBusinessContentUpdater
{
    UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token);
}
```

`BusinessContentUpdateResult` must expose `Status`, `Message`, and an `IResourceProvider BusinessProvider` only for `Updated`. Provide static factories `NotConfigured`, `NoUpdate`, `Updated(IResourceProvider)`, and `Failed(string)`. An internal default updater returns `NotConfigured` and performs no I/O.

Add `SetBusinessContentUpdater(IBusinessContentUpdater updater)` and `PrepareBusinessContentAsync(CancellationToken token)` to `ResourceManager`. The latter calls the updater once, returns non-`Updated` results unchanged, turns `Updated` without a provider into `Failed`, and otherwise calls `SetBusinessProvider` before returning `Updated`.

Make `LaunchState.HandleHotUpdate` asynchronous. When disabled it logs the existing skip message. When enabled it awaits `PrepareBusinessContentAsync`; it logs an explicit `TODO(YooAsset)` plus `TODO(Huatuo)` warning for `NotConfigured`, continues for `NoUpdate`/`Updated`, and uses `PublishError("BusinessHotUpdate", result.Message, true)` then returns `false` for `Failed`. It must not create a server request, load an assembly, or invoke legacy downloading code.

Add this private, side-effect-free launch policy method and use it before publishing an error:

```csharp
private static bool CanContinueAfterBusinessUpdate(BusinessContentUpdateResult result)
{
    return result.Status != BusinessContentUpdateStatus.Failed;
}
```

- [ ] **Step 4: Run focused tests to verify they pass**

Run the Step 2 command again, then run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests.FSM.LaunchCancellationTests -testResults /private/tmp/business-update-launch-green.xml -logFile /private/tmp/business-update-launch-green.log
```

Expected: both commands report `PASS`.

- [ ] **Step 5: Run the complete client runtime suite**

Run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testFilter ZWebGLFrameWork.Runtime.Tests -testResults /private/tmp/business-update-runtime.xml -logFile /private/tmp/business-update-runtime.log
```

Expected: `PASS` with no compilation errors.

- [ ] **Step 6: Inspect generated changes, commit, push, and verify remote**

Run:

```bash
git status --short
git diff --check
git add Assets/Scripts/Core/Resource/BusinessContentUpdate.cs Assets/Scripts/Core/Resource/ResourceManager.cs Assets/Scripts/FSM/Game/States/LaunchState.cs Assets/Tests/Runtime/Core/ResourceManagerProviderTests.cs Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs
git commit -m "feat: add business content update boundary"
git push gitee master
git ls-remote gitee refs/heads/master
```

Expected: no unintended Unity-generated files are staged, the commit succeeds, and the remote SHA equals local `HEAD`.
