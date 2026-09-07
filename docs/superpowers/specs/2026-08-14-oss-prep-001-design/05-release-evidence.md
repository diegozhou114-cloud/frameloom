# Release Evidence

This document owns the terminal release revisions, Required gates, publication state machine,
annotated tag, and public/private evidence formats. Shared terms are in the [bundle index](README.md).

## Terminal Revisions

After any number of reviewable implementation commits, final `P` contains the publishable package,
canonical move/APIs, tests and validators, host/Server/Node/Python/CI path fixes, package docs, both
licenses/notices, root governance documents, and PR template. The gates owned by the
[licensing specification](01-scope-and-licensing.md) have already passed. `P` freezes package tree
`T`.

Direct child `R` changes exactly root `README.md` to embed full `P`; it changes no other path.
Required checks include:

```bash
git rev-parse P:Packages/com.zwebglframework.runtime
git rev-parse R:Packages/com.zwebglframework.runtime
```

Both tree commands return `T`. Root README explains P/R tree identity. D0, Demo dependency, tag, and
all three `master` heads use full `R`.

If a defect appears after a candidate `P`, invalidate it and any child candidate `R`. Append a
corrected `P'`, pass applicable gates, then create README-only direct child `R'`. Do not expand the
R whitelist, amend/rebase/hide candidates, or move a tag. After tag creation, fixes require a new
version rather than candidate regeneration.

## Gate Registry

Every gate is assigned exactly one phase: `Pre-tag`, `Post-tag/Pre-public`, or `Post-public`.
Technical gates are Required unless explicitly identified as non-goals. Required package/runtime
coverage includes Unity EditMode/PlayMode, Editor/Mono allocation and SOAK, package-owned engine-free
cross-runtime .NET validation, Node/Python, WebGL IL2CPP build/browser smoke, P/R fresh-project,
external contracts, P/R tree/content equality, GUID/meta/asmdef/path, licenses/notices/metadata,
full-history secret/privacy/size/provenance/license audit, and diff/whitespace/staged whitelists.

For local macOS Unity `2022.3.62f3c1`, CompileAndValidate uses `-batchmode -nographics -quit`.
EditMode and PlayMode use `-batchmode -runTests` without `-nographics`; evidence records the
actual graphics backend and device (Metal on Apple M3 Pro for the approved local host). A test
gate is `PASS` only when Unity exits 0 by itself, the complete nonzero XML has zero failed tests,
completion-to-exit is recorded, watchdog and signal counts are zero, and no shutdown spin or
unobserved exception is present. Signal termination (including exit 143) or an XML-only result is
not a pass. This rule applies only to the local macOS gate; GitHub Actions/headless behavior needs
separate evidence and cannot inherit the local result.

Before any formal local gate runs on Apple Silicon, the runner records the host machine, hardware
ARM64 capability, editor architecture list, and executable file description. The canonical Unity
executable must be a native thin `arm64` binary: an `x86_64`, universal, Rosetta-translated, or
uninspectable editor fails before the first Required gate. This architecture preflight does not
relax the TestRunner argument, complete-XML, self-exit, signal, watchdog, shutdown, or exception
requirements above.

