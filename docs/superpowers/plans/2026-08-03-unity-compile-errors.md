# Unity Compile Error Repair Implementation Record

**Goal:** Resolve the reported Unity compiler errors on the latest `master`
without restoring superseded architecture or changing runtime behavior.

**Environment:** Unity 2022.3.62f3c1, C# 9.0, .NET Standard 2.1, .NET 8 for
the server-side ReplayValidator.

## Original Baseline

The initial working tree reported compiler errors in simulation, gameplay,
network, combat, FSM, and replay validation code. A pre-rebase repair covered
those call sites and serializer compatibility issues.

Before integration, `origin/master` had advanced by 393 commits and contained a
newer implementation of the affected architecture. The local commits were
rebased onto that remote state so obsolete production code would not be
reintroduced.

## Rebase Decisions

- Keep the remote `ISimBattleSession` combat architecture; do not restore the
  removed `CombatBootstrap` flow.
- Keep the remote host replay codec and ReplayValidator implementation.
- Keep the remote saturating `BigInteger` implementation of `LogicInt`.
- Keep the removal of the old SimulationCore network and orchestrator files.
- Keep the remote Combat, FSM, ReplayValidator, and test assembly definitions.
- Preserve only the remaining compile-safe test correction described below.

## Retained Code Change

**File:** `Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs`

`FullSnapshot_Restore_ChecksumIdentical` previously constructed a
`SimEntity` through an inaccessible implementation detail. The test now stores
the first entity returned by `SimWorld.CreateEntity` and passes that public
handle to `DestroyEntity`.

This keeps the test independent of entity ID, generation, and world-epoch
internals while preserving its original snapshot/restore behavior.

## Final Change Boundary

The rebased local range changes only:

- `Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs`
- `docs/superpowers/specs/2026-08-03-unity-compile-errors-design.md`
- `docs/superpowers/plans/2026-08-03-unity-compile-errors.md`

Unity-generated `.meta` normalization, package-lock updates, build output, and
the user's editor layout are excluded from the commits.

## Verification

- [x] CodeGraph index is current after the rebase.
- [x] Unity batch compilation completed with a 14.751-second script compile and
  no `error CS` entries in `BuildLogs/Validate_20260803_175326.log`.
- [x] Unity EditMode tests passed: 328 total, 328 passed, 0 failed, 0 skipped.
- [x] `SimWorldDeterminismTests` passed all 10 tests, including
  `FullSnapshot_Restore_ChecksumIdentical`.
- [x] ReplayValidator built successfully with 0 warnings and 0 errors:

  ```bash
  dotnet build Server/ReplayValidator/ReplayValidator.csproj \
    --no-restore --disable-build-servers -m:1 \
    -p:UseSharedCompilation=false --verbosity minimal
  ```

- [x] `git diff --check origin/master..HEAD` passed.

## Integration

The local `master` is rebased directly on the fetched `origin/master`; no merge
commit is required. The final commits can be pushed with a normal fast-forward
push to `origin/master`.
