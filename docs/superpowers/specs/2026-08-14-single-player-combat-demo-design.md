# Single-Player Combat Demo Design

**Date:** 2026-08-14
**Status:** Approved design
**Framework:** ZWebGLFrameWork on Unity `2022.3.62f3c1`

## 1. Goal

Create a separate Unity repository containing a small single-player combat technology demo. A
clean Unity project must install the framework as a versioned UPM Git dependency and use public
APIs for deterministic commands, buffered tick output, presentation interpolation, pooled views,
battle completion, checksum reporting, and Replay validation. Framework source must not be copied
into the Demo's `Assets` tree.

## 2. Non-Goals

The Demo excludes villages, construction, progression, multiple stages, login, networking,
matchmaking, prediction, rollback, hot update, complex animation, production UI, and new SOAK
coverage. It validates a framework; it is not a small commercial game.

## 3. Repository And Package Boundary

The Demo is an independent Git repository and Unity project. The framework remains in its current
repository and ships as one aggregate package, `com.zwebglframework.runtime`, while retaining its
internal asmdef boundaries.

`DEMO-D0` pins an exact Gitee commit SHA and package subdirectory in `Packages/manifest.json`.
Local `file:` dependencies, symlinks, and copied framework folders are forbidden. After D0 passes,
the dependency changes to the public GitHub package URL pinned to `v0.1.0`; it never follows
`master`.

## 4. Framework Prerequisites

`OSS-PREP-001` delivers the UPM package and two public seams:

1. Make the existing `SessionPresentationBridge.Apply(SimPresentationOutputBuffer, long)`
   overload public. XML documentation must state its borrowed, synchronous lifetime.
2. Add a public `CommandRecorder` buffered-output overload. It requires a non-null
   `SimTickOutputBuffer` containing `ExecutedCommands`, copies permanent Replay data, and updates
   final tick, checksum, and result as `Record(TickResult)` does.

External-assembly contract tests cover both seams. The Demo cannot use `InternalsVisibleTo`,
reflection, duplicated bridge code, or package internals. Replay recording is intentionally owning
and remains outside zero-allocation windows.

## 5. Runtime Architecture

### DemoBootstrap

Creates the canonical manifest, `BattleInit`, session, reusable output buffer, presentation bridge,
view factory, recorder, and UI bindings. It requests `Presentation | ExecutedCommands` and
pre-sizes the fixed entity budget. Startup either reaches deployment or shows a terminal error; it
never silently falls back to owning `Tick()`.

### DemoBattleLoop

Advances at 20 Hz. Each fixed tick:

1. calls `IBufferedSimBattleSession.TickInto` with one buffer;
2. synchronously applies borrowed presentation to `SessionPresentationBridge`;
3. records through the buffered `CommandRecorder` overload;
4. copies tick, phase, checksum, and result scalars into UI state;
5. samples presentation interpolation between fixed ticks.

The loop never calls owning `Tick()` or reads `session.State` after startup.

### InkSpriteViewFactory

Implements `IEntityViewFactory` with bounded `GameObjectPool` instances for player units, enemy
guards, and the stronghold. Each object owns a `SpriteRenderer` and `IEntityView` component.
Checkout resets transform, sprite, tint, and active state; return ends the borrow and deactivates
the object. Presentation controls position; view-only tint or scale may show brief impact feedback.

### DemoHud

Contains only swordsman, archer, and guardian choices; start, restart, and validate-Replay actions;
and phase, tick, checksum, result, and Replay status. The player selects a unit, clicks a legal
deployment position, and starts deterministic automatic combat.

### DemoReplayController

At terminal result, exports the owning `ReplayFile`, creates a fresh session from the same package
and manifest, and calls `ReplayPlayback.Run`. It displays validity, command count, final checksum,
and failed tick. It never reuses the completed live session.

## 6. Data Flow

