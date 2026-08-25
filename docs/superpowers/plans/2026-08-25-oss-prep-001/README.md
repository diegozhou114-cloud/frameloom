# OSS-PREP-001 Implementation Plan

**Status:** Approved

**Planning baseline D:** `af1c2745062f262fc4b922f90b46f43158b537b7`

**Design correction D2:** `cdb122fd6a4451ceae489b8f9d18dff53ded9e85`

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

- `D` is the approved design commit above; `D2` is its direct release-order correction.
- `L` is the plan-bundle-only direct child of `D2`; Git resolves its SHA after commit creation.
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

Do not implement Demo content, rename `ZWebGLFrameWork.*` namespaces/asmdefs/Server projects, change
serialized identities, add source copies/symlinks/`file:` dependencies, create GitHub, push, create
a tag, switch visibility, or publish a Release during implementation. Never touch
`UserSettings/Layouts/default-2022.dwlt`, shader compiler logs, historical evidence, or historical
plans merely because they contain old paths.

## Planning Findings

- The baseline has 523 tracked `.meta` files; every file contains a GUID.
- The 25 current asmdefs comprise 16 production/transport, eight test, and one host Editor assembly.
- CodeGraph identifies `CombatManager` as the production caller of buffered presentation Apply;
  Replay capture callers are the Unity and Server Replay/session fixtures listed in stage 3.
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

Implementation requires a separate instruction after `L`; plan approval and the plan-only commit
authorize neither implementation nor publication.
