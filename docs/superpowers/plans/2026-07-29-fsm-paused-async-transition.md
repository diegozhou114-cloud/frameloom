# Deferred FSM Transitions During Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve valid Launch, Login, and Battle asynchronous completions during a background Pause and execute state transitions only after the application resumes.

**Architecture:** `FSMState<T>` passes its identity to `FSM<T>` when requesting a transition. The FSM immediately processes a current base state, retains the first valid request from the single suspended state, rejects every attempt to transition through the active overlay, and replays the retained request after `PopState`; Login and Battle use their existing cancellation identities instead of treating suspension as exit.

**Tech Stack:** Unity 2022.3.62f3c1, C#, NUnit, Unity Test Framework PlayMode/EditMode, UniTask, CodeGraph CLI, Git/Gitee.

## Global Constraints

- Scope is limited to the Unity client under `Assets/`; do not modify `Server/`.
- Support exactly one FSM overlay at a time; do not add nested-overlay scheduling.
- Preserve the existing transition table and emit Pause-to-resumed before resumed-to-target.
- Preserve cancellation after actual state exit; suspension must not cancel or restart asynchronous work.
- Do not implement YooAsset, AssetBundle, Addressables, or package-delivery behavior.
- Run Unity with exactly one editor process and without `-quit`; confirm zero Unity processes before and after every invocation.
- Treat all tests under `Assets/Tests/Runtime` as PlayMode tests.
- Revert Unity's unrelated rewrites of legacy `.meta` files and `Packages/packages-lock.json`; retain only files explicitly listed by a task.
- Before every push, run `git fetch gitee master`; stop and resolve normally if the local branch is behind or diverged.
- Never force-push. Push each independently verified repair commit directly to `gitee/master`.

---

### Task 1: Add Source-aware Deferred Transitions

**Files:**
- Modify: `Assets/Tests/Runtime/FSM/PauseResumeTests.cs`
- Modify: `Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs`
- Modify: `Assets/Scripts/FSM/Core/FSMState.cs`
- Modify: `Assets/Scripts/FSM/Core/FSM.cs`

**Interfaces:**
- Consumes: `FSM<T>.ChangeState(T, object)`, `PushState(T, object)`, `PopState(object)`, `FSMState<T>.Owner`, and `IBootPipeline.RunAsync(CancellationToken)`.
- Produces: `internal bool ChangeStateFrom(FSMState<T>, T, object)` while keeping the protected `FSMState<T>.ChangeState(T, object)` signature unchanged.

- [ ] **Step 1: Write behavior-first core Pause tests**

In `PauseResumeTests.cs`, expand the enum and add a target probe:

```csharp
private enum StateId { Battle, Pause, MainVillage, Login }

private sealed class StateProbe : FSMState<StateId>
{
    public StateProbe(StateId stateId) : base(stateId) { }
}
```

Add this helper to `BattleProbe`:

```csharp
public void Request(StateId target, object data = null) =>
    ChangeState(target, data);
```

Add this helper to `PauseProbe`:

```csharp
public void Request(StateId target) => ChangeState(target);
```

Add these four tests:

```csharp
[Test]
public void SuspendedSource_TransitionExecutesAfterResumeWithOriginalData()
{
    var battle = new BattleProbe();
    var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
    fsm.AddStates(battle, new PauseProbe(), new StateProbe(StateId.MainVillage));
    var transitions = new System.Collections.Generic.List<FSMTransition<StateId>>();
    fsm.OnStateChanged += transitions.Add;
    var data = new BattleContext { Seed = 73 };
    Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
    Assert.That(fsm.PushState(StateId.Pause), Is.True);

    battle.Request(StateId.MainVillage, data);

    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
    Assert.That(transitions.ConvertAll(item => item.NewState),
        Is.EqualTo(new[] { StateId.Battle, StateId.Pause }));
    Assert.That(fsm.PopState(), Is.True);
    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
    Assert.That(battle.ResumeCount, Is.EqualTo(1));
    Assert.That(battle.ExitCount, Is.EqualTo(1));
    Assert.That(transitions.ConvertAll(item => item.NewState), Is.EqualTo(new[]
    {
        StateId.Battle, StateId.Pause, StateId.Battle, StateId.MainVillage,
    }));
    Assert.That(transitions[3].Data, Is.SameAs(data));
}

[Test]
public void SuspendedSource_FirstValidTransitionWins()
{
    var battle = new BattleProbe();
    var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
    fsm.AddStates(battle, new PauseProbe(),
        new StateProbe(StateId.MainVillage), new StateProbe(StateId.Login));
    Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
    Assert.That(fsm.PushState(StateId.Pause), Is.True);

    battle.Request(StateId.MainVillage);
    battle.Request(StateId.Login);

    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
    Assert.That(fsm.PopState(), Is.True);
    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
}

[Test]
public void ActiveOverlay_RejectsBypassAndNestedPush()
{
    var battle = new BattleProbe();
    var pause = new PauseProbe();
    var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
    fsm.AddStates(battle, pause, new StateProbe(StateId.MainVillage));
    Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
    Assert.That(fsm.PushState(StateId.Pause), Is.True);

    Assert.That(fsm.ChangeState(StateId.MainVillage), Is.False);
    pause.Request(StateId.MainVillage);
    Assert.That(fsm.PushState(StateId.MainVillage), Is.False);
    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
    Assert.That(fsm.PopState(), Is.True);
    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Battle));
}

[Test]
public void ExitedSource_CannotMoveTheCurrentState()
{
    var battle = new BattleProbe();
    var fsm = new FSM<StateId> { TransitionGuard = (_, _) => true };
    fsm.AddStates(battle, new StateProbe(StateId.MainVillage),
        new StateProbe(StateId.Login));
    Assert.That(fsm.ChangeState(StateId.Battle), Is.True);
    Assert.That(fsm.ChangeState(StateId.MainVillage), Is.True);

    battle.Request(StateId.Login);

    Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.MainVillage));
}
```

