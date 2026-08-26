# Stage 2: Canonical Package Move

This stage owns mechanical relocation, compatibility preservation, and live path repair. It makes
no public API behavior change; those changes belong to [stage 3](03-public-api-tdd.md).

## Task 2.1: Add A Move-Integrity Validator Test First

- **Paths:** add `scripts/oss/validate-package-move.mjs` and
  `scripts/oss/validate-package-move.test.mjs`; read `$OSS_EVIDENCE_ROOT/baseline/{meta.tsv,asmdefs.ndjson,tracked-files.txt}`.
- **Behavior:** RED fixtures prove the validator rejects changed/missing/duplicate GUIDs, copied
  production files, unexpected new metas, changed asmdef JSON, stale old roots, and missing mapped
  destinations. GREEN implements path-aware comparison, parsing JSON/YAML rather than source text.
- **Do not touch:** production, tests, existing metas, asmdefs, package roots, or evidence inputs.
- **Verify:** `node --test scripts/oss/validate-package-move.test.mjs`; expected RED before the
  validator exists/accepts bad fixtures, then exit 0 with every negative fixture rejected.
- **Checkpoint:** validator-only staged whitelist and passing Node test.
- **Stop:** a validator cannot distinguish path relocation from GUID/assembly mutation, or baseline
  evidence is incomplete.

## Task 2.2: Perform The Single Canonical Move

- **Paths:** create `Packages/com.zwebglframework.runtime/package.json`; move exactly:

```text
Assets/Scripts/Core/Tests/      -> Assets/Tests/Core/
Assets/Scripts/Core/Tests.meta  -> Assets/Tests/Core.meta
Assets/Scripts/Combat/Tests/    -> Assets/Tests/Combat/
Assets/Scripts/Combat/Tests.meta -> Assets/Tests/Combat.meta
Assets/Scripts/                 -> Packages/com.zwebglframework.runtime/Runtime/
Assets/Scripts.meta             -> Packages/com.zwebglframework.runtime/Runtime.meta
Packages/com.zwebglframework.kcp2k/ -> Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k/
Packages/com.zwebglframework.nativewebsocket/ -> Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/
```

- **Behavior:** use `git mv`; retain every moved file and `.meta` byte-for-byte. Add only the valid
  package shell needed for embedded discovery: name `com.zwebglframework.runtime`, version `0.1.0`,
  Unity `2022.3`, and registry dependency `com.unity.ugui: 1.0.0`; stage 4 completes public metadata.
  Generate unique GUIDs only for `package.json.meta`, `ThirdParty.meta`, `ThirdParty/Kcp2k.meta`, and
  `ThirdParty/NativeWebSocket.meta`; pass that exact new-meta whitelist to the validator. Before the
  transport move, record NativeWebSocket revision `cbbf04d42522d5f5b466224a0b8e3936676f9b32`,
  root `LICENSE.txt`, nested `Runtime/Upstream/integrations/Unity/package.json`, and nested
  `Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj`. Produce an owner-reviewed,
  auditable conclusion that the canonical root Apache-2.0 license is the distribution basis; disclose
  both nested MIT metadata declarations as a conflict, not as FrameLoom MIT relicensing authority.
  Retain the license, copyright, attribution, and notice material required for redistribution.
- **Do not touch:** `Assets/Editor/`, scenes/resources, existing asmdef contents, namespace/type names,
  public behavior, transport license/copyright headers, Layout, shader logs, or historical evidence.
- **Verify:** `git diff --summary` shows renames rather than copies; old roots are absent; then run
  `node scripts/oss/validate-package-move.mjs --baseline "$OSS_EVIDENCE_ROOT/baseline" --root "$PWD" --allow-new-meta Packages/com.zwebglframework.runtime/package.json.meta --allow-new-meta Packages/com.zwebglframework.runtime/ThirdParty.meta --allow-new-meta Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k.meta --allow-new-meta Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket.meta`.
  Expected: 523 baseline GUIDs remain once each at mapped paths, four unique new GUIDs, no production
  file under `Assets/Scripts`, and no second framework/transport package root.
- **Checkpoint:** mechanical moves, package shell, and integrity validator only.
- **Stop:** any existing GUID/content changes, copy remains, Unity regenerates metadata, the package
  cannot be discovered without an authored `file:` dependency, or pinned upstream evidence cannot
  close the NativeWebSocket conclusion. For the latter, report the one required owner/legal decision;
  do not guess, move the transport, or carry an unresolved formal-review deferral toward `P`.

