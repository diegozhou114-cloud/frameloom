# Network and Evidence Unblock Design

**Date:** 2026-07-25
**Status:** Approved choices captured for written review

## Goal

Unblock the remaining project-repair work without weakening runtime transport
selection, third-party provenance, platform verification, or historical evidence
integrity. The approved decisions are R1, A1, B1, and E1.

## Scope

This design covers four related gates:

1. route realtime channel factories by `RealtimeTransport`;
2. vendor the pinned NativeWebSocket revision under its canonical root license;
3. install the pinned Unity editor's Windows Build Support module; and
4. replace the impossible Task 17 historical closure assumptions with an
   explicit, machine-validated recovery contract.

It does not add gameplay, invent a server endpoint, change the C1 ownership
decision, migrate Unity versions, or relax final GREEN/build gates.

## R1: Transport-Keyed Factory Registry

`RealtimeChannelFactoryRegistry.Current` remains an
`IRealtimeChannelFactory`, so existing runtime consumers do not gain concrete
WebGL or Native assembly references. Internally, the registry owns a router
whose entries are keyed by `RealtimeTransport`.

Concrete transport assemblies register only their own key:

- Native registers `RealtimeTransport.NativeKcp`.
- WebGL registers `RealtimeTransport.WebSocket`.

Registration rejects null factories and duplicate keys. Creating an
unregistered transport fails deterministically with an argument/routing error.
`ResetForTests` clears all registrations without replacing the stable router
object. Registration and creation remain synchronized by the existing registry
gate; a factory is selected under the gate and invoked after leaving it.

The design avoids concrete assembly cross-references and avoids an
exception-driven factory chain. Editor can load both concrete assemblies in
any initialization order without one transport overwriting the other.

Tests are written first and must prove:

- NativeKcp and WebSocket factories coexist;
- each transport dispatches to only its registered factory;
- duplicate, null, and missing registrations fail as specified;
- reset removes both registrations; and
- the existing connection controller continues to consume `Current` through
  `IRealtimeChannelFactory`.

## A1: NativeWebSocket Provenance and Adapter

The dependency source is the already pinned immutable revision:

- upstream: `https://github.com/endel/NativeWebSocket.git`;
- commit: `cbbf04d42522d5f5b466224a0b8e3936676f9b32`;
- archive SHA-256:
  `b89e5afd349b091c7861993fa4267fa5e1423bd64fe1187936c082ff92d46538`;
- canonical root `LICENSE`: Apache License 2.0.

Vendoring must be reproducible from `docs/dependencies/nativewebsocket.lock`.
The full canonical license remains in
`ThirdPartyNotices/NativeWebSocket-LICENSE.txt`. The Unity integration
`package.json` declares MIT while the canonical repository root declares
Apache-2.0; dependency documentation must record this metadata conflict and a
formal-project-review TODO. It must not silently rewrite either upstream fact.

The vendored package uses the actual 2.0.4 source layout and its existing
`colyseus.nativewebsocket` assembly definition. The adapter is the only direct
consumer of NativeWebSocket. It owns the long-lived native `Connect` task,
uses `OnOpen` plus protocol `AuthAccepted` for readiness, dispatches the native
message queue outside WebGL, and accepts only an absolute `wss` URI with exact
path `/v1/realtime`.

Malformed login JSON, base64, GUID, handshake, and close behavior receive
focused tests. Browser WSS end-to-end evidence is not fabricated: it requires
a real harness or remains a Task 13 ingress gate. C1 remains unchanged: real
source endpoint rebinding belongs to server `KcpIngress`.

## B1: Windows Build Support

Install Windows Build Support for Unity `2022.3.62f3c1` through the normal
Unity Hub/module mechanism. No other editor version is accepted. Installation
is an external toolchain change and uses the host approval UI.

After installation, verification must show the module belongs to the pinned
editor and a real `StandaloneWindows64` build reaches Unity compilation and
build execution. An unsupported-target message is not a passing build. Build
artifacts and logs remain under the configured external evidence root.

## E1: Historical Evidence Recovery Contract

The current Task 17 generator is historically impossible for 33 of 38 rows:
some planned exact commit subjects do not exist, many acceptance tests first
appear in the implementation commit, and some authentic RED phases were
compile failures that could not emit NUnit XML. No proxy commit, renamed XML,
hand-edited XML, or synthetic regression may repair that history.

The revised contract retains strict current behavior verification while making
historical provenance explicit:

### Fix provenance

Each closure row names one or more explicit reachable 40-character commit
SHAs. Multiple commits are permitted because `evidence-ledger.mjs` already
stores `fixCommits` as an array. The recovery manifest validates every SHA with
`git cat-file`, verifies its recorded subject for readability, and rejects
missing or duplicate row ownership. It does not manufacture a commit merely
to match planned wording.

### RED evidence classes

Each row declares exactly one validated RED class:

1. `ExecutedTestFailure`: a nonzero structured NUnit/TRX result containing the
   exact acceptance method with `Failed` status.
2. `ExpectedCompileFailure`: the original task explicitly required compilation
   to fail; a structured receipt records the command, nonzero exit, source
   revision, log hash, and exact required diagnostics/source paths.
3. `HistoricalSourceRed`: no executed RED was retained because the test and
   implementation entered the same commit; a structured receipt records the
   pre-fix revision, exact source audit, relevant diff, missing behavior seam,
   and the reason executed historical RED is unavailable.

`HistoricalSourceRed` is an explicit recovery classification, not a claim that
a test executed. It is allowed only for already-shared historical work and
cannot be used for new implementation after this design. New work continues
to require an observed failing test before production code.

### GREEN and gate evidence

Every closed row still requires a nonzero current structured GREEN containing
the exact acceptance method with `Passed` status, plus the applicable broad
gate evidence. Rows whose planned exact method never entered source require a
real current acceptance test to be added through TDD before closure; a similar
method name is not accepted. Compiler logs and source receipts never substitute
for current GREEN or final platform gates.

The recovery generator emits the existing evidence-ledger inputs, including
multiple `--fix` values where required. Evidence artifacts remain hash-addressed
and ledger validation continues to require readable commits, RED, GREEN, and
gate artifacts. The 101-entry ledger schema and issue ownership remain intact.

## Sequence and Verification

1. Implement R1 through RED/GREEN registry tests and run the full Network
   EditMode suite.
2. Vendor and implement A1 through focused adapter tests, provenance checks,
   Network EditMode, and WebGL build.
3. Install B1 and run the real Windows build gate.
4. Implement E1 validators through failing unit tests, migrate all 38 rows to
   explicit provenance, add missing current acceptance tests through TDD, and
   close only rows whose GREEN and broad gates pass.
5. Run final branch-wide review, required-test validators, supported platform
   builds, ledger `--require-all-closed`, clean-tree checks, then perform the
   already requested push and master integration.

## Failure Handling

- A duplicate transport registration is a deterministic configuration error.
- A dependency hash or license mismatch stops vendoring.
- A Windows module/version mismatch stops the build claim.
- An unreadable commit, unvalidated RED receipt, missing exact GREEN, failed
  broad gate, or changed artifact hash stops evidence closure.

These failures remain visible; none is converted into a warning-only success.