Extend the existing `Clear_ExitsCurrentAndSuspendedStates` test immediately
before `fsm.Clear()`:

```csharp
battle.Request(StateId.MainVillage);
Assert.That(fsm.CurrentStateId, Is.EqualTo(StateId.Pause));
```

Register `new StateProbe(StateId.MainVillage)` in that test. After `Clear`, also
assert `fsm.HasCurrentState` is false and `fsm.PopState()` is false.

- [ ] **Step 2: Write the real asynchronous Launch regression test**

In `LaunchCancellationTests.cs`, add:

```csharp
private sealed class CompletingPipeline : IBootPipeline
{
    private readonly UniTaskCompletionSource<GameState?> _completion = new();

    public async UniTask<GameState?> RunAsync(CancellationToken token)
    {
        return await _completion.Task.AttachExternalCancellation(token);
    }

    public void Complete(GameState target) => _completion.TrySetResult(target);
}

private sealed class LoginProbe : FSMState<GameState>
{
    public LoginProbe() : base(GameState.Login) { }
}

private sealed class PauseProbe : FSMState<GameState>
{
    public PauseProbe() : base(GameState.Pause) { }
}

[UnityTest]
public System.Collections.IEnumerator
    LaunchState_PipelineCompletionDuringPause_TransitionsAfterResume()
{
    var pipeline = new CompletingPipeline();
    var fsm = new FSM<GameState> { TransitionGuard = (_, _) => true };
    fsm.AddStates(new LaunchState(pipeline), new LoginProbe(), new PauseProbe());
    Assert.That(fsm.ChangeState(GameState.Launch), Is.True);
    Assert.That(fsm.PushState(GameState.Pause), Is.True);

    pipeline.Complete(GameState.Login);
    yield return null;

    Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.Pause));
    Assert.That(fsm.PopState(), Is.True);
    Assert.That(fsm.CurrentStateId, Is.EqualTo(GameState.Login));
}
```

- [ ] **Step 3: Run focused RED and retain behavioral evidence**

Confirm no editor is active:

```bash
pgrep -fl '/Unity.app/Contents/MacOS/Unity'
```

Expected: no output. Then run exactly one editor without `-quit`:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-master-merge \
  -runTests -testPlatform PlayMode \
  -testFilter 'ZWebGLFrameWork.Runtime.Tests.FSM;ZWebGLFrameWork.Runtime.Tests.LoginStateTests' \
  -testResults /private/tmp/zweb-fsm-paused-transition-red.xml \
  -logFile /private/tmp/zweb-fsm-paused-transition-red.log
```

Expected: the new tests compile and fail because a suspended or exited source
can immediately transition the current state. The Launch test must show Login
became current before `PopState`. A compilation error, missing XML, unrelated
failure, or remaining Unity process is not accepted RED. Wait for natural exit
and confirm zero Unity processes again.

- [ ] **Step 4: Preserve source identity in the state helper**

Keep the protected helper's signature and replace only its body in
`FSMState<T>`:

```csharp
protected void ChangeState(T targetState, object data = null)
{
    Owner?.ChangeStateFrom(this, targetState, data);
}
```

- [ ] **Step 5: Implement the minimal one-slot FSM contract**

In `FSM<T>`, replace `TransitionRequest` and add the deferred field:

```csharp
private TransitionRequest? _deferredTransition;