## Task 2.3: Repair Every Live Path Consumer And Host Lock

- **Paths:** update `Assets/Editor/AutoFixProcessor.cs`, `Assets/Editor/CompileHook.cs`,
  `Assets/Tests/Editor/{LegacyCombatRemovalTests.cs,RepositoryIntegrityTests.cs}`,
  `Assets/Tests/Network/{LegacyNetworkStackRemovalTests.cs,WebGlAssemblyBoundaryTests.cs}`,
  `scripts/repair/audit-compiler-contracts.sh`, `scripts/vendor-kcp2k.sh`,
  `scripts/vendor-nativewebsocket.sh`, `scripts/vendor-nativewebsocket.test.sh`,
  `scripts/vendor-nativewebsocket.metadata.test.sh`, `.gitattributes`, `AGENTS.md`,
  `docs/DEVELOPMENT_GUIDE.md`, `Packages/manifest.json`, and `Packages/packages-lock.json`.
  Update Server links in `Server/SimulationCore/ZWebGLFrameWork.SimulationCore.csproj`,
  `Server/NetworkContracts/ZWebGLFrameWork.Network.Contracts.csproj`,
  `Server/Kcp2kHost/ZWebGLFrameWork.Kcp2kHost.csproj`,
  `Server/ECS.Tests/ZWebGLFrameWork.ECS.ServerTests.csproj`,
  `Server/Network.Tests/ZWebGLFrameWork.Network.ServerTests.csproj`, and
  `Server/SimulationCore.Tests/ArchitectureTests.cs`.
- **Behavior:** point live source/link/include/vendor destinations at `Runtime/` or `ThirdParty/`.
  Pin host UniTask to full SHA `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`. Author no framework
  manifest entry: embedded discovery is automatic. Regenerate the lock so the old two embedded
  packages disappear, `com.zwebglframework.runtime` is `source: embedded`, and UniTask resolves the
  same SHA. Unity's generated embedded lock value may use `file:com.zwebglframework.runtime`; this
  is not an authored `file:` dependency.
- **Do not touch:** historical plans/specs/evidence, `docs/启动管线开发待办.md`, product assets,
  runtime semantics, Server behavior/tests/infrastructure/project names, or `.github/workflows/ci.yml`
  whose existing broad `Assets/**` and `Packages/**` paths already cover the move.
- **Verify:** run `rg -n 'Assets/Scripts|Packages/com\.zwebglframework\.(kcp2k|nativewebsocket)'`
  over the listed live consumers and expect no stale hit except explicit absence assertions; parse
  both package JSON files with `jq`; run the vendor script tests and the Task 1.3 package-owned .NET
  builds/tests against relocated paths. Do not use the complete Server solution as this gate.
- **Checkpoint:** path-only repairs and regenerated host manifest/lock, separate from API changes.
- **Stop:** any consumer needs a source copy/symlink, lock revision changes, or Server must rename an
  assembly/project or change behavior/test infrastructure to compile.

## Task 2.4: Prove Assembly And Host Compatibility

- **Paths:** all moved production/transport asmdefs, all host test asmdefs, host Unity project,
  package-linked .NET projects, and `$OSS_EVIDENCE_ROOT/package-move/`.
- **Behavior:** deep-compare every asmdef field and GUID against baseline; run the stage 1 Required
  Unity, framework .NET, Node, and Python commands on moved paths; then refresh CodeGraph after the
  large move. Optional Server reference results remain separately labeled and non-blocking.
