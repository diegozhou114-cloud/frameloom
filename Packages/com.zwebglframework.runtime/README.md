# FrameLoom

FrameLoom is a deterministic Unity strategy-game framework distributed as one Git-installable Unity
package.

## Requirements

- Unity 2022.3 LTS. FrameLoom is tested on 2022.3.62f3c1; not every 2022.3 patch is tested.
- Consumers must directly pin UniTask to `e5acc106ee196bc5a32fb14cdf2987b0f96d11e0`.

Unity 2022.3 package manifests cannot express Git dependencies transitively. Add UniTask directly to
the consuming project's manifest with this immutable URL:

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#e5acc106ee196bc5a32fb14cdf2987b0f96d11e0"
```

## Buffered output APIs

`SessionPresentationBridge.Apply(SimPresentationOutputBuffer frame, long revision)` synchronously
borrows presentation data from a published `SimTickOutputBuffer`. Pass `output.Presentation` with the
matching `output.Revision` while the parent output is valid. The bridge consumes all borrowed
collections before returning and retains only interpolation value copies and bridge-owned views.
The parent output may be cleared, reused, or disposed after `Apply` returns; later `Sample` calls
remain valid.

`CommandRecorder.Record(SimTickOutputBuffer output)` synchronously creates owning copies of the
published commands and tick result. Recorded data remains valid after the parent output is cleared,
reused, or disposed. Recording is an allocating capture path and is outside zero-allocation gates.

Both APIs reject invalid or unpublished output according to their documented exceptions. Use them
with output published by `IBufferedSimBattleSession.TickInto`.

## Validation scope

The package's Unity editor validation targets 2022.3.62f3c1. WebGL IL2CPP managed allocation is not
measured by editor evidence, and Git-host consumer resolution is validated separately during the
release process.
