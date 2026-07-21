# Task 15 Session-Only Battle Host Design

## Purpose

Task 15 removes the production combat runtime's split ownership between the legacy ECS path and
`SimulationCore`. A battle must be created, advanced, presented, cancelled, and completed through
exactly one `ISimBattleSession`.

The project is still a framework rather than a content-complete game. It may therefore ship with
recommended local authority and content values for now, provided every production replacement
point is explicit, searchable, and reported at runtime.

## Scope

This design covers:

- creation of complete framework-stage `BattleParams`;
- one canonical framework content manifest;
- conversion from `BattleParams` to `BattleInit`;
- a Session-only `CombatManager` running at 20 Hz;
- `BattleState` lifetime cancellation and Session-derived UI state;
- architecture, EditMode, and PlayMode acceptance tests.

Task 17 remains responsible for deleting legacy combat files after Task 15 is green. This task does
not add networking, matchmaking, production content delivery, a production identity service, or a
new general-purpose dependency injection framework.

## Decision

Use two small framework providers and keep conversion strict:

1. `FrameworkBattleAuthority` creates complete framework-stage battle identity and replay inputs.
2. `FrameworkBattleContent` owns the canonical manifest bytes used to create the Session.
3. `BattleInitFactory` validates and converts complete inputs; it never invents missing values.
4. `CombatManager` owns exactly one Session and has no legacy runtime branch.

This is preferred over a `ScriptableObject` because the current values are framework defaults, not
designer-authored project content. It is preferred over inline constants in `BattleInitFactory`
because inline fallbacks would hide the future production integration boundary.

## Architecture

```text
MainVillageState
  -> FrameworkBattleAuthority.CreateVillageAttack(...)
  -> complete BattleParams
  -> BattleState.OnEnter(...)
  -> CombatManager.StartBattleAsync(...)
       -> BattleInitFactory.Create(...)
       -> ISimBattleSessionFactory.Create(FrameworkBattleContent.CanonicalBytes)
       -> ISimBattleSession.Start(BattleInit)
       -> 20 Hz Tick()
       -> SessionPresentationBridge
       -> SimCombatPhase / SimBattleResult
       -> UI and FSM
```

Production assemblies outside `SimulationCore` must not directly reference `SimWorld` or
`SimOrchestrator`. `CombatManager` is the Unity-facing Session host, not an alternate simulation
implementation.

## Components

### FrameworkBattleAuthority

`FrameworkBattleAuthority` creates complete `BattleParams` for the current local framework flow.
It obtains the attacker identity from local persisted player data. If that identity is absent, it
uses a documented reserved framework player ID. It assigns a different reserved system defender ID
so attacker and defender can never alias.

Each battle receives non-empty Match and Session IDs. The Seed is produced once when the parameters
are created, stored on `BattleParams`, and passed unchanged to `BattleInit`; replay never regenerates
it. The provider must make its inputs injectable or otherwise deterministic under test so uniqueness
and replay stability can be verified without timing assumptions.

Every temporary authority rule is adjacent to the searchable marker
`TODO(PRODUCTION-AUTHORITY)`. The provider emits one warning per application run stating that local
framework authority is active. The warning does not prevent Debug or Release builds at this stage.

### FrameworkBattleContent

`FrameworkBattleContent` constructs and caches one canonical byte array containing the current
framework definitions for TownHall, Cannon, Barbarian, and all four supported combat modes. It
returns a clone or read-only equivalent so callers cannot mutate the process-wide source bytes.

Every temporary content rule is adjacent to `TODO(PRODUCTION-CONTENT)`. The provider emits one
warning per application run stating that framework content is active. A future production project
will replace this provider with versioned content assets or a server-authoritative payload without
changing `BattleInitFactory` or Session ownership.

The values originate in the repository's approved SimulationCore migration fixture and are locally
defined; no third-party source code is copied. If a later implementation adopts data or code from an
external repository, its source URL, revision, license, and adapted files must be recorded before
merge.

### BattleParams And BattleInitFactory

`BattleParams` carries mode, attacker and defender villages, Match ID, Session ID, Seed, attacker
player ID, and defender player ID. The authority provider is responsible for constructing complete
parameters.

