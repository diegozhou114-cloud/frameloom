# Stage 6: README-only Release Commit R

This stage owns the adjacent `P`/`R` terminal revisions and release-candidate evidence. Remote,
tag, Demo, visibility, and Release operations remain separately authorized follow-on gates.

## Task 6.1: Create The README-only Direct Child

- **Paths:** modify exactly root `README.md`; read package README/changelog/governance and external
  `$OSS_EVIDENCE_ROOT/release-revisions.txt` for full `P`.
- **Behavior:** write an English package-first README with a complete consumer manifest that pins
  FrameLoom's GitHub `?path=/Packages/com.zwebglframework.runtime` URL to full `P` and UniTask to
  `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`. Explain that `P` and direct child `R` share `T`, while
  D0/Demo/tag use full `R`. State Unity 2022.3 LTS support and tested `2022.3.62f3c1`, untested other
  versions/patches, Editor/Mono allocation and SOAK evidence, WebGL build/browser smoke, and
  unmeasured WebGL IL2CPP managed allocation. Mark Server as reference/validation only, use public
  APIs in quick start, say only "planned external validation demo", and link package changelog plus
  governance without exposing the email or claiming CI success.
- **Do not touch:** any other file, package tree, tests, dependencies, governance, metadata, source,
  tag/ref/remote, Demo content, Layout, or shader logs.
- **Verify:** before commit run:

```bash
test "$(git diff --cached --name-only)" = README.md
git diff --cached --check
node scripts/oss/validate-package-governance.mjs --root "$PWD" --release-readme README.md \
  --expected-package-revision "$P_SHA"
```

  Commit with command-scoped approved author/committer identity and a natural timestamp. Then set
  `R_SHA=$(git rev-parse HEAD)` and require `git rev-parse HEAD^` equals full `P_SHA` and
  `git diff-tree --no-commit-id --name-only -r "$R_SHA"` prints exactly `README.md`.
- **Checkpoint:** `R` candidate exists as direct child, with full SHA recorded externally; no push.
- **Stop:** a second path is staged, README needs a package edit, P SHA is absent/abbreviated, manifest
  uses tag/branch/`file:`, identity differs, or `R` is not the direct child. Invalidate the pair and
  append corrected `P'/R'`; never amend or broaden the whitelist.

## Task 6.2: Prove P/R Tree And Git Content Equality

- **Paths:** Git objects `P`, `R`, both package trees, and
  `$OSS_EVIDENCE_ROOT/release-candidate/tree-content/`.
- **Behavior:** resolve both package paths to `T`; compare path/mode/blob inventories and content
  digests from Git, including every package path. Record commands, outputs, and exit codes.
- **Do not touch:** checkout content, package tree, commits, refs, or exclusions beyond named
  Unity/UPM cache metadata that is not part of the Git tree.
- **Verify:** run:

```bash
P_TREE=$(git rev-parse "${P_SHA}:Packages/com.zwebglframework.runtime")
R_TREE=$(git rev-parse "${R_SHA}:Packages/com.zwebglframework.runtime")
test "$P_TREE" = "$R_TREE"
node scripts/oss/compare-package-content.mjs --p "$P_SHA" --r "$R_SHA" \
  --output "$OSS_EVIDENCE_ROOT/release-candidate/tree-content"
```

  Expected: both tree values are the recorded `T`; sorted Git inventories and aggregate SHA-256 are
  byte-identical; only root README differs between revisions.
- **Checkpoint:** P/R/T equality evidence independent of UPM cache behavior.
- **Stop:** any tree/inventory/digest mismatch or a Git package path is excluded.

## Task 6.3: Authorize Gitee R, D0, Then Full-History Audit

- **Paths:** Gitee remote `origin` at `https://gitee.com/zhoujiawencoding/zweb-glframe-work.git`,
  root README, complete `ed23bb0..R` history, and external
  `$OSS_EVIDENCE_ROOT/release-candidate/{d0,history,gates}/`; no Demo source is modified.
- **Behavior:** first finish local R documentation/license/metadata/identity/diff audits. Under a
  separate owner authorization, fast-forward Gitee `master` to full R. D0 then installs the Gitee
  package-subdirectory URL pinned to full R and validates combat, Replay, pooling, Editor/Mono
  allocation, SOAK, and WebGL smoke. Only after D0 passes, run the complete history/privacy/license
  audit. GitHub must still not exist. Pre-tag metadata URL 404s remain known staging state.
- **Do not touch:** Demo implementation/assets, a GitHub repository, tags, package/runtime code,
  history, non-fast-forward remote state, or raw evidence publication.
- **Verify:** after authorization run:

