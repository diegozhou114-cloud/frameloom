# FrameLoom

FrameLoom is a deterministic Unity strategy-game framework delivered as one Git-installable Unity
package. It provides deterministic simulation and tick output, presentation and view pooling, Replay
validation, and WebGL-oriented transport adapters for host games.

## Install The Package

Add both immutable Git dependencies to the consuming project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zwebglframework.runtime": "https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#ed0650a3b1736e71aad3a39bf5fe75a372a69d0b",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0"
  }
}
```

Unity 2022.3 cannot express Git dependencies transitively, so consumers pin UniTask directly. The
FrameLoom entry resolves the `Packages/com.zwebglframework.runtime` subdirectory at full revision
`ed0650a3b1736e71aad3a39bf5fe75a372a69d0b` (`P`).

Canonical GitHub consumer resolution is a later release gate; local package-freeze evidence does not
prove private or public GitHub resolution yet.

`P` owns package tree `T` (`2cf3dd10887cf63639d4593a8b5761d6645b1ae4`). Its README-only direct
child `R` will share that exact tree. D0, the planned external validation demo, and the release tag
use full `R` once it exists; they do not change the package dependency pinned above.

## Compatibility And Validation Scope

FrameLoom supports Unity 2022.3 LTS and was tested with Unity `2022.3.62f3c1`. Other Unity versions,
including other 2022.3 patches, are untested. Package-freeze evidence covers Editor/Mono allocation
and SOAK validation, plus a WebGL IL2CPP build and trusted browser smoke. WebGL IL2CPP managed
allocation remains unmeasured.

`Server/` is reference/validation only. It is not distributed package runtime or production server
infrastructure.

## Quick Start

Create a valid `BattleInit` and an `ISimBattleSession` from your game's configuration, then drive its
public lifecycle explicitly:

```csharp
using System;
using ZWebGLFrameWork.SimulationCore;

public static class BattleLoop
{
    public static TickResult StartAndTick(ISimBattleSession session, BattleInit init)
    {
        StartResult started = session.Start(init);
        if (started.Code != StartCode.Accepted)
            throw new InvalidOperationException($"Session start failed: {started.Code}");

        return session.Tick();
    }
}
```

For buffered presentation and command-recording lifetime rules, use the
[package README](Packages/com.zwebglframework.runtime/README.md). The public package changelog is
[CHANGELOG.md](Packages/com.zwebglframework.runtime/CHANGELOG.md).

## Runnable Mini Siege Example

This branch includes a standalone two-player frame-synchronization showcase. It demonstrates how
FrameLoom's deterministic simulation, fixed-point ECS state, ordered input frames, checksums, replay
tests, and WebSocket transport fit together in a small playable game.

- [Mini Siege guide](docs/minisiege.md)
- Build: `./scripts/minisiege/build.sh macos`
- Run two local clients: `./scripts/minisiege/run.sh macos`
- Verify two packaged clients frame by frame: `./scripts/minisiege/verify.sh`

The demo uses two lanes, guards, archers, a cooldown skill, resource decisions, and a three-minute
win condition. The Unity clients render the state while the .NET host advances the authoritative
simulation at 20 ticks per second. The example is intended for framework evaluation and local
experimentation, rather than production matchmaking.

## Governance

- [License](LICENSE): project-owned code is MIT; retained third-party licensing is described in the notices.
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