- **Do not touch:** asmdef names/references/platform settings to silence failures; package API logic.
- **Verify:** `node scripts/oss/validate-package-move.mjs ...`, full baseline commands from
  [task 1.3](01-baseline.md#task-13-run-the-pre-move-compile-and-test-baseline), and
  `codegraph sync && codegraph explore "SessionPresentationBridge CommandRecorder canonical paths"`.
  Expected: all Required regressions exit 0 and CodeGraph reports only package production definitions.
- **Checkpoint:** reviewable canonical-move commit; staged diff contains relocation/validator/path
  repair only, with no API visibility or behavior change.
- **Stop:** changed GUID/asmdef contract, serialization incompatibility, Required compile/test
  regression, mixed-filter drift, stale CodeGraph package paths after sync, or any production
  definition remains under `Assets/`. Optional Server reference failure alone does not stop the move.

## Frozen Assembly Inventory

Every row is compared by parsed JSON; `excludePlatforms` is empty throughout, and root namespace,
unsafe/override/precompiled/version/no-engine fields must remain byte-semantically equal.

| File after move | Name | GUID | Exact references | includePlatforms / defineConstraints / autoReferenced |
| --- | --- | --- | --- | --- |
| `Assets/Editor/ZWebGLFrameWork.Editor.asmdef` | `ZWebGLFrameWork.Editor` | `bc1e7ddea4af03f4f8c98d0fbd904c41` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.ECS,ZWebGLFrameWork.Data,ZWebGLFrameWork.UI,ZWebGLFrameWork.FSM,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.Combat,ZWebGLFrameWork.SimulationCore` | `[Editor] / [] / true` |
| `Assets/Tests/Combat/ZWebGLFrameWork.Combat.Tests.asmdef` | `ZWebGLFrameWork.Combat.Tests` | `37a5a73b9f3cd498ea4024d5954d5295` | `ZWebGLFrameWork.Combat,ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.SimulationCore` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Packages/com.zwebglframework.runtime/Runtime/Combat/ZWebGLFrameWork.Combat.asmdef` | `ZWebGLFrameWork.Combat` | `a4b58f3c00bb55944b3fda5d7624f2c1` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.SimulationCore,UniTask` | `[] / [] / true` |
| `Assets/Tests/Core/ZWebGLFrameWork.Core.Tests.asmdef` | `ZWebGLFrameWork.Core.Tests` | `a667fc877e975ba4cbe87636fabb41bb` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Packages/com.zwebglframework.runtime/Runtime/Core/ZWebGLFrameWork.Core.asmdef` | `ZWebGLFrameWork.Core` | `d6957f4c38b193d49aec58f1e4d4b371` | `UniTask` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/Data/ZWebGLFrameWork.Data.asmdef` | `ZWebGLFrameWork.Data` | `24d6cb3a660484048a5c9e6445add951` | `ZWebGLFrameWork.Core,UniTask` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/ECS/ZWebGLFrameWork.ECS.asmdef` | `ZWebGLFrameWork.ECS` | `d7c4ef1606ffaf24d9d8f1956830d046` | `ZWebGLFrameWork.Core,UniTask` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/FSM/ZWebGLFrameWork.FSM.asmdef` | `ZWebGLFrameWork.FSM` | `6f4734730ebad3a4db5bc3a850cb7144` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.UI,ZWebGLFrameWork.Network.Contracts,ZWebGLFrameWork.Network.Runtime,ZWebGLFrameWork.Network.UnityHttp,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.Combat,ZWebGLFrameWork.SimulationCore,UniTask` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/Gameplay/ZWebGLFrameWork.Gameplay.asmdef` | `ZWebGLFrameWork.Gameplay` | `70656e24a2b4c9343a75da506edae917` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,UnityEngine.UI` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/Network/Contracts/ZWebGLFrameWork.Network.Contracts.asmdef` | `ZWebGLFrameWork.Network.Contracts` | `7cdd0956900e46d0a7661e7b7a544a36` | `[]` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/Network/Native/ZWebGLFrameWork.Network.Native.asmdef` | `ZWebGLFrameWork.Network.Native` | `388762ebc03c34797a35afe96e5035c2` | `ZWebGLFrameWork.Network.Contracts,ZWebGLFrameWork.Network.Runtime,ZWebGLFrameWork.Kcp2k` | `[Editor,WindowsStandalone64] / [] / false` |
| `Packages/com.zwebglframework.runtime/Runtime/Network/Runtime/ZWebGLFrameWork.Network.Runtime.asmdef` | `ZWebGLFrameWork.Network.Runtime` | `82a28d0d23a84161a9ed194498434cbe` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Network.Contracts,ZWebGLFrameWork.Network.UnityHttp` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/Network/UnityHttp/ZWebGLFrameWork.Network.UnityHttp.asmdef` | `ZWebGLFrameWork.Network.UnityHttp` | `be4e160bce37a4555aa2b74a8cb8e58c` | `ZWebGLFrameWork.Network.Contracts` | `[] / [] / false` |
| `Packages/com.zwebglframework.runtime/Runtime/Network/WebGL/ZWebGLFrameWork.Network.WebGL.asmdef` | `ZWebGLFrameWork.Network.WebGL` | `a7b0ff05ac304c6886d9b6c21cae9102` | `ZWebGLFrameWork.Network.Contracts,ZWebGLFrameWork.Network.Runtime,colyseus.nativewebsocket,ZWebGLFrameWork.NativeWebSocket.Core` | `[Editor,WebGL] / [] / false` |
| `Packages/com.zwebglframework.runtime/Runtime/SimulationCore/ZWebGLFrameWork.SimulationCore.asmdef` | `ZWebGLFrameWork.SimulationCore` | `b7406297eb97846c2a85fcf18c724493` | `[]` | `[] / [] / true` |
| `Packages/com.zwebglframework.runtime/Runtime/UI/ZWebGLFrameWork.UI.asmdef` | `ZWebGLFrameWork.UI` | `2dced79e13932a94ca7d418d6902419d` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.Combat,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.SimulationCore,UnityEngine.UI` | `[] / [] / true` |
| `Assets/Tests/ECS/ZWebGLFrameWork.ECS.Tests.asmdef` | `ZWebGLFrameWork.ECS.Tests` | `7b35d7c12c2a4db3be69e6a26248519f` | `ZWebGLFrameWork.ECS` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Assets/Tests/Editor/ZWebGLFrameWork.Editor.Tests.asmdef` | `ZWebGLFrameWork.Editor.Tests` | `9299924e5f3c3457a82ba1ea751c1099` | `ZWebGLFrameWork.Editor,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.Combat,ZWebGLFrameWork.SimulationCore` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef` | `ZWebGLFrameWork.Network.Tests` | `dd88bd3e061c4a0f816c700af86edbb3` | `ZWebGLFrameWork.Network.Contracts,ZWebGLFrameWork.Network.UnityHttp,ZWebGLFrameWork.Network.WebGL,ZWebGLFrameWork.Network.Runtime,ZWebGLFrameWork.Network.Native,ZWebGLFrameWork.Kcp2k` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Assets/Tests/PlayMode/Combat/ZWebGLFrameWork.Combat.PlayModeTests.asmdef` | `ZWebGLFrameWork.Combat.PlayModeTests` | `443fa4f104524741b2115c41057fe88a` | `ZWebGLFrameWork.Combat,ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.FSM,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.SimulationCore,UniTask` | `[] / [UNITY_INCLUDE_TESTS] / false` |
| `Assets/Tests/Runtime/ZWebGLFrameWork.Runtime.Tests.asmdef` | `ZWebGLFrameWork.Runtime.Tests` | `26cff82b7740047a0afd4225549a6136` | `ZWebGLFrameWork.Core,ZWebGLFrameWork.Data,ZWebGLFrameWork.Gameplay,ZWebGLFrameWork.UI,ZWebGLFrameWork.FSM,ZWebGLFrameWork.Combat,ZWebGLFrameWork.SimulationCore,UniTask,UnityEngine.UI` | `[] / [UNITY_INCLUDE_TESTS] / false` |
| `Assets/Tests/SimulationCore/ZWebGLFrameWork.SimulationCore.Tests.asmdef` | `ZWebGLFrameWork.SimulationCore.Tests` | `ae6bfc2177fc143159852a38f9a074eb` | `ZWebGLFrameWork.SimulationCore,ZWebGLFrameWork.Combat` | `[Editor] / [UNITY_INCLUDE_TESTS] / false` |
| `Packages/com.zwebglframework.runtime/ThirdParty/Kcp2k/Runtime/ZWebGLFrameWork.Kcp2k.asmdef` | `ZWebGLFrameWork.Kcp2k` | `ba6945ab4a63d4397b323b3804254876` | `[]` | `[Editor,WindowsStandalone64] / [] / false` |
| `Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/colyseus.nativewebsocket.asmdef` | `colyseus.nativewebsocket` | `69c7f9655d64d4a878548c09abb02a6e` | `ZWebGLFrameWork.NativeWebSocket.Core` | `[Editor,WebGL] / [] / omitted` |
| `Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/ZWebGLFrameWork.NativeWebSocket.Core.asmdef` | `ZWebGLFrameWork.NativeWebSocket.Core` | `0ac1285effe484e00ae3218a789fc139` | `[]` | `[Editor,WebGL] / [] / false` |
