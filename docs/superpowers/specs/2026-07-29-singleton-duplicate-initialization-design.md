# Singleton Duplicate Initialization Design

**Date:** 2026-07-29
**Status:** Approach A approved; awaiting written-spec review

## Goal

Prevent a duplicate `SingletonDontDestroy<T>` component from running derived
initialization after the base class schedules its GameObject for destruction.
The first instance must retain its current behavior, while duplicate instances
must have no initialization side effects.

## Scope

This change covers the singleton base contract and every current production
class that overrides its `Awake` method:

- `DataManager`;
- `UIManager`;
- `GameFSM`;
- `NetworkManager`;
- `CombatManager`; and
- `SceneServiceRegistry`.

Other `SingletonDontDestroy<T>` subclasses inherit the base `Awake` directly
and therefore require no mechanical edits. The change does not alter ordinary
`Singleton<T>`, gameplay behavior, FSM transition rules, network protocols,
server code, or resource-provider selection.

## Selected Design: Explicit Primary-Instance Guard

`SingletonDontDestroy<T>` exposes a protected, read-only primary-instance
predicate backed by its existing `_instance` field. Its `Awake` continues to:

1. schedule a duplicate GameObject for destruction and return; or
2. assign the first instance and call `DontDestroyOnLoad`.

Each overriding `Awake` calls `base.Awake()` first and then returns immediately
when the predicate is false. Only the primary instance may continue with its
manager-specific initialization.

The predicate does not call `Instance`, search the scene, create an object, or
introduce a dependency between managers. It reports only whether the current
component is the instance already owned by its generic base class.

`SceneServiceRegistry` replaces its local pre-base duplicate calculation with
the same post-base guard. This removes the only hand-written variation and
makes all six lifecycle entries follow one readable convention.

## Resulting Behavior

- `DataManager` creates its models and publishes `DataReady` exactly once.
- `UIManager` creates one UI root, layer set, EventSystem ownership path, and
  `BuildingInfoController`.
- `GameFSM` constructs and wires one internal FSM.
- `NetworkManager` constructs one HTTP adapter and realtime controller.
- `CombatManager` constructs one simulation/presentation dependency set.
- `SceneServiceRegistry` registers one set of scene callbacks.
- Duplicate GameObjects remain subject to Unity's normal delayed `Destroy`;
  they are inert during that delay.

## Deferred Option B

TODO: After the current repair series is complete, consider replacing the
explicit derived guards with a base-controlled template method such as
`OnSingletonAwake`. That optimization would make omission impossible for new
subclasses, but it is deliberately excluded now because it changes the
lifecycle shape of every subclass and requires a broader migration and review.

The implementation will also leave a concise TODO beside the base lifecycle
contract so this follow-up remains discoverable from production code.

## Tests and Verification

Tests are written before production changes and must first reproduce the bug.
The focused PlayMode coverage will prove:

1. a test singleton identifies only its first component as primary and does
   not run duplicate derived initialization;
2. creating a duplicate `DataManager` publishes `DataReady` only once; and
3. creating a duplicate `UIManager` does not add a second
   `BuildingInfoController` or UI layer hierarchy.

After the focused RED/GREEN run, verification includes the complete PlayMode
and EditMode suites. Every Unity batch invocation follows the project rule:
check that no Unity process is active before launch, run exactly one editor
process without `-quit`, wait for natural exit, and confirm zero Unity
processes before starting the next invocation.

## Failure Handling

- A false predicate on the first instance is a contract failure and must be
  caught by the focused base test.
- Any duplicate initialization event or created UI dependency fails the
  focused manager tests.
- A compilation failure, test failure, unexpected Unity process, or incomplete
  result file blocks commit and push.
- The implementation change is committed and pushed to `gitee/master` only
  after focused and full-suite verification and code review pass.
