# OSS-PREP-001 Design

**Date:** 2026-08-14
**Status:** Approved
**Baseline:** `ed23bb0`
**Framework:** ZWebGLFrameWork on Unity `2022.3.62f3c1`

## Purpose

Prepare the existing framework for public release under the FrameLoom brand as one aggregate,
Git-installable UPM package named `com.zwebglframework.runtime`. The work preserves the current
Unity host and assembly contracts, exposes two narrow public APIs for an external combat Demo, and
adds the licensing, governance, validation, and publication controls needed for v0.1.0.

## Normative Documents

This directory is one design bundle. Rules are authoritative in their owning document; linked
documents must not restate them with different semantics.

1. [Scope And Licensing](01-scope-and-licensing.md)
2. [Package And Dependencies](02-package-and-dependencies.md)
3. [Public API And Testing](03-public-api-and-testing.md)
4. [Governance And Publication](04-governance-and-publication.md)
5. [Release Evidence](05-release-evidence.md)
6. [Implementation Sequence](06-implementation-sequence.md)

Existing Chinese design and historical documents are non-normative. The English public materials
defined in this bundle control installation, compatibility, support, licensing, and governance.

## Scope

OSS-PREP-001 only:

1. moves the existing framework into one Git-installable UPM package;
2. adds the two approved public APIs;
3. adds independent public-contract and fresh-project validation, with separate `DEMO-D0`; and
4. prepares first-release licensing, governance, documentation, evidence, and publication gates.

It does not create Demo content, complete game functionality, commercial services, deployment,
monitoring, disaster recovery, an SLA, or a long-term multi-version maintenance commitment. It
does not rename namespaces, asmdefs, serialized types, Server projects, or the package ID; change
product scope; create or publish GitHub; use source mirrors, symlinks, or authored `file:`
dependencies; or modify user Layout files or shader compiler logs.

## Terms

- `D`: design-bundle-only commit after final design approval.
- `L`: implementation-plan-only commit after separate plan approval.
- `P`: final package-freeze revision after any number of reviewable implementation commits.
- `R`: direct child of `P`; its only changed path is root `README.md`.
- `T`: Git tree at `Packages/com.zwebglframework.runtime`, identical in `P` and `R`.
- `DEMO-D0`: external release validation against full `R`; it is not part of package installation.

`P`, `R`, and `T` remain symbols until their objects exist. No document guesses their SHA values.
The phrase "terminal two-commit release" describes the adjacent `P`/`R` pair, not the number of
commits allowed in the implementation history.

## Design Commit Boundary

No commit or push occurs while this design remains under review. After final approval and the
GitHub noreply identity gate, `D` has exactly this staged-path whitelist and no other path:

```text
docs/superpowers/specs/2026-08-14-oss-prep-001-design/README.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/01-scope-and-licensing.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/02-package-and-dependencies.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/03-public-api-and-testing.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/04-governance-and-publication.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/05-release-evidence.md
docs/superpowers/specs/2026-08-14-oss-prep-001-design/06-implementation-sequence.md
```

The writing-plans workflow begins only after `D` is approved and created. `L` then stages only the
reviewed implementation plan. Neither commit is created during the current design review.