The Required .NET projects and mixed-assembly class filter are owned by the
[package specification](02-package-and-dependencies.md#cross-runtime-net-scope). ReplayValidator,
FrameSync, RealtimeHost, Kcp2kHost, `Server/Tests`, fixture generators, and the complete Server
solution are reference-only `Required = No` rows. Their failure or `NOT RUN` status cannot block
FrameLoom, halt independent Node/Python gates, or be represented as framework or CI PASS. The prior
336/340 aggregate Server result is historical diagnostic evidence only, never a complete PASS.

Runtime/host gates run from complete `P`; package-only checks may use equivalent `T`. `R` does not
mechanically repeat runtime tests, but must pass R fresh-project, equality, D0, and release-material
audits. Evidence may attribute unchanged runtime results to `P/T`, never claim a rerun on `R`.

WebGL IL2CPP managed allocation, Server reference validation, and explicit non-goals use
`Required = No`. Missing Actions history never waives local gates and local results are not CI
results.

## Publication State Machine

### Pre-tag

All Pre-tag Required rows must be `PASS` before creating a tag. This phase includes all P/T runtime,
Unity-host, and framework cross-runtime gates, both P/R fresh-project runs, P/R tree/content equality,
D0 on full `R`, identity, documentation, license/notices/metadata, history, diff/whitespace, and exact
staged-whitelist audits.

First fast-forward full `R` to Gitee `master`; GitHub still does not exist. Run D0 from the Gitee URL
pinned to full `R`, then require the complete history/privacy/license audit to pass. Only afterward
perform the approved availability recheck, create GitHub as an empty private repository without
generated content, and push complete unchanged history with `master=R`. This first GitHub push must
not create or push a tag.

Run the P and R fresh-project gates through the real private GitHub URL. Record their authenticated
private boundary and never describe them as anonymous. P/R commit objects are both reachable from
`master=R`; lockfiles must resolve their respective full SHAs. A 404 from the three tag-based package
metadata URLs before `v0.1.0` exists is expected and does not fail these UPM gates. Only when every
Pre-tag Required row is `PASS` may the tag transition begin.

### Tag Transition

This transition is not a fourth gate phase. It starts only after every Pre-tag Required row passes
and ends before Post-tag/Pre-public evaluation.

Create one local unsigned annotated `v0.1.0` directly on `R`. Its English message contains
`FrameLoom v0.1.0` and full `R`. Reject lightweight, wrong-target, abbreviated-message, signed, or
verified claims. Push that same existing tag object to Gitee and private GitHub; never recreate it
per host.

### Post-tag / Pre-public

All Required rows in this phase must be `PASS` before public visibility. Verify local/Gitee/GitHub
annotated tag-object SHAs are identical, peeled SHAs equal full `R`, all `master` heads/defaults
equal `R`, GitHub topics match the approved set, and tagged package README/changelog/license paths
and private authenticated metadata links are correct. Confirm security, Actions configuration,
release metadata, and reviewed evidence candidates are complete. Attach candidate versions under
the two approved asset names to the private repository's Release before visibility changes.

### Post-public

After visibility changes, run anonymous clone, separate clean P and R consumer UPM resolutions,
`v0.1.0` tag/Release resolution at `R`, README/link checks, the three metadata URLs, Release page,
and candidate Release asset checks. Private authenticated results cannot substitute for these
anonymous gates. Any Required failure stops release completion and announcement. Public visibility
does not permit moving/rebuilding the tag; repair in a later commit/version.

Future-phase Required rows may remain `NOT RUN` before their phase and do not block an earlier
transition. A phase transition evaluates every Required row assigned to that phase and all prior
phases. The final validation summary is generated only when every Required row across all phases is
`PASS`; this removes the tag/publication timing loop.

After that generation, replace the two candidate assets with the final summary and checksum list.
An independent completion verifier anonymously downloads both final assets and compares their bytes,
names, and hashes with the local final outputs. This non-self-referential completion result remains
in private completion evidence rather than becoming a row inside the summary it verifies. Failure
still blocks release completion and announcement; only success permits either.

## Annotated Tag Identity

Evidence records local, Gitee, and GitHub tag-object SHA plus peeled commit SHA. Object SHAs match
each other; peeled SHAs match each other and `R`. Hosts receive the same object because recreating
tagger metadata changes object identity. `v0.1.0` is never force-updated, deleted/recreated, moved,
or retrospectively signed. Future signing starts with a later approved version.

## Validation Summary

GitHub Release exposes exactly:

```text
frameloom-v0.1.0-validation-summary.md
frameloom-v0.1.0-sha256.txt
```

The English summary records P/R/T, tag target, Unity `2022.3.62f3c1`, FrameLoom/UniTask SHAs,
environment/tool versions, gate results, failures, unverified items, limitations, and local-vs-CI
boundaries. Gate table columns are exactly:

```text
Gate | Required | Environment | Revision | Status | Evidence digest | Notes
```

`Required` is `Yes`/`No`; `Status` is only `PASS`, `FAIL`, `NOT RUN`, or `NOT APPLICABLE`.
`PASS`/`FAIL` requires a lowercase 64-character digest mapped by the checksum list. Other statuses
use `-` and explanatory Notes. No INCONCLUSIVE/SKIPPED/WARN/UNKNOWN status exists. The generator and
validator enforce columns, values, digest mapping, phase blockers, and final all-Required-PASS.

Fixed non-Required policy rows include:

```text
WebGL IL2CPP managed allocation | No | Unity 2022.3.62f3c1 WebGL IL2CPP | R | NOT RUN | - | Not measured; Editor/Mono results cannot substitute for WebGL IL2CPP managed-allocation evidence.
Server reference build/tests | No | .NET 8 reference implementation | P | NOT RUN | - | Optional reference-host evidence; not a framework or CI result.
```

## Checksum And Privacy

Checksum lines are `<64-lowercase-sha256>  <sanitized-filename>`, sorted by filename in stable byte
order, UTF-8/LF with one final newline. No absolute path, username, random temp value, job token, or
internal identifier appears. Include the summary hash, never the checksum file's own hash. An
independent validator checks format, separator, sort, duplicates, redaction, mapping, and actual
public/private file hashes. No JSON companion or second public evidence authority exists.

Raw Unity/Profiler/Playwright/build/test logs remain in a repository-external private evidence root.
Public assets are scanned for secrets, paths, personal data, tokens, cookies, private keys, env vars,
internal URLs, and unreviewed logs. A sanitized private-artifact digest may be public, but does not
make its raw file downloadable; summary labels it as internal integrity evidence.

Publishing assets does not modify `R`, `T`, or tag. Do not claim Actions PASS without a real cited
canonical run. WebGL IL2CPP managed allocation remains unmeasured; Editor/Mono cannot substitute.
