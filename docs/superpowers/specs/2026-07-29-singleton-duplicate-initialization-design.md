# Singleton Duplicate Initialization Design

**Date:** 2026-07-29
**Status:** Approach B implemented on 2026-07-30.

## Goal

Prevent a duplicate `SingletonDontDestroy<T>` component from running derived
initialization after the base class schedules its GameObject for destruction.
The first instance must retain its current behavior, while duplicate instances
must have no initialization side effects.

## Scope

This change covers the singleton base contract and every current production
class that previously overrode its `Awake` method:

- `DataManager`;
- `UIManager`;
- `GameFSM`;
- `GameLauncher`;
- `NetworkManager`;
- `CombatManager`; and
- `SceneServiceRegistry`.

Other `SingletonDontDestroy<T>` subclasses already inherit the base `Awake`
directly and therefore require no mechanical edits. The change does not alter ordinary
`Singleton<T>`, gameplay behavior, FSM transition rules, network protocols,
server code, or resource-provider selection.

## Implemented Design: Base-Controlled Template Method

`SingletonDontDestroy<T>.Awake` is now a non-virtual Unity lifecycle entry.
It continues to:

1. schedule a duplicate GameObject for destruction and return; or
2. assign the first instance, call `DontDestroyOnLoad`, and invoke
   `OnSingletonAwake`.

Only the primary instance reaches the protected virtual `OnSingletonAwake`
hook. The seven former `Awake` overrides now implement that hook, so their
existing initialization code is unchanged while the base class owns duplicate
rejection and ordering.

C# prevents a subclass from overriding this base `Awake`. A PlayMode
architecture test additionally scans the production assembly and fails if any
`SingletonDontDestroy<T>` descendant declares its own `Awake`, including a
method that would hide rather than override the base entry. This makes a future
lifecycle bypass a test failure rather than a silent runtime regression.

`IsPrimaryInstance` remains available for non-initialization callbacks. In
particular, `GameLauncher.Start`, `GameFSM.OnApplicationPause`, and primary-only
destruction cleanup retain their existing guards and timing.

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

## Tests and Verification

Tests are written before production changes and must first reproduce the bug.
The focused PlayMode coverage will prove:

1. a test singleton runs `OnSingletonAwake` only for its first component and
   does not initialize a duplicate;
2. creating a duplicate `DataManager` publishes `DataReady` only once; and
3. creating a duplicate `UIManager` does not add a second
   `BuildingInfoController` or UI layer hierarchy; and
4. no production `SingletonDontDestroy<T>` descendant declares `Awake`.

After the focused RED/GREEN run, verification includes the complete PlayMode
and EditMode suites. Every Unity batch invocation follows the project rule:
check that no Unity process is active before launch, run exactly one editor
process without `-quit`, wait for natural exit, and confirm zero Unity
processes before starting the next invocation.

## Failure Handling

- A base lifecycle entry that Unity does not invoke would leave the test probe
  uninitialized and fail the focused base test.
- Any duplicate initialization event, created UI dependency, or direct
  production `Awake` declaration fails the focused PlayMode suite.
- A compilation failure, test failure, unexpected Unity process, or incomplete
  result file blocks commit and push.
- The implementation change is committed and pushed to `gitee/master` only
  after focused and full-suite verification and code review pass.