```bash
git push origin "${R_SHA}:refs/heads/master"
test "$(git ls-remote --heads origin refs/heads/master | cut -f1)" = "$R_SHA"
node scripts/oss/release-evidence.mjs validate-d0 \
  --revision "$R_SHA" --evidence-root "$OSS_EVIDENCE_ROOT/release-candidate/d0"
node scripts/oss/audit-history.mjs --revision "$R_SHA" \
  --evidence-root "$OSS_EVIDENCE_ROOT/release-candidate/history"
```

  Expected: Gitee master equals R, D0 Required rows PASS against Gitee R, and the complete audit
  exits 0 before any private GitHub creation or push.
- **Checkpoint:** Gitee D0 and full-history audit evidence authorize consideration of private GitHub;
  this remains a separately approved release action, not normal implementation authority.
- **Stop:** Gitee requires non-fast-forward, D0 uses P or another URL/SHA, any D0/audit gate fails,
  or GitHub already exists before this checkpoint.

## Task 6.4: Authorize Private GitHub Master And P/R Fresh Gates

- **Paths:** future empty private `https://github.com/diegozhou114-cloud/frameloom`, Git remote
  `github`, root README manifest, two unique external consumer projects, and
  `$OSS_EVIDENCE_ROOT/release-candidate/{upm-p,upm-r}/`.
- **Behavior:** after task 6.3 PASS and a read-only availability recheck, separately authorize empty
  private repository creation with no generated file/commit/branch. Push complete unchanged history
  and `master=R` only; push no tag. Set `master` default. Run P and R gates through the real private
  GitHub URL with isolated projects and record the private authentication boundary without secrets;
  these are the first real authenticated P/R resolutions. Require respective lock revisions, version
  `0.1.0`, public contracts, cleanup, and identical resolved package inventories/digests. Tag-based
  metadata URL 404s before tag do not fail UPM.
- **Do not touch:** create/push GitHub without separate authorization, push a tag, reuse project state,
  use Gitee/local paths in place of README GitHub URL, or label private resolution anonymous.
- **Verify:** after authorization run:

```bash
git push github "${R_SHA}:refs/heads/master"
test "$(git ls-remote --heads github refs/heads/master | cut -f1)" = "$R_SHA"
test -z "$(git ls-remote --tags github refs/tags/v0.1.0)"
scripts/oss/run-fresh-project-gate.sh --framework-revision "$P_SHA" \
  --access private-authenticated \
  --unity /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  --evidence-root "$OSS_EVIDENCE_ROOT/release-candidate/upm-p"
scripts/oss/run-fresh-project-gate.sh --framework-revision "$R_SHA" \
  --access private-authenticated \
  --unity /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  --evidence-root "$OSS_EVIDENCE_ROOT/release-candidate/upm-r"
node scripts/oss/compare-package-content.mjs \
  --resolved-p "$OSS_EVIDENCE_ROOT/release-candidate/upm-p" \
  --resolved-r "$OSS_EVIDENCE_ROOT/release-candidate/upm-r"
```

  Expected: GitHub master equals R with no tag; both authenticated gates and content comparison exit
  0; every Pre-tag Required row is PASS before tag creation.
- **Checkpoint:** complete Pre-tag gate matrix authorizes the separately approved tag transition.
- **Stop:** pre-push audit evidence is absent, repository is nonempty, tag appears, history/master
  differs, auth leaks, or any resolution/compile/revision/content/cleanup/Required check fails.

## Later Authorized Publication State Machine

These are blockers, not implementation actions:

1. **Pre-tag:** tasks 6.3-6.4 must complete in order; every Pre-tag Required row is PASS.
2. **Tag transition:** only then create one local unsigned annotated `v0.1.0` on R with the approved
   noreply tagger/message; push that same existing tag object to Gitee and private GitHub. Never
   recreate it per remote or push a separately constructed tag.
3. **Post-tag/Pre-public:** require local/Gitee/GitHub tag-object and peeled SHA equality, all master
   heads/defaults at R, exact topics, tagged files/metadata links, Actions configuration, governance,
   Release metadata, and reviewed candidate assets before visibility changes.
4. **Post-public:** rerun separate anonymous P and R fresh-project gates, verify anonymous
   `v0.1.0` tag/Release resolution at R, then check clone, README/metadata URLs, Release page, and
   final assets. Private authentication evidence cannot substitute. Failure blocks completion and
   announcement and is fixed in a new version, never by moving v0.1.0.
5. **Completion:** only after every Required row in every phase is PASS generate and independently
   verify `frameloom-v0.1.0-validation-summary.md` and `frameloom-v0.1.0-sha256.txt`. Keep raw logs
   private and distinguish local results from actual Actions results. Keep every Server reference
   row `Required = No`; do not present optional results or the historical 336/340 aggregate as
   framework or CI PASS.
