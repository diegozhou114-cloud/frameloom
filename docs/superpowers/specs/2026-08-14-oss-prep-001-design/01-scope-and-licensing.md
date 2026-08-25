# Scope And Licensing

This document owns the MIT, third-party, contribution-license, and per-file-header boundaries.
Project scope and release terms are defined in the [bundle index](README.md).

## MIT License

Project-owned source code and documentation use MIT. Both `/LICENSE` and
`/Packages/com.zwebglframework.runtime/LICENSE` contain the complete, unmodified standard MIT text
with this exact line:

```text
Copyright (c) 2026 Diego Zhou
```

Both files are byte-identical UTF-8 text with LF line endings and one final newline. The package
license is self-contained, not a root reference, external link, SPDX-only marker, summary, or
abbreviation. `package.json` declares `"license": "MIT"`.

The release gate records these commands, requires `cmp` exit zero, and requires identical SHA-256:

```bash
cmp LICENSE Packages/com.zwebglframework.runtime/LICENSE
shasum -a 256 LICENSE Packages/com.zwebglframework.runtime/LICENSE
```

Both license files are final in `P` and unchanged in `R`. Existing names, namespaces, asmdefs,
serialized identities, and package ID do not alter the ownership boundary. A later license change
requires an approved governance decision, new package/release revisions, and a new version.

## Third-Party Boundary

Vendored and external content remains under its original license. Preserve every original license,
copyright statement, attribution, upstream identity, and immutable revision. Do not replace,
expand, or relicense third-party rights, and do not assign FrameLoom MIT copyright to vendored
content. FrameLoom MIT excludes content separately marked under another license.

Demo art is outside the framework MIT grant. A future Demo repository must separately record art
provenance and redistribution permission; no Demo art enters this package.

## Package Notices

`Packages/com.zwebglframework.runtime/THIRD_PARTY_NOTICES.md` is self-contained for consumer audit.
It covers all package-vendored sources, resources, and transports, including Kcp2k and
NativeWebSocket. Each stable-schema entry records:

- name and official upstream URL;
- exact version, tag, or full commit SHA;
- original license and package-relative retained-license path;
- original copyright statement;
- modification status and precise modification scope; and
- every covered package distribution path.

The package notice also records UniTask upstream, MIT license, and full commit
`e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`. It states that UniTask is not vendored, is not declared
by framework `package.json`, and must be installed directly by the consumer through its pinned Git
URL.

## Repository Notices

Root `THIRD_PARTY_NOTICES.md` includes every package entry and additionally covers third-party
content used only by the host Unity project, non-package `Assets`, Editor/build tooling, tests/CI,
and the Server reference implementation. Root-only entries identify repository scope, host path,
and purpose without implying delivery to package consumers.

The root notice records Contributor Covenant 2.1, its official source, and CC BY 4.0. CC BY 4.0
applies only to `CODE_OF_CONDUCT.md`; it does not change MIT licensing for project-owned code or
documentation. Contributor Covenant is not represented as package-distributed content.

The two notices are scope-specific and need not be byte-identical. Both are final in `P`; `R`
changes neither.

## Structured Notice Audit

The validator parses a stable table or section schema, never whole-file string equality. Common
entries must agree on name, upstream URL, revision, license, copyright, and modification status.
Root entries may add host scope, path, and purpose but cannot alter shared licensing facts.

The validator reports and fails on:

- a package entry missing from the root notice;
- any common-field mismatch;
- a package third-party path without a package notice;
- a referenced retained-license file that does not exist; or
- a UniTask SHA different from the consumer manifest template.

Unresolved revision, license, copyright, modification, or path facts block release; notices cannot
use guessed values or unresolved placeholders.

## Per-File Headers

v0.1.0 does not bulk-add SPDX or copyright headers to project-owned files. The two licenses,
`package.json`, and applicable notices establish the MIT grant. Existing and new FrameLoom files do
not require individual MIT headers, and their absence is not an audit failure.

Preserve every existing third-party license, SPDX, copyright, and attribution header verbatim.
Audit third-party boundaries by path and structured notices. Do not modify large groups of source,
tests, docs, `.meta`, or generated files solely to add headers. Future per-file SPDX adoption is a
separate governance task.

## Contribution Licensing

The project requires no CLA, DCO, `Signed-off-by`, bot, signing service, or external compliance
platform. `CONTRIBUTING.md` states that contributors retain copyright and, by submitting content,
agree to publish project-owned contributions under MIT. Contributors confirm submission rights and
non-infringement.

Quoted or adapted third-party content must disclose source, original license, and changes. Content
without clear permission is prohibited. Vendored additions or updates require prior maintainer
approval and synchronized notices. A PR template may ask contributors to confirm submission rights,
MIT publication of project-owned contributions, and disclosure of all third-party sources.