`BattleInitFactory` consumes `BattleParams` plus the exact canonical manifest bytes. It validates
all required identities, IDs, villages, mode mapping, and content bytes. Invalid or incomplete
inputs produce a specific startup exception before a Session is published as active. The factory
does not contain default identities, fixed Seeds, generated IDs, or substitute manifest data.

The defender village is mapped to deterministic initial defender entities. The manifest hash is
computed from the exact byte sequence passed to the Session factory, ensuring that `BattleInit` and
the Session agree on content identity.

### CombatManager

`CombatManager` owns at most one `ISimBattleSession`. `StartBattleAsync(BattleParams,
CancellationToken)` stops and cleans any prior battle, validates the new init, creates and starts the
Session, applies its initial presentation frame, and awaits one terminal result.

`Update` uses one accumulator at 20 Hz and only calls `ISimBattleSession.Tick`. Phase, presentation,
and terminal result are derived from Session output. Deployment, move, skill, and end-deployment
commands target `Session.State.NextTick` and use a monotonic sequence per player.

Cancellation resolves the matching pending completion as cancelled before disposing the Session.
Normal termination resolves it exactly once before cleanup. `StopBattle` is idempotent, destroys all
active presentation views, clears timing and command state, disposes the Session, and cannot leave a
pending completion source. A stale callback may not resolve or publish into a newer battle.

All `UseSimulationCore` configuration and legacy world, system, spawner, and orchestrator fields are
removed from this host.

### BattleState And UI

`MainVillageState` asks `FrameworkBattleAuthority` for complete parameters instead of constructing
an incomplete self-attack inline. The current framework defender may still be derived locally, but
attacker and defender identities remain distinct.

`BattleState` passes the original `BattleParams` and a state-lifetime cancellation token to
`CombatManager`. `OnExit` cancels first, stops the battle, unsubscribes events, and closes battle UI.
A completion arriving after exit cannot publish a result or reopen UI.

FSM and panels consume `SimCombatPhase` and `SimBattleResult`; they do not translate back through
legacy `CombatPhase` or `CombatEndEvent` contracts.

## Error Handling

- Null or incomplete battle parameters fail before Session activation.
- Invalid mode or content manifest data fails with a diagnostic that identifies the invalid field.
- A rejected Session start disposes the newly created Session and leaves no active state or views.
- A rejected command returns `CommandSubmitResult` unchanged to the caller.
- Cancellation is an expected lifecycle result and is not logged as a battle failure.
- Unexpected startup or tick failures clean the active Session and are logged once with context.
- Temporary authority and content warnings are emitted once per application run, not once per tick
  or command.

## Verification

Tests are written or retained before production changes and must demonstrate the original red state.
Acceptance requires all of the following evidence:

1. The architecture test reports no production references to `SimWorld` or `SimOrchestrator`
   outside `SimulationCore`.
2. Authority tests prove attacker/defender separation, non-empty unique Match and Session IDs,
   stable replay Seed propagation, and the documented missing-player fallback.
3. Content tests prove stable canonical bytes, all four mode profiles, required archetypes, and
   mutation resistance.
4. Repository integrity tests verify that production replacement markers
   `TODO(PRODUCTION-AUTHORITY)` and `TODO(PRODUCTION-CONTENT)` remain discoverable while framework
   providers are present.
5. Factory tests cover successful field/entity/hash mapping and rejection of every required missing
   field, with no implicit fallback.
6. PlayMode proves one complete Session battle, command submission, terminal result delivery, and
   zero active views after cleanup.
7. PlayMode proves exiting `BattleState` before completion cancels the operation, disposes the
   Session, and prevents a late result.
8. The full SimulationCore EditMode suite and repository integrity suite pass.
9. Unity batch compilation completes with zero compiler errors and all targeted test reports contain
   zero failures.

Batch PlayMode evidence does not prove scene composition or hands-on UI behavior. Any interaction
that cannot be exercised without the real project scenes is reported as a remaining manual
verification risk rather than represented as tested.

## Change Boundary

Expected production changes are limited to Combat, the battle-related FSM states and assembly
references, the three battle UI panels and assembly reference, and removal of the SimulationCore
feature flag from `AppConfig`. Test changes are limited to the Task 15 architecture, EditMode, and
PlayMode coverage plus Unity metadata required for those files.

Legacy file deletion, unrelated UI redesign, save-schema migration, network protocol changes, and
general framework refactors are outside this task.
