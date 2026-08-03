# Unity Compile Error Repair Design

## Goal

Remove the reported Unity compiler errors without changing public APIs, game
behavior, simulation determinism, or protocol behavior.

## Original Repair Scope

The initial errors required typed `ref` component callbacks, correct
`System.Math` resolution, generation-safe network delegates, a Unity-compatible
replay serializer, and compile fixes in the Combat/FSM/ReplayValidator layers.
Those repairs were implemented and verified before integration.

## Rebase Constraint

Before integration, the remote `master` had advanced by 393 commits and replaced
several affected systems with newer architecture. The rebase therefore treats
the current remote implementation as authoritative:

- Combat uses `ISimBattleSession` instead of the removed bootstrap flow.
- Replay serialization uses the host codec.
- `LogicInt` uses saturating `BigInteger` arithmetic.
- Obsolete SimulationCore network and orchestrator files remain deleted.

Restoring the pre-rebase production edits would reintroduce removed code or
overwrite newer behavior, so they are intentionally not part of the final diff.

## Final Retained Change

`SimWorldDeterminismTests.FullSnapshot_Restore_ChecksumIdentical` stores the
first `SimEntity` returned by `CreateEntity` and destroys that handle later. It
no longer constructs an entity through inaccessible internals or assumes a
specific ID, generation, or world epoch.

The final rebased scope is one test file plus this design and its implementation
record. No production API or runtime behavior changes.

## Verification Contract

- Unity batch compilation must contain no `error CS` diagnostics.
- All Unity EditMode tests must pass.
- The complete `SimWorldDeterminismTests` fixture must pass.
- ReplayValidator must build with zero warnings and zero errors.
- The commit range must pass `git diff --check` and exclude generated metadata,
  build output, package lock rewrites, and user editor settings.

Final evidence: 328/328 EditMode tests passed, the 10-test determinism fixture
passed, ReplayValidator built with 0 warnings and 0 errors, and the commit-range
whitespace check passed.
