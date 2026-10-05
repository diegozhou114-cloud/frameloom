# Stage 5: Package Freeze P

This stage owns the final package/runtime/host gate and designation of `P` and package tree `T`.
Fresh-project checks that need the final root README run in [stage 6](06-release-candidate.md).

## Task 5.1: Establish A Clean Candidate Revision

- **Paths:** all implementation/checkpoint commits after `L`; package tree; repository index and
  worktree; private `$OSS_EVIDENCE_ROOT/p-candidate/identity.txt` and `diff.txt`.
- **Behavior:** require all stage 2-4 changes to be committed in reviewable checkpoints with the
  approved command-scoped identity. Use current full HEAD as candidate; do not create an empty
  marker commit. Audit every `ed23bb0..candidate` author/committer name/email and natural timestamps.
- **Do not touch:** commit history, Git config, root README, package content after testing starts,
  remotes, tags, Layout, or shader logs.
- **Verify:** run:

```bash
CANDIDATE_P=$(git rev-parse HEAD)
test -z "$(git status --porcelain=v1 --untracked-files=all)"
git diff --check af1c2745062f262fc4b922f90b46f43158b537b7.."$CANDIDATE_P"
git log --format='%H%x09%an%x09%ae%x09%cn%x09%ce%x09%aI%x09%cI' \
  ed23bb0.."$CANDIDATE_P" > "$OSS_EVIDENCE_ROOT/p-candidate/identity.txt"
node scripts/oss/audit-history.mjs --revision "$CANDIDATE_P" \
  --evidence-root "$OSS_EVIDENCE_ROOT/p-candidate/history"
```

  Expected: clean tree/index, clean diff, every new identity is exactly approved, history audit PASS.
- **Checkpoint:** immutable candidate SHA recorded externally; not yet called `P`.
- **Stop:** dirty/untracked path, identity drift, rewritten ancestry, secret/privacy/license/size
  finding, or root README was prematurely changed for release.

## Task 5.2: Run Every Applicable Required Gate On The Candidate

- **Paths:** candidate package/runtime, host Unity project, package-linked .NET projects,
  Node/Python/CI tooling, optional Server reference projects, external
  `$OSS_EVIDENCE_ROOT/p-candidate/gates/`; `ci/oss/gates.json` owns phase assignments.
- **Behavior:** execute the Required package/runtime/Unity-host/framework suite against the candidate. The
  runner records environment, revision, exit status, sanitized evidence name/digest, and Notes for
  each gate. Rows that structurally require `R`, tag, private GitHub, or public GitHub remain Required
  but `NOT RUN` in their later phase and do not block this stage. WebGL IL2CPP managed allocation and
  Server reference validation are non-Required; neither substitutes for Required framework evidence.
- **Do not touch:** thresholds/tests to obtain PASS, package content during the run, generated logs in
  repository, or Actions success claims without a cited run.
- **Verify:** run:

```bash
OSS_SANDBOX_PROFILE=/absolute/external/path/unity-network.sb
scripts/oss/run-required-gates.sh --phase package-freeze \
  --revision "$CANDIDATE_P" \
  --unity /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity-arm64.app/Contents/MacOS/Unity \
  --sandbox-profile "$OSS_SANDBOX_PROFILE" \
  --evidence-root "$OSS_EVIDENCE_ROOT/p-candidate/gates"
node scripts/oss/release-evidence.mjs validate-gates \
  --registry ci/oss/gates.json \
  --results "$OSS_EVIDENCE_ROOT/p-candidate/gates/results.ndjson" \
  --through package-freeze
```

  For the owner-authorized isolated invocation, create the canonical external file named by
  `OSS_SANDBOX_PROFILE` with these exact bytes (including the final newline):

```scheme
(version 1)
(allow default)
(deny network-outbound (remote ip "*:*"))
(allow network-outbound (remote ip "localhost:*"))
```

  On macOS, `--sandbox-profile` is optional (omit it for the normal run) but, when supplied, must be a canonical external regular
  file with no symlink component and exactly the approved profile bytes. The unsandboxed runner keeps
  watchdog/process supervision and all Node, npm, .NET, HTTPS, and browser work outside the sandbox;
  only each Unity child (including the WebGL build child) runs through the read-only evidence copy.
  The profile denies non-loopback outbound network while allowing `localhost`. Residual limitation:
  localhost proxies can bypass the intended remote-network restriction; this is a process-local
  diagnostic boundary, not a complete egress policy. Sandbox profiles are rejected for fixture runs.

  Expected: both exit 0 and the following applicable Required gates are `PASS`:

| Gate group | Required evidence |
| --- | --- |
| Unity | compile, nonzero EditMode/PlayMode, focused external contracts |
| Performance | Editor/Mono allocation and SOAK via `SimulationPerformanceBatch.Run` |
| Framework .NET | package-owned SimulationCore/Network builds, ECS/Network tests, exact mixed filter |
| Tooling | Node and Python suites, workflow contracts, clean generated state |
| WebGL | Unity 2022.3.62f3c1 IL2CPP build, summary/hashes, trusted-browser smoke |
| Structure | GUID/meta/path mapping, all asmdef fields/GUIDs, one production copy |
| Package | metadata, host manifest/lock, docs/links, license/notices/provenance |
| Audit | full-history secrets/privacy/PII/size/license and new identity range |
| Diff | whitespace, exact checkpoint whitelists, no Layout/shader-log change |

  ReplayValidator, FrameSync, RealtimeHost, Kcp2kHost, `Server/Tests`, fixture generators, and the
  complete Server solution are separate `Required = No` rows. `FAIL` or `NOT RUN` is reported without
  blocking `P`; the historical 336/340 result is diagnostic only and never framework or CI PASS.

- **Checkpoint:** complete private P-candidate evidence with no failed applicable Required row.
- **Stop:** any applicable Required row is not `PASS`, any gate ran against a dirty/different revision,
  mixed filtering drifts, WebGL smoke is replaced by build-only evidence, or optional Server/allocation
  evidence is overstated.

## Task 5.3: Designate P And Freeze T

- **Paths:** Git object at candidate HEAD, its
  `Packages/com.zwebglframework.runtime` tree, and external
  `$OSS_EVIDENCE_ROOT/release-revisions.txt`.
- **Behavior:** only after tasks 5.1-5.2 pass, record the candidate full SHA as `P` and
  `git rev-parse "${CANDIDATE_P}:Packages/com.zwebglframework.runtime"` as `T`. Record package
  version `0.1.0`, UniTask SHA, Unity version, and applicable evidence digest. Freeze every package
  path; no tag, remote, or Release action occurs.
- **Do not touch:** package tree after `T` is recorded, root README, history, remotes, or tag refs.
- **Verify:** run `git cat-file -t "$CANDIDATE_P"` and `git cat-file -t "$T_SHA"`; expected `commit`
  and `tree`. Re-run package governance validation against `P`; expected exit 0 and clean worktree.
- **Checkpoint:** full SHA values `P` and `T` plus evidence are ready for owner review and stage 6.
- **Stop:** any file must change, the package tree cannot be resolved, results reference another
  revision, or owner does not accept the candidate. Append a fix commit and rerun; do not amend.
