# Framework and Business Resource Layers Design

## Purpose

Separate client resources into immutable framework content and replaceable
business content. The separation must be usable now with local `Resources`
assets and must leave a small, explicit integration boundary for a future
YooAsset plus Huatuo-based business hot-update implementation. No server
component, download protocol, AssetBundle build flow, YooAsset package, or
Huatuo dependency is part of this change.

## Scope and Constraints

- Framework content is loaded locally and cannot be replaced at runtime.
- Business content is loaded locally by default and is the only content that a
  future updater may replace.
- Existing unlayered `ResourceManager.Load`, `LoadAsync`, `Unload`, and
  `ClearCache` callers remain source-compatible and retain framework/local
  behavior.
- The internal legacy `Provider`, `SetProvider`, and custom-provider `Init`
  entry points are removed. They currently have no production callers and
  would allow a remote implementation to replace framework content, which
  violates the layer boundary.
- The design adds no networking, remote manifest format, server endpoint, or
  dynamic-code execution behavior.
- Existing serialized `AppConfig.EnableHotUpdate` and `ResourceProviderType`
  values remain compatible with current project assets.

## Architecture

`ResourceManager` owns two provider slots and two independent caches.

- `Framework` is initialized as a `ResourcesProvider` and is exposed only for
  loading. No public API accepts a replacement framework provider.
- `Business` is initialized as a separate `ResourcesProvider`. A public
  business-only replacement operation can swap it later. That operation clears
  only the business cache and asks the previous business provider to release
  unused assets.
- Layer-aware `Load` and `LoadAsync` APIs select a provider and cache by
  `ResourceLayer`. Cache keys include the layer so equal paths cannot collide
  between framework and business content.
- Existing unlayered APIs are compatibility APIs for framework/local content;
  they continue to use the framework provider and framework cache.

The launch state initializes the framework provider as before. It does not
choose a remote provider from the legacy resource-provider enum. The legacy
values continue to log their existing local fallback warning.

## Hot-Update Boundary

Introduce a client-only `IBusinessContentUpdater` abstraction. It has one
operation that checks and prepares a business-content update and returns an
explicit `BusinessContentUpdateResult` value. The result distinguishes:

- `NotConfigured`: no project updater has been supplied;
- `NoUpdate`: an installed updater checked successfully and found no work;
- `Updated`: an installed updater made new business content available;
- `Failed`: the updater could not establish usable business content.

The default updater reports `NotConfigured`. It performs no download, no
assembly loading, and no provider replacement.

`LaunchState` invokes this boundary only when `EnableHotUpdate` is true. A
`NotConfigured` result logs a named `TODO(YooAsset)` and `TODO(Huatuo)` warning
and continues with the local business provider. `NoUpdate` and `Updated`
continue startup. `Failed` presents a retryable boot error and stops the
pipeline before data preload. This makes absent project infrastructure safe
for framework development while ensuring a future configured updater cannot
silently fail.

The future Huatuo adapter may implement `IBusinessContentUpdater` and install
a business provider only after it has validated the business package and any
required metadata. It must never modify the framework provider. The adapter
will be a client integration and must consume a project-defined delivery
contract; defining that contract is deliberately outside this framework
change.

## Public API Shape

```csharp
public enum ResourceLayer
{
    Framework,
    Business,
}

public interface IBusinessContentUpdater
{
    UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token);
}

public enum BusinessContentUpdateStatus
{
    NotConfigured,
    NoUpdate,
    Updated,
    Failed,
}
```

`BusinessContentUpdateResult` carries a status plus an optional diagnostic
message. It does not carry server or transport concepts. A manager method
installs only an `IResourceProvider` for `ResourceLayer.Business`; callers
cannot pass the framework layer to a replacement API.

## Error and Lifecycle Rules

- A missing updater is an intentional local-development state, not a success
  report. It is visible in logs and retains local business assets.
- An updater failure stops boot in the existing retryable error flow.
- Replacing the business provider releases unused resources from the old
  business provider and invalidates only cached business entries.
- An in-flight result from a replaced business provider must not enter the new
  business cache.
- Framework cache entries remain valid across a business-provider swap.

## Verification

Runtime tests will demonstrate provider routing, cache isolation, framework
immutability through the public API, and in-flight business-load invalidation.
Focused launch tests will demonstrate that the default updater reports an
unconfigured state and that launch treats it as a local fallback rather than a
completed update. Existing resource-provider fallback tests remain green.

Unity EditMode/Runtime execution will run with a single Unity process and be
terminated after the run. Generated Unity metadata and lock-file noise will be
reviewed and removed before commit unless it is a deliberate source change.
