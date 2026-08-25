# Implementation Sequence

This document owns the six gated implementation stages. Commit/release terms are in the
[bundle index](README.md); gate phases are in [Release Evidence](05-release-evidence.md).

Each stage is a separate reviewable checkpoint. A stage begins only after the previous exit gate
passes. Never combine mechanical relocation, API behavior, governance, and release README into one
unreviewable batch.

## 1. Baseline

Record tracked files, every `.meta` GUID, asmdef name/GUID/reference, Server/CI path reference,
branch/full HEAD, and Git status. Run approved baseline compilation and tests. A failure stops
migration and is reported as pre-existing rather than obscured by later changes.

## 2. Canonical Package Move

Move production source into `Packages/com.zwebglframework.runtime/` with history and `.meta` intact;
do not copy it. Repair host, Server, CI, and asmdef-related paths. Do not mix public API behavior
changes into this stage.

Compare GUID/asmdef inventories, compile all affected assemblies, and run complete host regression.
Serialization GUID or assembly incompatibility stops work and is not bypassed by metadata
regeneration.

After the large move, run `codegraph sync` successfully before further structural analysis. The
graph must show canonical package paths before API work.

## 3. Public API TDD

Add the independent external asmdef contract and capture expected RED for inaccessible/missing
APIs. Implement only the two approved public contracts. Cover exception atomicity, missing fields,
unpublished/cleared/disposed buffers, borrowed synchronous consumption, owning Replay, reuse, and
final metadata. Require focused GREEN and full host regression.

## 4. Package And Governance

Add package metadata/docs, both licenses/notices, root governance, and PR template. Add fresh-project,
metadata, license, notice, history, evidence-generation, and evidence-validation tooling. Do not
change stable public runtime behavior.

Validate English public scope, structured package JSON/notices, license identity, dependency SHAs,
paths/links, evidence formats, and checkpoint whitelist.

## 5. Package Freeze

Run every Required pre-`R` package/runtime/host/tooling/audit/document gate. R-dependent rows remain
Required but cannot run or be marked PASS yet. When all applicable gates pass, record full `P` and
`P:Packages/com.zwebglframework.runtime` as `T`. No package path changes afterward. A failure
invalidates the candidate rather than relabeling it `P`.

## 6. Release Commit

Create direct child `R` changing only root `README.md` to embed full `P`. Run P/R tree/content
equality, both fresh-project runs, and D0 on full `R`. A failure invalidates the pair; append corrected
`P'` and README-only child `R'`, then rerun applicable gates. Do not broaden whitelist, rewrite
history, or move a tag.

Implementation planning begins only after final design approval. The design-only `D` and
plan-only `L` precede implementation under the approved identity and whitelist rules.
