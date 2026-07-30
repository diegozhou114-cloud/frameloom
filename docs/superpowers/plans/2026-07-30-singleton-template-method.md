# Singleton Template Method Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `SingletonDontDestroy<T>` initialization into a base-controlled template method so duplicate instances cannot run manager initialization and production subclasses cannot accidentally bypass the singleton lifecycle.

**Architecture:** `SingletonDontDestroy<T>.Awake` remains the sole Unity lifecycle entry for this base hierarchy. It establishes or rejects the instance, then invokes a protected `OnSingletonAwake` hook only for the primary instance. Concrete singleton types move only their existing primary-only initialization into that hook; non-initialization callbacks such as `Start`, `OnApplicationPause`, and `OnDestroy` retain their current behavior and guards.

**Tech Stack:** Unity 2022.3.62f3c1, C#, NUnit PlayMode tests, Cysharp UniTask.

## Global Constraints

- Client framework only; do not modify `Server/`.
- Do not add or configure YooAsset; existing resource TODO markers remain deferred.
- Run exactly one Unity batch editor at a time and confirm it has exited before any later Unity invocation.
- Use TDD: observe focused tests fail before changing production code, then rerun focused and complete EditMode/PlayMode suites.
- Commit and normal-push the verified small repair to `gitee/master`; never force-push.

---

### Task 1: Prove and Enforce the Lifecycle Contract

**Files:**
- Modify: `Assets/Tests/Runtime/Core/SingletonDontDestroyInitializationTests.cs`
- Modify: `Assets/Scripts/Core/Singleton/SingletonDontDestroy.cs`

**Interfaces:**
- Consumes: `SingletonDontDestroy<T>` Unity lifecycle behavior.
- Produces: `protected virtual void OnSingletonAwake()`; it is invoked once for the primary instance only.

- [x] **Step 1: Write a failing production-architecture test**

Add this reflection assertion without changing the existing probe yet:

```csharp
[Test]
public void ProductionDontDestroySingletons_DoNotDeclareAwake()
{
    foreach (Type type in ProductionDontDestroySingletonTypes())
    {
        Assert.That(type.GetMethod("Awake", BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            Is.Null, type.FullName + " must use OnSingletonAwake");
    }
}
```

`ProductionDontDestroySingletonTypes` filters the runtime assembly to concrete, direct subclasses of `SingletonDontDestroy<>`, excluding test types. The test must fail while production subclasses still declare `Awake`.

- [x] **Step 2: Run the focused test and verify RED**

Run one PlayMode batch filtered to `ZWebGLFrameWork.Runtime.Tests.Core.SingletonDontDestroyInitializationTests`.

Expected: the reflection assertion reports the existing production `Awake` declarations. This is a normal failed assertion, not a compiler error from an API that does not exist yet.

- [x] **Step 3: Migrate the base and all lifecycle overrides atomically**

Changing the base `Awake` contract makes existing derived `override Awake` declarations invalid C#. Therefore this step must include the production migration in the same compilation unit: first replace the base contract, then replace all seven overrides and the test probe with `OnSingletonAwake`. Do not run a Unity test between those edits.

In `SingletonDontDestroy<T>`, replace the virtual `Awake` contract with a non-overridable lifecycle method that preserves duplicate destruction and primary registration, then calls the hook:

```csharp
protected void Awake()
{
    if (_instance != null && _instance != this)
    {
        Destroy(gameObject);
        return;
    }

    _instance = this as T;
    DontDestroyOnLoad(gameObject);
    OnSingletonAwake();
}

protected virtual void OnSingletonAwake() { }
```

Keep `IsPrimaryInstance` because existing non-`Awake` callbacks use it. Remove the deferred template-method TODO because this task implements it.

- [x] **Step 4: Run the focused test and verify GREEN**

Run the same PlayMode filter.

Expected: the primary probe hook runs once, a duplicate hook does not run, and the production architecture assertion is green.

### Task 2: Record and Statistically Check the Completed Migration

**Files:**
- Modify: `docs/superpowers/specs/2026-07-29-singleton-duplicate-initialization-design.md`

**Interfaces:**
- Consumes: the Task 1 completed `OnSingletonAwake` migration.
- Produces: an up-to-date design record and static evidence that no production singleton directly declares `Awake`.

- [x] **Step 1: Update the design record**

Modify `docs/superpowers/specs/2026-07-29-singleton-duplicate-initialization-design.md` to mark Option B implemented and record the non-overridable lifecycle entry plus the architecture test. Do not leave a stale deferred TODO.

- [x] **Step 2: Run static checks**

Run:

```bash
rg -n "protected override void Awake\\(\\)" Assets/Scripts --glob '*.cs'
git diff --check
```

Expected: no `Awake` override remains in a `SingletonDontDestroy<T>` subclass; whitespace check exits zero.

### Task 3: Regress, Review, and Deliver

**Files:**
- Verify: all Task 1 and Task 2 files.

- [x] **Step 1: Run full Unity suites serially**

Run complete EditMode and PlayMode suites in separate Unity batch invocations, checking that no Unity editor process remains after each. Preserve source edits; restore only generated `.meta` and `Packages/packages-lock.json` changes if Unity creates them.

- [ ] **Step 2: Review the final diff**

Verify no direct production `Awake` override remains, the hook executes after primary registration, duplicate instances return before the hook, and no unrelated behavior changed.

- [ ] **Step 3: Commit and push**

Stage only this migration's production code, test, specification, and plan files. Commit with:

```bash
git commit -m "refactor(client): centralize singleton initialization"
git push gitee master
```