private readonly struct TransitionRequest
{
    public FSMState<T> Source { get; }
    public T StateId { get; }
    public object Data { get; }

    public TransitionRequest(FSMState<T> source, T stateId, object data)
    {
        Source = source;
        StateId = stateId;
        Data = data;
    }
}
```

Replace public `ChangeState` and move its existing queue loop into
`QueueTransition`:

```csharp
public bool ChangeState(T stateId, object data = null)
{
    if (_suspendedStates.Count > 0) return false;
    return QueueTransition(new TransitionRequest(null, stateId, data));
}

internal bool ChangeStateFrom(FSMState<T> source, T stateId, object data = null)
{
    if (source == null || !ReferenceEquals(source.Owner, this)) return false;

    if (ReferenceEquals(_currentState, source))
    {
        if (_suspendedStates.Count > 0) return false;
        return QueueTransition(new TransitionRequest(source, stateId, data));
    }

    if (_suspendedStates.Count != 1 ||
        !ReferenceEquals(_suspendedStates.Peek(), source))
        return false;
    if (!_states.TryGetValue(stateId, out FSMState<T> target))
    {
        Debug.LogError($"FSM: 状态 {stateId} 未注册。请先调用 AddState。");
        return false;
    }
    if (ReferenceEquals(target, source) ||
        !CanTransitionTo(source.StateId, stateId) ||
        _deferredTransition.HasValue)
        return false;

    _deferredTransition = new TransitionRequest(source, stateId, data);
    return true;
}