```text
Pointer input / HUD
  -> deterministic command submission
  -> IBufferedSimBattleSession.TickInto
  -> reusable SimTickOutputBuffer
       -> SessionPresentationBridge -> InkSpriteViewFactory -> GameObjectPool
       -> buffered CommandRecorder -> ReplayFile -> fresh session -> ReplayPlayback.Run
```

Unity input, timing, rendering, and generated art remain outside `SimulationCore`.

## 7. Interaction And Lifecycle

One orthographic 2D scene contains three player archetypes, enemy guards, and one stronghold:

```text
load -> deploy units -> start battle -> automatic resolution
     -> result/checksum -> validate replay or restart
```

Restart, scene destruction, cancellation, exceptions, and normal completion share one idempotent
cleanup path. It disposes the old session and buffer, cleans the bridge, and returns every borrowed
view before constructing another battle.

## 8. Art Direction And Provenance

The style is lightweight 2D ink-wash wuxia. Assets are one battlefield background; transparent
sprites for swordsman, archer, guardian, enemy guard, and stronghold; and a few original brush or
border textures. There is no skeletal animation, multi-frame combat animation, elaborate particle
system, or portrait set. Framework interpolation supplies movement.

CherryStudio generates and organizes art locally. Every committed asset has an
`Assets/Art/PROVENANCE.md` entry containing model/service, complete prompt, generation date,
original output location or hash, edits, and the basis permitting public redistribution. Generated
art never enters the framework package.

## 9. Error Handling

- Package, manifest, or session-start failure shows a terminal error.
- Missing buffered capability is a failed validation, not an owning fallback.
- Replay mismatch shows failed tick and expected/actual checksum.
- Cleanup failure cannot leave a session running behind the error UI.
- Borrowed presentation and `IEntityView` references never outlive their contracts.

## 10. DEMO-D0 Publication Gate

D0 uses placeholder sprites and allows framework publication only when a fresh project proves:

- remote Git UPM resolution at an exact commit SHA, without copied source or internal access;
- one complete deploy-to-result battle through `TickInto` with owning `Tick()` count zero;
- one buffer instance, no post-start `session.State` reads, and synchronous borrowed output;
- entity create/update/destroy through the bridge and pooled view factory;
- Replay validity and matching final checksum in a fresh session;
- complete session, buffer, bridge, and view cleanup on restart;
- focused EditMode and PlayMode tests;
- zero `GC.Alloc` events in the warmed stable-view `TickInto -> Apply -> Sample` window, excluding
  owning Replay capture and UI work;
- a successful WebGL IL2CPP build;
- Playwright confirmation of Canvas, real deployment input, battle start, terminal result, Replay
  success, and no browser console or page errors.

The existing framework SOAK remains authoritative; D0 does not repeat 50,000 ticks.

## 11. DEMO-001 Public Gate

After framework publication, the Demo pins the GitHub `v0.1.0` package, replaces placeholders with
approved ink-wash assets, completes provenance, documents controls and validation limits, and
passes final Editor and WebGL regression. The public Demo and framework READMEs link to each other.

## 12. Risks And Controls

| Risk | Control |
|---|---|
| Package extraction breaks existing hosts | OSS-PREP preserves asmdefs and runs Unity, Server, Node, and Python gates. |
| Demo depends on internals | External asmdef tests cover the two explicit public seams. |
| Replay obscures allocation evidence | Owning recording stays outside the presentation measurement window. |
| Art rights are unclear | No asset is committed without provenance and redistribution basis. |
| Scope grows into game content | One encounter and the explicit non-goals are release gates. |
| Hidden local paths make D0 pass | D0 requires remote Git package resolution in a fresh project. |

## 13. Final Decisions

- External single-player combat technology Demo, not a complete game.
- One aggregate UPM Git package and a thin Demo adapter over public APIs.
- Simple 2D ink-wash wuxia art generated through local CherryStudio.
- Unity Editor and WebGL IL2CPP are required targets.
- The framework becomes public immediately after D0; art polish follows against `v0.1.0`.
