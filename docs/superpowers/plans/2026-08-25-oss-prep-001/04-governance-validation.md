# Stage 4: Package And Governance

This stage owns package/public materials and validation tooling. Runtime API behavior is frozen at
the [stage 3 checkpoint](03-public-api-tdd.md#task-35-focused-then-complete-regression).

## Task 4.1: Complete Package Metadata And Package Documentation

- **Paths:** complete `Packages/com.zwebglframework.runtime/package.json`; add package
  `README.md`, `CHANGELOG.md`, and their metas. Add failing schema/link fixtures to
  `scripts/oss/validate-package-governance.test.mjs`, then implement
  `scripts/oss/validate-package-governance.mjs`.
- **Behavior:** structurally require name `com.zwebglframework.runtime`, version `0.1.0`, display
  name `FrameLoom`, description `deterministic Unity strategy-game framework`, Unity `2022.3`, MIT,
  author `Diego Zhou` with profile URL and no email, only `com.unity.ugui: 1.0.0` dependency, and
  the exact ordered keywords `unity`, `unity3d`, `unity-package`, `upm`, `webgl`,
  `deterministic-simulation`, `strategy-game`, `ecs`. Require immutable v0.1.0 URLs ending in package
  `/README.md`, `/CHANGELOG.md`, and `/LICENSE` under
  `https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/Packages/com.zwebglframework.runtime`.
  Package README is English, public-API-only, explains
  direct pinned UniTask and Unity 2022.3 Git transitive-dependency limits, and does not guess `P/R`.
  Package changelog has the approved undated `[0.1.0]` capabilities and validation limitations.
- **Do not touch:** root `README.md`, runtime/tests, namespace/asmdef/package ID, a root changelog,
  unapproved metadata fields/keywords, CI badges, Demo links, or registry publication settings.
- **Verify:** `node --test scripts/oss/validate-package-governance.test.mjs` is RED on each invalid
  fixture, then GREEN; `node scripts/oss/validate-package-governance.mjs --root "$PWD"` exits 0;
  `jq -e` validates metadata; all three URL filenames exist in the package.
- **Checkpoint:** final package metadata/docs plus validator; new metas are exact-whitelisted and
  unique. Package metadata is now final and receives no release-stage edit.
- **Stop:** Unity package specification rejects an approved field/path, docs require a guessed SHA,
  or metadata implies unsupported Unity/product/service capability.

## Task 4.2: Add Licenses, Notices, And Governance Test First

- **Paths:** add root and package `LICENSE`; root and package `THIRD_PARTY_NOTICES.md`;
  `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, and
  `.github/pull_request_template.md`; extend the governance validator/tests. Add package document
  metas only for `LICENSE` and `THIRD_PARTY_NOTICES.md`. Audit retained NativeWebSocket
  `ThirdParty/NativeWebSocket/LICENSE.txt`, nested `Runtime/Upstream/integrations/Unity/package.json`,
  and nested `Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj`.
- **Behavior:** tests first reject changed MIT bytes, notice common-field drift, missing retained
  licenses, undisclosed package third-party paths, wrong UniTask SHA, and excess email exposure.
  Both licenses are byte-identical standard MIT with `Copyright (c) 2026 Diego Zhou`. Package notices
  self-contain Kcp2k, NativeWebSocket, and external UniTask facts; root notices include those plus
  repository-only dependencies and Contributor Covenant 2.1/CC BY 4.0. Use the official unmodified
  Covenant 2.1 text and approved private contact. `SECURITY.md` lists `0.1.x` as receiving reports
  and `< 0.1` as not, promises intake/evaluation only, and offers no response/fix/SLA commitment.
  `CONTRIBUTING.md` implements the approved no-CLA/no-DCO MIT contribution grant, rights warranty,
  third-party disclosure/approval rules, and lightweight PR confirmations in English.
- **Do not touch:** third-party headers/licenses, vendored code, Demo art, per-file SPDX/MIT headers,
  README, source behavior, CLA/DCO/signing systems, or governance email outside Security/Conduct.
- **Verify:** run the governance Node tests/validator, then:

```bash
cmp LICENSE Packages/com.zwebglframework.runtime/LICENSE
shasum -a 256 LICENSE Packages/com.zwebglframework.runtime/LICENSE
test "$(shasum -a 256 Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k/LICENSE.txt | cut -d' ' -f1)" = 1a12b7192346c004f7c3e5ce90d652750aa0fe7a29df67cac5f6271433808fe0
test "$(shasum -a 256 Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/LICENSE.txt | cut -d' ' -f1)" = 50e754644c6bf26a359a7d278a660625344243342087de19fb8c9c90147307f8
```

  Expected: `cmp` and tests exit 0; license hashes match; structured common entries agree; all notice
  paths exist. NativeWebSocket notices pin `cbbf04d42522d5f5b466224a0b8e3936676f9b32`, identify the
  canonical root Apache-2.0 license as the owner-reviewed distribution basis, disclose both nested
  MIT metadata declarations as conflicts, and verify retained materials satisfy actual redistribution
  requirements. The validator rejects FrameLoom MIT reinterpretation and unresolved review deferrals.
- **Checkpoint:** licensing/governance files and their test-first validator, separate from tooling.
- **Stop:** the owner-reviewed NativeWebSocket conclusion is missing or cannot be closed from approved
  design and pinned upstream evidence, any upstream revision/license/copyright/modification fact
  remains ambiguous, official Covenant text cannot be verified, third-party bytes change, or notices
  cannot self-contain scope. Request the one required owner/legal decision rather than guessing.

## Task 4.3: Build The Fresh-Project Gate Test First

- **Paths:** add `ci/upm/manifest.template.json`, `scripts/oss/generate-upm-consumer.mjs`,
  `scripts/oss/generate-upm-consumer.test.mjs`, `scripts/oss/run-fresh-project-gate.sh`,
  `scripts/oss/run-fresh-project-gate.test.mjs`, `scripts/oss/validate-fresh-project.mjs`, and
  `scripts/oss/validate-fresh-project.test.mjs`. Input public contracts remain only in
  `Assets/Tests/PublicApiContract/`.
- **Behavior:** fixture RED rejects branch/tag/short SHA/`file:`/symlink/local path, wrong package
  subdirectory, wrong lock revision, internals/friend access, stale source, or failed cleanup. The
  generator and driver fixtures model a unique minimal Unity project outside the worktree and a
  manifest with one required full FrameLoom revision plus UniTask
  `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`. They copy independent contract sources, not metas, and
  generate a no-friend test asmdef with ordinary references exactly `ZWebGLFrameWork.Combat` and
  `ZWebGLFrameWork.SimulationCore`, `includePlatforms: [Editor]`, `overrideReferences: true`,
  `precompiledReferences: [nunit.framework.dll]`, `autoReferenced: false`, and
  `defineConstraints: [UNITY_INCLUDE_TESTS]`; NUnit is not an ordinary reference. Stage 4 executes only
  fixtures, negative tests, and a no-remote dry-run; it performs no Unity consumer resolution and
  emits no formal release-gate evidence. The implemented validator is exercised on fixtures for lock
  source/revision, package identity/version, origin/subdirectory, public-only access, compile result,
  and cleanup so it is ready for the first real authenticated P/R runs in
  [task 6.4](06-release-candidate.md#task-64-authorize-private-github-master-and-pr-fresh-gates).
- **Do not touch:** commit a consumer project/Library/Logs/Temp/cache, contact a remote, invoke Unity,
  generate formal release evidence, declare UniTask in package metadata, reuse host source binaries,
  or substitute `file:`, symlink, local repository, or Gitee for the unavailable GitHub gate.
- **Verify:** `node --test scripts/oss/*fresh-project*.test.mjs`; then run
  `scripts/oss/run-fresh-project-gate.sh --dry-run --no-remote --framework-revision 1111111111111111111111111111111111111111 --fixture-output "$OSS_EVIDENCE_ROOT/fresh-tool-fixture"`.
  Expected: fixture/negative tests and dry-run exit 0 without Unity or network access; generated
  manifest/asmdef/driver inputs are deterministic; no consumer project remains; output is explicitly
  fixture-only and is never registered as release evidence.
- **Checkpoint:** deterministic generator/template/driver/validator only; no generated project.
- **Stop:** a Stage 4 check contacts a remote, invokes Unity, substitutes another source for GitHub,
  produces release evidence, external tests require internals/host paths or a non-approved asmdef,
  or cleanup leaves an unreported temporary project.

## Task 4.4: Add Release, History, And Evidence Validators Test First

- **Paths:** add `ci/oss/gates.json`, `scripts/oss/compare-package-content.mjs` and `.test.mjs`,
  `scripts/oss/audit-history.mjs` and `.test.mjs`, `scripts/oss/release-evidence.mjs` and `.test.mjs`,
  `scripts/oss/run-required-gates.sh`, `scripts/oss/run-required-gates.test.mjs`,
  `scripts/oss/run-webgl-smoke.sh`, and `scripts/oss/run-webgl-smoke.test.mjs`. Update
  `.github/workflows/ci.yml` and `scripts/ci/workflow-contract.test.mjs` to invoke
  repository-safe checks without claiming a historical Actions pass.
- **Behavior:** RED fixtures reject unequal P/R trees/inventories, secrets/credentials/private keys,
  unapproved personal data, oversized or unlicensed history blobs, wrong new-commit identities,
  invalid gate phase/status/Required/digest, package-keyword/GitHub-topic set drift, unsafe filenames,
  bad checksum format/order/hash, and
  public/private evidence confusion. GREEN provides: Git-tree and resolved-package inventories;
  `ed23bb0..R` identity plus full-history privacy/provenance/size/license audit; a phase-aware gate
  registry; exact English summary table generation; standard sorted SHA-256 list generation; and an
  independent validator. The table columns are exactly `Gate | Required | Environment | Revision |
  Status | Evidence digest | Notes`; values are `Yes/No` and `PASS/FAIL/NOT RUN/NOT APPLICABLE`, with
  digest `-` only for the latter two statuses. Checksum lines are lowercase SHA-256, two spaces, then
  sanitized filename in stable byte order, include the summary hash, and exclude the list's own hash.
  `gitleaks git --redact` supplies the secret scan and its version is recorded.
- **Do not touch:** history, remotes, tags, public evidence with raw logs/absolute paths, a JSON public
  evidence authority, CI badges, or WebGL managed-allocation claims.
- **Verify:** `node --test scripts/oss/*.test.mjs scripts/ci/workflow-contract.test.mjs` and
  `node scripts/oss/audit-history.mjs --revision HEAD --evidence-root "$OSS_EVIDENCE_ROOT/history"`.
  Expected: negative fixtures fail for their intended reason; current audit produces a reviewable
  report/digests; any real finding remains a release blocker rather than an automatic history edit.
- **Checkpoint:** validators/runner/CI contract in reviewable commits separate from public files.
- **Stop:** history requires rewrite, gitleaks is unavailable/unversioned, a third-party boundary is
  unprovable, evidence can leak local data, or the phase model allows a Required non-PASS transition.

## Task 4.5: Freeze Stage 4 Behavior And Paths

- **Paths:** every stage 4 file, external contracts, package/runtime tree, host/Server/CI tooling.
- **Behavior:** rerun package/governance/fresh-tool/history/evidence tests, where `fresh-tool` means
  only fixture, negative, and no-remote dry-run tests from task 4.3, then full Node/Python, Unity
  EditMode/PlayMode, and Server regression. Audit public English scope and exact new-meta list.
- **Do not touch:** stable APIs, product content, Layout, shader logs, root README, or remotes.
- **Verify:** all commands above plus [task 1.3](01-baseline.md#task-13-run-the-pre-move-compile-and-test-baseline)
  exit 0; `git diff --check` is clean; no generated consumer/cache/evidence is tracked.
- **Checkpoint:** Package And Governance complete; package docs/notices/license/metadata are ready for
  `P`, while root README remains intentionally unchanged until `R`.
- **Stop:** any regression, non-English normative public addition, unexpected meta, generated project,
  unreviewed third-party fact, or public-scope expansion.
