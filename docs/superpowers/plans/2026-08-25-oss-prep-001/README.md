# OSS-PREP-001 Implementation Plan

**Status:** Approved

**Planning baseline D:** `af1c2745062f262fc4b922f90b46f43158b537b7`

**Design correction D2:** `cdb122fd6a4451ceae489b8f9d18dff53ded9e85`

**Framework-scope correction D3:** `c164d8a99d8a76dc08c5a7bc92798999f2af4980`

**Target branch:** `codex/oss-prep-001`

**Unity:** `2022.3.62f3c1`

## Outcome

Implement the approved [OSS-PREP-001 design](../../specs/2026-08-14-oss-prep-001-design/README.md)
without expanding product scope. The result is one canonical Git-installable package,
`com.zwebglframework.runtime`, two narrow public APIs, independent consumer validation, and the
minimum English licensing, governance, documentation, and release evidence needed for v0.1.0.

## Ordered Stages

Each stage begins only after the preceding checkpoint passes.

1. [Baseline](01-baseline.md)
2. [Canonical Package Move](02-package-move.md)
3. [Public API TDD](03-public-api-tdd.md)
4. [Package And Governance](04-governance-validation.md)
5. [Package Freeze P](05-package-freeze.md)
6. [README-only Release Commit R](06-release-candidate.md)

The phase files own their implementation rules. This index owns shared execution conventions and
does not duplicate phase-specific acceptance criteria.

Stage 6 tasks 6.1-6.2 complete local implementation. Tasks 6.3-6.4 describe separately authorized
release gates in fixed order: Gitee `R` and D0, full-history audit, private GitHub `master=R` without
a tag, then authenticated P/R fresh-project validation. Tag and visibility remain later transitions.

## Revision Terms

- `D` is the approved design commit above; `D2` corrects release order and `D3` narrows the release
  gate to framework scope.
- `L` is the original plan-bundle-only direct child of `D2`; `L2` is the plan-only direct child of
  `D3` that applies the framework-scope correction.
- `P` is the final package-freeze revision after any number of reviewable implementation commits.
- `R` is the direct child of `P` and changes only root `README.md`.
- `T` is the identical `Packages/com.zwebglframework.runtime` Git tree in `P` and `R`.
- `DEMO-D0` is a separately owned external validation against full `R`, not Demo implementation.

Symbols `P`, `R`, and `T` are resolved only after their Git objects exist. The fixed external
dependency revision is UniTask `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`.

## Execution Contract

- Run from repository root with Unity at
  `/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity`.
- Keep raw logs and generated projects under an owner-selected repository-external
  `OSS_EVIDENCE_ROOT`; never commit that directory or claim stronger cache isolation than observed.
- Treat the one-time Unity metadata canonicalization as a pre-baseline repair before Task 1.1. Its
  accepted tracked scope is exactly the 71 legacy `.meta` blobs that are 59 bytes without a terminal
  LF and that Unity 2022.3.62f3c1 reproducibly rewrites in a clean clone. Preserve every GUID, asset
  path, and pre-existing nonempty semantic value.
- A missing importer is not independently invalid. Keep the other 16 importerless `.meta` files
  byte-for-byte unchanged, including
  `Assets/Tests/SimulationCore/SessionTerminalTests.cs.meta` with `timeCreated: 1784620800`.
- Unity may generate shader compiler logs only at the approved ignored pattern
  `/Logs/shadercompiler-UnityShaderCompiler*.log`. Never edit, delete, stage, or use those logs as
  test input. Any other tracked change or unapproved untracked path blocks execution.
- Use `Diego Zhou <316624805+diegozhou114-cloud@users.noreply.github.com>` through command-scoped
  author/committer variables for every new commit. Do not change Git configuration or timestamps.
- Begin each code/tool change with a failing focused test or contract, capture RED outside the
  repository, implement the minimum change, then capture GREEN before broad regression.
- Each task below names its paths, behavior, exclusions, verification, checkpoint, and stop rule.
  A checkpoint may be committed only after its checks pass and its staged paths match that task.
- Never amend, rebase, squash, rewrite, or normalize existing history. Failed release candidates
  are superseded by later commits under the design's `P'/R'` rule.

## Fixed Change Boundary

Production source moves once from `Assets/Scripts/` into the aggregate package. Scenes, product
assets, all existing `Assets/Editor/` host tools, tests, `Server/`, and external contracts stay in
their approved host locations. No existing Editor tool is sufficiently consumer-only to justify an
assembly-boundary change in v0.1.0, so the package has no tracked `Editor/` content.

`Server/` remains a reference implementation. Only mechanical source-link/include changes required
by package relocation are allowed; behavior, test infrastructure, deployment, and productization are
outside scope. Required .NET validation targets package-owned engine-free cross-runtime contracts;
the complete Server solution and reference hosts/tests are non-Required diagnostics.

Do not implement Demo content, rename `ZWebGLFrameWork.*` namespaces/asmdefs/Server projects, change
serialized identities, add source copies/symlinks/`file:` dependencies, create GitHub, push, create
a tag, switch visibility, or publish a Release during implementation. Never touch
`UserSettings/Layouts/default-2022.dwlt`, shader compiler logs, historical evidence, or historical
plans merely because they contain old paths.

## Planning Findings

- The baseline has 523 tracked `.meta` files with 523 unique GUIDs. The pre-baseline repair
  canonicalizes the 71 clean-clone-reproducible legacy files and leaves the 16 stable importerless
  files byte-for-byte unchanged; importer presence alone is not an integrity criterion.
- The 25 current asmdefs comprise 16 production/transport, eight test, and one host Editor assembly.
- CodeGraph identifies `CombatManager` as the production caller of buffered presentation Apply;
  Replay capture callers are the Unity and Server Replay/session fixtures listed in stage 3.
- The per-project .NET classification and exact mixed-assembly class filter are owned by
  [Task 1.3](01-baseline.md#task-13-run-the-pre-move-compile-and-test-baseline). Reference Server
  failure or `NOT RUN` never blocks independent Node/Python gates and is never framework CI PASS.
- Kcp2k is MIT at `66efda6686f649838d42f078fbaabf56ac449de4`. NativeWebSocket is pinned at
  `cbbf04d42522d5f5b466224a0b8e3936676f9b32`; its canonical root Apache-2.0 license is the
  distribution basis, while nested Unity package and csproj MIT declarations are disclosed metadata
  conflicts and never a basis for FrameLoom MIT relicensing.

## Review And Commit Boundaries

The approved plan-only `L` whitelist is exactly these seven files:

```text
docs/superpowers/plans/2026-08-25-oss-prep-001/README.md
docs/superpowers/plans/2026-08-25-oss-prep-001/01-baseline.md
docs/superpowers/plans/2026-08-25-oss-prep-001/02-package-move.md
docs/superpowers/plans/2026-08-25-oss-prep-001/03-public-api-tdd.md
docs/superpowers/plans/2026-08-25-oss-prep-001/04-governance-validation.md
docs/superpowers/plans/2026-08-25-oss-prep-001/05-package-freeze.md
docs/superpowers/plans/2026-08-25-oss-prep-001/06-release-candidate.md
```

Implementation requires a separate instruction after `L2`; plan approval and the plan-only commit
authorize neither implementation nor publication.

The `L2` scope-correction whitelist is exactly the five actually modified plan files: `README.md`,
`01-baseline.md`, `02-package-move.md`, `05-package-freeze.md`, and `06-release-candidate.md` in this
directory. It contains no design, implementation, test, package, or Server file.
