# FrameLoom

FrameLoom is a deterministic Unity strategy-game framework delivered as one Git-installable Unity
package. It provides deterministic simulation and tick output, presentation and view pooling, Replay
validation, and WebGL-oriented transport adapters for host games.

## Install The Package

Add both immutable Git dependencies to the consuming project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zwebglframework.runtime": "https://github.com/diegozhou114-cloud/frameloom.git?path=/Packages/com.zwebglframework.runtime#2608415a643b37968492118057cbb13482c08538",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0"
  }
}
```

Unity 2022.3 cannot express Git dependencies transitively, so consumers pin UniTask directly. The
FrameLoom entry resolves the `Packages/com.zwebglframework.runtime` subdirectory at full revision
`2608415a643b37968492118057cbb13482c08538`. This revision includes the subscription and resource
lifetime APIs and has been resolved from GitHub in an independent Unity consumer project.

## Compatibility And Validation Scope

FrameLoom supports Unity 2022.3 LTS and was tested with Unity `2022.3.62f3c1`. Other Unity versions,
including other 2022.3 patches, are untested. Package-freeze evidence covers Editor/Mono allocation
and SOAK validation, plus a WebGL IL2CPP build and trusted browser smoke. WebGL IL2CPP managed
allocation remains unmeasured.

`Server/` is reference/validation only. It is not distributed package runtime or production server
infrastructure.

## Subscription And Resource Lifetimes

The current source adds scoped event cleanup and cancellable resource leases. Panel subscriptions
can follow one open period, while independently owned resource leases remain valid across business
provider replacement until the consumer releases them.

An optional `com.zwebglframework.yooasset` package adapts YooAsset 2.3.17 without adding a YooAsset
dependency to the core runtime. Applications configure and initialize their business resource package.
See the [integration guide](Packages/com.zwebglframework.yooasset/README.md) and
[update validation notes](docs/updates/2026-11-02-subscription-resource-lifetimes.md).

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

## Governance

- [License](LICENSE): project-owned code is MIT; retained third-party licensing is described in the notices.
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
