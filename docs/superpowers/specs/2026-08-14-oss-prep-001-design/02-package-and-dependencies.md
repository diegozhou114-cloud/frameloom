# Package And Dependencies

This document owns the canonical UPM layout, dependency contract, package metadata, and public
package-document boundaries. Release revision terms are in the [bundle index](README.md).

## Canonical Package

`Packages/com.zwebglframework.runtime/` is the only canonical framework production source. Move,
do not copy, production files with their `.meta` files:

```text
Packages/com.zwebglframework.runtime/
|-- package.json
|-- README.md
|-- CHANGELOG.md
|-- LICENSE
|-- THIRD_PARTY_NOTICES.md
|-- Runtime/{Core,ECS,FSM,Data,Network,UI,Combat,Gameplay,SimulationCore}/
|-- Editor/
`-- ThirdParty/{Kcp2k,NativeWebSocket}/
```

Preserve every production GUID, asmdef name/reference, platform filter, define constraint, and
automatic-reference setting. A missing/changed GUID or incompatible assembly contract stops the
migration; never regenerate `.meta` to pass. Kcp2k and NativeWebSocket keep their license and
copyright files under the explicit `ThirdParty/` boundary.

Scenes, product resources, host-only Editor tools, tests, Demo content, and `Server/` remain outside
the package. `Assets/Tests/PublicApiContract/` owns the external contract assembly. Package
`Editor/` contains only consumer-facing tooling. Update host, Server, CI, test, and documentation
paths to the canonical source. No manifest authors `file:` dependencies or symlinks; the current
host uses Unity's embedded-package behavior, while external consumers use immutable Git URLs.

`Server/` is a package-external reference implementation, not a service deliverable. Package moves
may change only mechanical source-link/include paths required by relocation; they do not change
Server behavior, test infrastructure, deployment scope, or product readiness.

## Cross-Runtime .NET Scope

The Required .NET gate validates package-owned, engine-free contracts. It does not build or test the
complete Server solution as a release condition. The project inventory is:

| Project | Classification | Basis |
| --- | --- | --- |
| `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj` | Framework Required | Directly compiles package-owned SimulationCore source. |
| `Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj` | Framework Required | Directly compiles package-owned Network contracts. |
| `Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj` | Framework Required | Directly compiles ECS source and its engine-free lifecycle tests. |
| `Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj` | Framework Required | Directly compiles Network contracts/runtime/WebGL boundaries and their engine-free tests. |
| `Server/SimulationCore.Tests/ZWebGLFrameWork.SimulationCore.ServerTests.csproj` | Mixed | Links framework tests but also references ReplayValidator and repository checks. |
| `Server/ReplayValidator/ReplayValidator.csproj` | Reference Non-Required | Replay validation reference executable. |
| `Server/FrameSync/FrameSyncServer.csproj` | Reference Non-Required | Frame-sync reference behavior. |
| `Server/Kcp2kHost/ZWebGLFrameWork.Kcp2kHost.csproj` | Reference Non-Required | Kcp transport reference host. |
| `Server/RealtimeHost/ZWebGLFrameWork.RealtimeHost.csproj` | Reference Non-Required | ASP.NET realtime reference host. |
| `Server/Tests/ZWebGLFrameWork.Server.Tests.csproj` | Reference Non-Required | Tests ReplayValidator, FrameSync, Kcp2kHost, and RealtimeHost. |
| `Server/Tools/FrameSyncFixtureGenerator/FrameSyncFixtureGenerator.csproj` | Reference Non-Required | Reference fixture generation. |
| `Server/Tools/ReplayFixtureGenerator/ReplayFixtureGenerator.csproj` | Reference Non-Required | Reference replay fixture generation. |
| `Server/ZWebGLFrameWork.Server.sln` | Reference Non-Required aggregate | Mixes every classification and is not a framework gate. |

The stable Required subset of the mixed test assembly is the exact classes
`ContentManifestGoldenTests`, `DeterministicRandomCompatibilityTests`,
`LogicIntCompatibilityTests`, `ModeBehaviorGoldenTests`, `SessionCommandTests`,
`SessionDeterminismTests`, `ModeBehaviorTests`, `CoreTargetingTests`, `SimSystemGroupTests`, and
`SessionTerminalTests`, selected by fully qualified class-name filters. Exclude
`ArchitectureTests`, `ReplayRoundTripTests`, and `ReplayValidatorTests`. A filter that selects an
unexpected class or depends on repository-root/service behavior stops the gate; do not create a new
test project to conceal the mixed boundary.

## Dependency Contract

Unity 2022.3 cannot express Git dependencies transitively between packages. Each consumer manifest
therefore declares both:

- FrameLoom at
  `https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime`
  plus a full framework commit SHA; and
- UniTask at `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` plus
  full SHA `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`.

That UniTask revision is package version `2.5.11` under MIT. UniTask is not copied, modified,
vendored, transitively declared, or covered by FrameLoom MIT. Framework `package.json` lists only
registry-resolvable dependencies, including `"com.unity.ugui": "1.0.0"`. "One aggregate package"
means FrameLoom itself has one package, not that consumers have one manifest dependency.

## Exact Metadata

The package uses:

```json
{
  "name": "com.zwebglframework.runtime",
  "version": "0.1.0",
  "displayName": "FrameLoom",
  "description": "deterministic Unity strategy-game framework",
  "unity": "2022.3",
  "license": "MIT",
  "author": {
    "name": "Diego Zhou",
    "url": "https://github.com/diegozhou114-cloud"
  },
  "keywords": [
    "unity",
    "unity3d",
    "unity-package",
    "upm",
    "webgl",
    "deterministic-simulation",
    "strategy-game",
    "ecs"
  ],
  "documentationUrl": "https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/Packages/com.zwebglframework.runtime/README.md",
  "changelogUrl": "https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/Packages/com.zwebglframework.runtime/CHANGELOG.md",
  "licensesUrl": "https://github.com/diegozhou114-cloud/frameloom/blob/v0.1.0/Packages/com.zwebglframework.runtime/LICENSE"
}
```

The implementation merges these fields with approved registry dependencies. It sets no
`unityRelease`, prerelease/build metadata, `author.email`, homepage, support, publisher, company,
organization, or technical-support field. v0.1.0 is Git UPM only, not a registry publication.

The three tagged URLs never use a branch. The package must contain `README.md`, `CHANGELOG.md`, and
`LICENSE` at those exact paths. During Gitee-only D0 their expected 404 staging state is not a D0
failure. Private GitHub verifies paths/tag contents; public gate verifies anonymous access. A Unity
specification conflict stops implementation for owner review rather than silently changing names.

Parse `package.json` structurally. Keywords must equal the eight GitHub topics in spelling, case,
count, and fixed package order. Remote topic comparison ignores order but requires exact set
equality. No unapproved discovery term is allowed, including multiplayer, rollback, server,
production-ready, game-template, networking, finite-state-machine, data-binding, or replay.

## Compatibility And README

Supported compatibility is Unity 2022.3 LTS; complete validation uses `2022.3.62f3c1`. Public docs
state that not every 2022.3 patch was tested. Unity 2021, Unity 6, and all other versions are
unsupported/unverified; any expansion requires matching Editor, test, and WebGL evidence.

Root README is package-first. Its complete manifest example pins FrameLoom to full `P` and UniTask
to the approved full SHA, explains that `P` and `R` share `T`, and uses neither branch, tag, `file:`,
symlink, nor local source path as a dependency. Quick start uses public APIs only. Before a real Demo
is public it says only "planned external validation demo" and includes no placeholder link.

The README distinguishes Editor/Mono allocation and SOAK evidence, WebGL IL2CPP build/browser smoke,
and unmeasured WebGL IL2CPP managed allocation. It shows no CI badge/claim before a real successful
GitHub Actions run. `Server/` is only a reference/validation host, with no production, deployment,
monitoring, or SLA promise.

## Language And Changelog

v0.1.0 public READMEs, changelog, licenses, notices, governance files, repository metadata, Release
materials, package metadata, changed/new public XML docs, and consumer instructions are English-only
and authoritative. No bilingual paragraphs or translation copy are added; historical/internal
Chinese material remains non-normative. Future translation is a separate, explicitly
non-authoritative task.

`Packages/com.zwebglframework.runtime/CHANGELOG.md` is the only version-history authority. There is
no root changelog. Its undated `[0.1.0]` entry records the aggregate UPM package, Unity support and
tested editor, simulation/tick output, presentation/pooling, Replay validation, public APIs,
direct-pinned UniTask, Server/reference status, allocation/WebGL boundaries, and material
compatibility notes. It is final in `P`/`T`; `R` cannot change it. GitHub Release notes only summarize
and link to it; root README uses a `v0.1.0` or full-commit permalink. Gitee maintains no separate
Release notes. Fixes use new version `0.1.1`, new package/release revisions, and immutable tag.
