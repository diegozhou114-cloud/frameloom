# Deferred FSM Transitions During Pause Design

**Date:** 2026-07-29
**Status:** Approved

## Goal

Keep the application in `Pause` while an asynchronous operation owned by the
suspended Launch, Login, or Battle state completes. A valid state transition
requested by that suspended state must execute after the application returns
to the foreground, without losing the result, bypassing transition rules, or
leaving a stale suspended state that can later be revived.

## Observed Failure

`FSM<T>.PushState` keeps the previous state alive in `_suspendedStates` and
marks it inactive. State-owned asynchronous work therefore continues during a
background pause.

Today `FSMState<T>.ChangeState` loses the identity of the requesting state and
calls the same public transition method used by external callers. While Pause
is current, this has two unsafe outcomes:

- the Pause transition rule rejects the target, permanently dropping the
  completed asynchronous result; or
- the Pause transition rule accepts the target, exits Pause directly, and
  leaves `_suspendedStates` populated so an obsolete state can be resumed.

`LoginState` and `BattleState` additionally treat `IsActive == false` as if the
state had exited. A suspended state is inactive but its existing operation
token remains live, so both states can discard a valid completion and remain
stuck after resume.

## Scope

This repair covers:

- the generic FSM transition request and overlay stack contract;
- the state helper that submits source-owned transitions;
- Launch, Login, and Battle asynchronous completion behavior; and
- focused PlayMode regression coverage for delayed transitions, request
  ordering, stale-source rejection, and public transition protection.

The change does not alter server code, network protocols, combat simulation,
scene-loading cancellation, UI stack ownership, resource providers, or the
YooAsset/AssetBundle plan. YooAsset and package delivery remain deferred work.

## Considered Approaches

### A. Source-aware deferred transition in the generic FSM (selected)

`FSMState<T>` submits its own identity with every transition request. The FSM
can distinguish a current source, a suspended source, and a stale source. It
executes a current source immediately, retains one valid request from a
suspended source, and rejects a stale source.

This centralizes overlay correctness, preserves the existing state API, and
fixes every asynchronous state without duplicating pending-target fields.

### B. Pending targets inside Launch, Login, and Battle

Each game state could retain its own target and replay it from `OnResume`.
This avoids changing the generic FSM, but duplicates ordering, validation, and
cleanup logic in every asynchronous state. New states could easily omit it.

### C. Cancel on suspend and restart on resume

Suspending a state could cancel its lifetime and restart its work on resume.
This is unsafe for non-idempotent boot, authentication, reward, and network
operations and would make pause duration affect application behavior.

## Selected Design

### Source-aware state requests

The protected `FSMState<T>.ChangeState` helper passes `this` to an internal FSM
entry point together with the target and data. The request has three outcomes:

1. If the source is current and no overlay is active, process it through the
   existing queued, re-entrant-safe transition path.
2. If the source is the single suspended state, validate the source-to-target
   rule and retain it without changing the current overlay.
3. If the source is neither current nor suspended, reject it as stale.

The application contract supports one overlay at a time. `PushState` returns
`false` when a state is already suspended. This matches the single global
Pause use case and avoids inventing scheduling semantics for nested overlays.

The public `FSM<T>.ChangeState` remains the synchronous external entry point.
When an overlay is active, it returns `false` instead of transitioning through
the overlay. A transition requested by the overlay state is rejected for the
same reason. Any caller that intentionally wants to leave an overlay must first
use `PopState`. This prevents both public and state-owned callers from
orphaning the suspended state.

### Deferred request ownership and ordering

The FSM retains at most one deferred transition, including its source, target,
and data. The first valid request wins. Later requests received before the
first request executes return `false`.

First-wins matches foreground behavior: the first accepted transition exits
the source state immediately, so later callbacks from that lifetime cannot
choose another target. A single slot is sufficient for the application's
single Pause overlay and avoids introducing a general overlay scheduler.

### Resume flow

`PopState` keeps its existing lifecycle and event order:

1. exit the overlay;
2. restore and call `OnResume` on the suspended state;
3. publish the overlay-to-resumed transition event; and
4. if the retained request belongs to that resumed state, clear the slot and
   submit it through the normal transition path.

The request is revalidated when executed. A rule that is no longer valid is
rejected normally. Clearing the slot before execution makes re-entrant
transitions deterministic and prevents replay.

`Clear` always discards the deferred request. Removing its source also discards
the request. No deferred request survives FSM teardown or source removal.

### Suspended asynchronous lifetime

No second state-lifetime API is added. `FSMState<T>.OnSuspend` deliberately
keeps the base lifetime token alive, while `OnExit` cancels it. Login and
Battle already have more specific operation tokens with the same ownership:
they remain live during suspension and are cancelled on exit. Those existing
tokens distinguish a valid paused completion from an obsolete completion.

- `LaunchState` already routes its late targets through the state helper, so
  the source-aware core behavior defers them without local state.
- `LoginState` relies on its `_loginCts` cancellation after its await rather
  than `IsActive`. A completion from a suspended Login remains valid; a
  completion from an exited Login remains ignored.
- `BattleState` uses its existing lifetime identity and cancellation checks
  without `IsActive`. A suspended battle can commit its one valid result,
  while an asynchronous failure requests MainVillage through the deferred
  source-aware transition path.

This change does not restart asynchronous work on resume and does not make
Pause a permitted substitute source for Launch or Login transitions.

## Return Values and Events

- The protected state helper keeps its existing `void` signature. Invalid,
  stale, and second requests do not change state or replace the retained slot.
- A public transition returns `false` while any state is suspended.
- Accepting a deferred request emits no state-change event because the current
  state remains Pause.
- Resume emits the existing Pause-to-source event, followed by the normal
  source-to-target event only when the deferred request executes successfully.

## Tests and Verification

Focused tests must be written and observed failing before production edits.
They will prove:

1. a suspended source can request a transition, Pause remains current, and the
   request executes with its data after `PopState`;
2. the first deferred request wins and later requests are rejected;
3. a public transition cannot bypass an active overlay and the original state
   remains resumable;
4. an overlay-owned transition and a nested `PushState` are rejected;
5. stale and cleared sources cannot execute retained requests;
6. a controllable Launch pipeline completing during Pause transitions only
   after resume; and
7. Login and Battle use their existing cancellation identities during Pause
   while continuing to reject completions after exit.

After focused RED/GREEN evidence, run the complete PlayMode and EditMode suites.
Every Unity invocation must start only after confirming zero Unity processes,
must run as the sole editor process without `-quit`, must exit naturally, and
must be followed by another zero-process check.

## Failure Handling

- Invalid or stale requests return `false`; they are never replayed.
- An invalid deferred request at resume is cleared and rejected without
  changing the resumed state.
- A compilation failure, missing result XML, failed test, unexpected Unity
  process, or unrelated working-tree mutation blocks commit and push.
- Runtime implementation is committed and pushed to `gitee/master` only after
  focused and full-suite verification plus code review pass.
