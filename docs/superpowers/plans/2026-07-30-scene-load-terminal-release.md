# Scene Load Terminal Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure cancellation or timeout never leaves a Unity scene load paused at the activation gate.

**Architecture:** `SceneLoadRunner` remains the sole owner of the `ISceneLoadOperation` activation gate. On a terminal cancellation or timeout it releases `AllowSceneActivation` before returning the existing failure result. It does not wait for an operation Unity cannot cancel, report completion progress, publish events, or decide a fallback scene; `LoadingState` continues to handle non-success results through its existing failure path.

**Tech Stack:** Unity 2022.3.62f3c1, C#, Cysharp UniTask, NUnit PlayMode tests.

## Global Constraints

- Client framework only; do not modify `Server/`.
- Do not add or configure YooAsset; existing resource TODO markers remain deferred.
- Preserve `SceneLoadStatus.Cancelled` and `SceneLoadStatus.Timeout` for terminal callers.
- Run exactly one Unity batch editor at a time and confirm it has exited before later invocation.
- Use TDD: observe the focused tests fail before changing production code, then rerun focused and complete EditMode/PlayMode suites.
- Commit and normal-push the verified small repair to `gitee/master`; never force-push.

---

### Task 1: Release the Activation Gate on Terminal Results

**Files:**
- Modify: `Assets/Tests/Runtime/FSM/SceneLoadingTests.cs`
- Modify: `Assets/Scripts/Core/Scene/SceneLoadRunner.cs`

**Interfaces:**
- Consumes: `ISceneLoadOperation.AllowSceneActivation`, `CancellationToken`, and `SceneLoadStatus`.
- Produces: `SceneLoadResult(Cancelled)` or `SceneLoadResult(Timeout)` after the gate is released; success progress remains reported only by the normal completion path.

- [x] **Step 1: Write failing PlayMode expectations**

Change `SceneLoadRunner_CancelBeforeActivation_DoesNotActivate` to assert that a cancelled operation at progress `0.9` has `AllowSceneActivation == true`. Add a cancellation case before progress `0.9` and an activation-phase timeout assertion, both requiring the gate to be true after their terminal result.

- [x] **Step 2: Run the focused tests and verify RED**

Run the filtered `ZWebGLFrameWork.Runtime.Tests.FSM.SceneLoadingTests` PlayMode suite. The three terminal-gate assertions must fail because the existing runner returns before changing `AllowSceneActivation`.

- [x] **Step 3: Implement the minimal terminal release**

Add a private `TerminalResult` helper to `SceneLoadRunner`:

```csharp
private static SceneLoadResult TerminalResult(
    ISceneLoadOperation operation,
    SceneLoadStatus status,
    string error = null)
{
    if (!operation.AllowSceneActivation)
        operation.AllowSceneActivation = true;
    return new SceneLoadResult(status, error);
}
```

Use it for both cancellation checks and the timeout branch. Do not call `reportProgress(1f)` from this helper and do not alter `LoadingState`.

- [x] **Step 4: Run focused tests and verify GREEN**

Run the same filtered PlayMode suite. It must pass while preserving the existing `LoadingState` failure-only tests and the normal success result.

- [x] **Step 5: Run complete regression and review**

Run complete EditMode and PlayMode suites serially, restore only generated `.meta` files and `Packages/packages-lock.json`, then have an independent reviewer check that terminal status is preserved and no success behavior was added.

- [ ] **Step 6: Commit and push**

Stage only the plan, test, and runner files. Commit as `fix(client): release terminal scene loads`, then normal-push `master` to `gitee`.
