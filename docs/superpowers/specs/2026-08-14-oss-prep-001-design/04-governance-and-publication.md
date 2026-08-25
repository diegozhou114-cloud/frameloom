# Governance And Publication

This document owns public branding, repository authority, governance policy, branch policy, and Git
identity. Phase-specific blockers are in [Release Evidence](05-release-evidence.md).

## Brand And Repository

Public brand and package display name are `FrameLoom`; description is
`deterministic Unity strategy-game framework`. The future canonical URL is
`https://github.com/diegozhou114-cloud/frameloom`. Gitee staging remains
`https://gitee.com/zhoujiawencoding/zweb-glframe-work`; different basenames are intentional, and
mirroring is verified by full commit/tag identities rather than names.

v0.1.0 does not rename `ZWebGLFrameWork.*` namespaces, asmdefs, package ID
`com.zwebglframework.runtime`, serialized identities, GUIDs, or Server/source names except the
approved package relocation. Repository topics exactly match the eight keywords defined in
[Package And Dependencies](02-package-and-dependencies.md#exact-metadata). Topics are discovery
metadata, not compatibility, multiplayer, production, or product-readiness claims.

A read-only check on 2026-08-14 returned `Page not found` for the proposed GitHub URL in the owner's
signed-in session; it created no repository. Recheck availability immediately before creation.

## Authority And Branches

Before publication, Gitee is staging authority and `DEMO-D0` uses full `R`; GitHub does not exist
before D0. After D0 passes, the complete history/privacy/license audit must pass before the first
private GitHub push. Recheck availability, then create GitHub empty and private with no generated
README, license, `.gitignore`, initial commit, `main`, or other branch.

The Pre-tag GitHub push contains complete unchanged history and `master=R` only; it pushes no tag.
Set `master` as the sole default/canonical branch and do not squash, rebase, filter, or re-import.
Local, Gitee, and GitHub canonical/default branches are all `master`; no `main` compatibility branch
exists. Any future rename is separate governance work. After all Pre-tag gates pass, create the tag
once locally and push the same tag object to Gitee and private GitHub.

After publication, GitHub is sole authority for branches, PRs, Issues, Actions, Releases, and
development. Gitee accepts only fast-forward mirrors of GitHub branches/tags and no independent
commit. Dependency manifests always pin full SHAs, never `master`; stable links use immutable tags
or commit permalinks.

The external Demo uses the GitHub package URL pinned to full `R`. `v0.1.0` is a human Release label
and documentation anchor, not the dependency locator. Publication follows the phase machine in
[Release Evidence](05-release-evidence.md#publication-state-machine).

## Security Policy

`SECURITY.md` prefers GitHub Private Vulnerability Reporting and uses
`diegozhou114@gmail.com` only when private reporting is unavailable. Unfixed vulnerabilities must
not enter public Issues. Its explicit table initially says:

| Version line | Receives security reports |
| --- | --- |
| `0.1.x` | Yes |
| `< 0.1` | No |

Only the current minor line receives/evaluates reports. On v0.2.0, 0.2.x becomes supported and
0.1.x unsupported. Before 1.x, no concurrent-minor maintenance is promised. "Supported" promises
only report intake/evaluation, with no response, remediation, patch, or SLA commitment; action
depends on impact, reproducibility, and capacity.

## Conduct And Contributions

`CODE_OF_CONDUCT.md` uses official Contributor Covenant 2.1 without rewriting core standards,
scope, responsibilities, or enforcement. It retains attribution, version, and CC BY 4.0 link and
uses `diegozhou114@gmail.com` for private conduct reports. Public Issue disclosure is never
required. Official references are:

- `https://www.contributor-covenant.org/version/2/1/code_of_conduct/`
- `https://creativecommons.org/licenses/by/4.0/`

`CONTRIBUTING.md` sends ordinary defects/features to GitHub Issues/PRs and security/conduct matters
to private channels. The email is not general support and carries no SLA. Contribution licensing
is defined only in [Scope And Licensing](01-scope-and-licensing.md#contribution-licensing).

README links to `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, and notices without repeating
the email. Among public repository files/metadata, the governance email appears only in Security and
Conduct. It is absent from README, package metadata, repository description, contribution guide,
license, Release metadata, commits, and tag.

## Public Git Identity

On 2026-08-25, a read-only check of the authenticated GitHub Email settings page confirmed the exact
noreply address `316624805+diegozhou114-cloud@users.noreply.github.com`. The numeric account prefix is
authoritative and must not be omitted or inferred from the username.

From `D` onward, all design, plan, implementation/checkpoint, `P`, and `R` commits use author and
committer `Diego Zhou <316624805+diegozhou114-cloud@users.noreply.github.com>`. The annotated tag uses
the same tagger. Supply
`GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME`, and `GIT_COMMITTER_EMAIL` through
command-scoped environment variables; tag identity is also command-scoped. Modify neither local nor
global Git configuration or another worktree's behavior.

Use natural author, commit, and tagger timestamps; never fabricate, backdate, normalize, or replace
them. Before release, enumerate `ed23bb0..R` and verify every full SHA, author/committer name/email,
and timestamps. Any mismatch blocks tag, public visibility, and Release. Record final tagger
name/email/timestamp only, not other GitHub settings.

Do not rewrite, rebase, or amend `ed23bb0` or earlier history to normalize identity. Evaluate older
emails through the full-history privacy audit. New commits and tag remain unsigned; noreply metadata
is not cryptographic proof and must not be called signed or verified.
