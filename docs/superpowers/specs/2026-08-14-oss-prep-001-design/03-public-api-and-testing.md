# Public API And Testing

This document owns the two public contracts, independent asmdef coverage, and fresh-project UPM
gate. Release-wide gate phases are defined in [Release Evidence](05-release-evidence.md).

## Borrowed Presentation Contract

The existing overload becomes public without any other signature change:

```csharp
public void Apply(SimPresentationOutputBuffer frame, long revision)
```

English XML documentation defines `frame` as borrowed from its parent `SimTickOutputBuffer`. The
caller supplies it only while the parent is valid and passes the matching parent `Revision`.
`Apply` synchronously consumes all borrowed collections and retains no buffer or collection view;
it retains only required interpolation value copies and bridge-owned views. After return, the parent
may be cleared, reused, or disposed without invalidating later `Sample` calls.

## Owning Replay Contract

`CommandRecorder` gains:

```csharp
public void Record(SimTickOutputBuffer output)
```

Validation and mutation use this exact order:

1. Null throws `ArgumentNullException(nameof(output))`.
2. Missing `SimTickOutputFields.ExecutedCommands` throws `ArgumentException` with parameter
   `output`.
3. `output.Tick < 0` means unpublished, cleared, or disposed and throws `ArgumentException` with
   parameter `output`.
4. Before Replay mutation, serialize every borrowed command into a new owning `ReplayTickRecord`
   with new `SerializableCommand` instances.
5. Only after complete serialization, append the record and update `FinalTick` and `FinalChecksum`;
   update `FinalResult` when `output.Result` is non-null, matching `Record(TickResult)`.

Every rejection or serialization failure leaves record count/content, final tick, checksum, and
result unchanged. After success, output clear/dispose/reuse cannot alter Replay data. Capture is an
owning, allocating path excluded from zero-allocation gates.

## External Assembly Contract

`Assets/Tests/PublicApiContract/ZWebGLFrameWork.PublicApiContract.Tests.asmdef` is an independent
test assembly referencing only public Combat/SimulationCore assemblies and NUnit. No
`InternalsVisibleTo`, reflection, duplicated bridge code, repository-only source path, or Demo
content is permitted.

The suite compiles both calls and verifies:

- presentation remains valid after parent clear, reuse, and dispose;
- Replay commands remain unchanged after buffer reuse;
- missing commands and new, cleared, or disposed unpublished buffers are rejected atomically;
- final tick, checksum, and terminal result match `Record(TickResult)`; and
- Replay capture stays outside allocation measurement windows.

TDD starts with an expected RED compile result from inaccessible/missing APIs, then implements the
minimum public changes and reaches focused GREEN before the complete host regression.

## Fresh-Project Gate

The repository retains only a deterministic consumer generator, manifest template, batchmode
compile driver, and result validator. It commits no generated consumer project, nested Unity
project, `Library`, `Logs`, `Temp`, or cache.

Each run creates a unique project outside the worktree with a fresh project-local `Library`. Its
manifest directly pins FrameLoom's package-subdirectory URL and UniTask to supplied full SHAs. It
rejects branches, floating tags, `file:`, symlinks, and local/host source paths.

If Unity 2022.3 supports reliable task-local UPM Git-cache isolation, use a unique cache. Otherwise
record that global-cache limitation and do not claim an empty-cache run. Prove resolution using
`packages-lock.json`, actual `Library/PackageCache` source, and observed revisions.

The generated independent asmdef has no friend relationship. Success requires:

- UPM resolves both Git dependencies;
- lock entries match expected sources, versions, and full revisions;
- FrameLoom retains `?path=/Packages/com.zwebglframework.runtime` and the expected package ID;
- public API compile and behavior validation passes without internals or host paths; and
- Unity batchmode exits zero with no Console compile error.

The caller supplies a repository-external evidence root. On success and failure, preserve manifest,
lockfile, Unity log/version, expected/actual SHAs, cache findings, validator output, exit status, and
cleanup result. Delete the temporary consumer afterward; report every residual absolute path if
cleanup fails. Keep the evidence root private and available for review.

## P And R Runs

Run the gate twice with identical validation except FrameLoom revision and with separate projects:

- `P` uses the full SHA embedded in root README.
- `R` uses the full SHA fixed by D0, the Demo, and `v0.1.0`.

Never reuse `Library`, `Temp`, `Logs`, or project-local package state. Both manifests pin the same
UniTask SHA. Lockfiles must resolve FrameLoom to `P` and `R` respectively; those revisions are not
expected to match. Both resolved packages report SemVer `0.1.0`.

Before acceptance, the terminal-revision gate in
[Release Evidence](05-release-evidence.md#terminal-revisions) must establish both package paths as
`T`. For each resolved package, generate a POSIX-relative, byte-order-sorted inventory of path plus
SHA-256 and an aggregate digest over its UTF-8/LF canonical form. Include every Git-tree path;
exclude only named Unity/UPM cache metadata outside `T`, and report other unexpected paths. The two
inventories and aggregate digests must match.

Resolution, compile, contract, revision, local-path, tree, or content mismatch fails the release
gate. Evidence stores separate P/R inputs/results, the P/R/T comparison, and inventory comparison.

## DEMO-D0 Boundary

Fresh-project validation proves installation and public contract compilation; it does not replace
`DEMO-D0`. D0 runs only against full `R` and covers real combat, Replay, pooled presentation,
Editor/Mono allocation, SOAK, and WebGL smoke. Demo implementation itself is outside OSS-PREP-001.