private bool QueueTransition(TransitionRequest request)
{
    if (_isTransitioning)
    {
        _pendingTransitions.Enqueue(request);
        return true;
    }

    bool firstResult = false;
    bool first = true;
    _pendingTransitions.Enqueue(request);
    _isTransitioning = true;
    try
    {
        while (_pendingTransitions.Count > 0)
        {
            bool result = ApplyTransition(_pendingTransitions.Dequeue());
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
```

Replace `ApplyTransition` with:

```csharp
private bool ApplyTransition(TransitionRequest request)
{
    if (request.Source != null &&
        !ReferenceEquals(_currentState, request.Source))
        return false;
    if (!_states.TryGetValue(request.StateId, out FSMState<T> newState))
    {
        Debug.LogError($"FSM: 状态 {request.StateId} 未注册。请先调用 AddState。");
        return false;
    }
    if (_currentState == newState) return false;

    bool hasOldState = _currentState != null;
    if (hasOldState && !CanTransitionTo(_currentState.StateId, request.StateId))
        return false;

    FSMState<T> oldState = _currentState;
    oldState?.OnExit();
    _previousState = oldState;
    T oldStateId = oldState != null ? oldState.StateId : default;
    _currentState = newState;
    newState.OnEnter(request.Data);
    OnStateChanged?.Invoke(new FSMTransition<T>(
        hasOldState, oldStateId, request.StateId, request.Data));
    return true;
}
```

Make `PushState` reject a second overlay:

```csharp
if (_suspendedStates.Count > 0 || _currentState == null ||
    !_states.TryGetValue(stateId, out FSMState<T> overlay))
    return false;
```

At the end of `PopState`, after the existing transition event, add:

```csharp
if (_deferredTransition.HasValue &&
    ReferenceEquals(_deferredTransition.Value.Source, resumed))
{
    TransitionRequest request = _deferredTransition.Value;
    _deferredTransition = null;
    QueueTransition(request);
}
return true;
```

In `RemoveState`, clear the slot before detaching a matching source:

```csharp
if (_deferredTransition.HasValue &&
    ReferenceEquals(_deferredTransition.Value.Source, state))
    _deferredTransition = null;
```

In `Clear`, set `_deferredTransition = null` next to the pending queue reset.

- [ ] **Step 6: Run focused GREEN and inspect the exact scope**

After a zero-process check, run the same FSM namespace filter with:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-master-merge \
  -runTests -testPlatform PlayMode \
  -testFilter ZWebGLFrameWork.Runtime.Tests.FSM \
  -testResults /private/tmp/zweb-fsm-paused-transition-core-green.xml \
  -logFile /private/tmp/zweb-fsm-paused-transition-core-green.log
```

Expected: every Runtime FSM namespace test passes. Confirm natural exit and
zero Unity processes. Revert only unrelated Unity-generated rewrites, then run:

```bash
codegraph sync
codegraph explore "ChangeStateFrom deferred FSM transition callers and impact"
git diff --check
git status --short
```

Expected: only the four Task 1 files are changed.

- [ ] **Step 7: Commit and push the core repair**

```bash
git add Assets/Scripts/FSM/Core/FSM.cs \
  Assets/Scripts/FSM/Core/FSMState.cs \
  Assets/Tests/Runtime/FSM/PauseResumeTests.cs \
  Assets/Tests/Runtime/FSM/LaunchCancellationTests.cs
git commit -m "fix(client): defer suspended FSM transitions"
git fetch gitee master
git rev-list --left-right --count master...gitee/master
git push gitee master:master
```

Expected before push: `1 0`. If the right count is non-zero, resolve without a
force-push and rerun the focused gate before pushing.

---

### Task 2: Keep Login and Battle Operations Alive Through Pause

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`
- Modify: `Assets/Scripts/FSM/Game/States/BattleState.cs`

**Interfaces:**
- Consumes: Task 1's source-aware state helper, `LoginState._loginCts`, and `BattleState._battleLifetime`.
- Produces: paused Login and Battle completions that remain valid until their actual state exits.

- [ ] **Step 1: Remove only the incorrect active-frame checks**

In `LoginState.OnGuestLogin`, replace both post-await guards with:

```csharp
if (token.IsCancellationRequested) return;
```

The first guard follows `GuestLoginAsync`; the second is inside the general
exception handler. Keep request cancellation, session assignment, error UI,
and transition targets unchanged.

In `BattleState.SafeStartBattle`, replace the success guard with:

```csharp
if (lifetime.IsCancellationRequested ||
    !ReferenceEquals(_battleLifetime, lifetime))
    return;
```

Replace the general exception condition with:

```csharp
if (ReferenceEquals(_battleLifetime, lifetime) &&
    !lifetime.IsCancellationRequested)
    ChangeState(GameState.MainVillage);
```

Keep reward commit order, UI calls, cancellation ownership, network behavior,
combat simulation, and target states unchanged.

- [ ] **Step 2: Run the focused FSM regression gate**

Confirm zero Unity processes, then run one editor without `-quit`:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-master-merge \
  -runTests -testPlatform PlayMode \
  -testFilter 'ZWebGLFrameWork.Runtime.Tests.FSM;ZWebGLFrameWork.Runtime.Tests.LoginStateTests' \
  -testResults /private/tmp/zweb-fsm-paused-transition-focused-green.xml \
  -logFile /private/tmp/zweb-fsm-paused-transition-focused-green.log
```

Expected: all Runtime FSM namespace tests and `LoginStateTests` pass, including
Launch completion during Pause and existing exit-cancellation coverage.
Confirm natural exit and zero Unity processes.

- [ ] **Step 3: Review, commit, and push the state integration**

```bash
codegraph sync
codegraph explore "LoginState BattleState async completion cancellation and ChangeState flow"
git diff --check
git diff -- Assets/Scripts/FSM/Game/States/LoginState.cs Assets/Scripts/FSM/Game/States/BattleState.cs
git add Assets/Scripts/FSM/Game/States/LoginState.cs Assets/Scripts/FSM/Game/States/BattleState.cs
git commit -m "fix(client): preserve paused async state completions"
git fetch gitee master
git rev-list --left-right --count master...gitee/master
git push gitee master:master
```

Expected before push: `1 0`. Resolve remote movement normally and rerun the
focused gate before any push.

---

### Task 3: Run Full Gates and Final Review

**Files:**
- Verify only; no planned production or test edits.

**Interfaces:**
- Consumes: both pushed implementation commits and all Unity client tests.
- Produces: complete test evidence, clean remote integrity, zero Unity processes, and review findings.

- [ ] **Step 1: Run complete PlayMode**

After confirming zero Unity processes, run exactly one editor without `-quit`:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-master-merge \
  -runTests -testPlatform PlayMode \
  -testResults /private/tmp/zweb-fsm-paused-transition-full-playmode.xml \
  -logFile /private/tmp/zweb-fsm-paused-transition-full-playmode.log
```

Expected: zero failed PlayMode tests, natural exit, and zero Unity processes.
Restore only unrelated Unity-generated serialization noise.

- [ ] **Step 2: Run complete EditMode**

After another zero-process check, run one editor without `-quit`:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/diegozhou/Documents/unityframework/zweb-master-merge \
  -runTests -testPlatform EditMode \
  -testResults /private/tmp/zweb-fsm-paused-transition-full-editmode.xml \
  -logFile /private/tmp/zweb-fsm-paused-transition-full-editmode.log
```

Expected: zero failed EditMode tests, natural exit, and zero Unity processes.
Restore only unrelated Unity-generated serialization noise.

- [ ] **Step 3: Close with review and remote-integrity evidence**

```bash
git diff --check
git status --short
git fetch gitee master
git rev-list --left-right --count master...gitee/master
git log -6 --oneline --decorate
pgrep -fl '/Unity.app/Contents/MacOS/Unity'
```

Review from the parent of the Task 1 implementation commit through `HEAD` for
stale suspended entries, replacement of the first request, event-order changes,
re-entrant source validation, cancellation regressions, scope leakage, and
missing tests.

Expected: clean working tree, `0 0` divergence from `gitee/master`, zero Unity
processes, no unresolved review finding, and both evidence XMLs with zero
failures. Any finding starts a focused RED/GREEN repair cycle and a new small
commit instead of being waived.
