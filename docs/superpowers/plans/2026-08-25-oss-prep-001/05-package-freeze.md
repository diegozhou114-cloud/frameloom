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

- **Paths:** candidate package/runtime, host Unity project, Server solution, Node/Python/CI tooling,
  external `$OSS_EVIDENCE_ROOT/p-candidate/gates/`; `ci/oss/gates.json` owns phase assignments.
- **Behavior:** execute the Required package/runtime/host suite against the full candidate SHA. The
  runner records environment, revision, exit status, sanitized evidence name/digest, and Notes for
  each gate. Rows that structurally require `R`, tag, private GitHub, or public GitHub remain Required
  but `NOT RUN` in their later phase and do not block this stage. WebGL IL2CPP managed allocation is
  the fixed non-Required `NOT RUN`; Editor/Mono evidence cannot substitute.
- **Do not touch:** thresholds/tests to obtain PASS, package content during the run, generated logs in
  repository, or Actions success claims without a cited run.
- **Verify:** run:

```bash
scripts/oss/run-required-gates.sh --phase package-freeze \
  --revision "$CANDIDATE_P" \
  --unity /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  --evidence-root "$OSS_EVIDENCE_ROOT/p-candidate/gates"
node scripts/oss/release-evidence.mjs validate-gates \
  --registry ci/oss/gates.json \
  --results "$OSS_EVIDENCE_ROOT/p-candidate/gates/results.ndjson" \
  --through package-freeze
```

  Expected: both exit 0 and the following applicable Required gates are `PASS`:

| Gate group | Required evidence |
| --- | --- |
| Unity | compile, nonzero EditMode/PlayMode, focused external contracts |
| Performance | Editor/Mono allocation and SOAK via `SimulationPerformanceBatch.Run` |
| Server | locked restore, warnings-as-errors build, nonzero Release tests |
| Tooling | Node and Python suites, workflow contracts, clean generated state |
| WebGL | Unity 2022.3.62f3c1 IL2CPP build, summary/hashes, trusted-browser smoke |
| Structure | GUID/meta/path mapping, all asmdef fields/GUIDs, one production copy |
| Package | metadata, host manifest/lock, docs/links, license/notices/provenance |
| Audit | full-history secrets/privacy/PII/size/license and new identity range |
| Diff | whitespace, exact checkpoint whitelists, no Layout/shader-log change |

- **Checkpoint:** complete private P-candidate evidence with no failed applicable Required row.
- **Stop:** any applicable Required row is not `PASS`, any gate ran against a dirty/different revision,
  WebGL smoke is replaced by build-only evidence, or unmeasured allocation is overstated.

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
