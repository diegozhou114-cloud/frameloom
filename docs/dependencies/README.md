## NativeWebSocket Licensing Classification

For pinned revision `cbbf04d42522d5f5b466224a0b8e3936676f9b32`, the repository-root
Apache-2.0 license is the owner-reviewed repository-wide distribution baseline.
`Runtime/Upstream/integrations/Unity/WebSocket.jspre` remains under its file-specific MIT notice.

The MIT declarations in `Runtime/Upstream/integrations/Unity/package.json` and
`Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj` are preserved metadata conflicts.
They do not relicense other NativeWebSocket files. None grants FrameLoom MIT rights over vendored
content, and no upstream license, header, declaration, or source file is rewritten by this decision.

## Optional YooAsset integration

`com.zwebglframework.yooasset` adapts YooAsset 2.3.17 for the Business resource layer.
The host manifest pins upstream revision `101236db8a58a327071ff1ab35d28bc1535ca1c1`
with UPM path `Assets/YooAsset`; the core runtime package does not depend on YooAsset.
The adapter reuses the existing UniTask 2.5.11 dependency. Consumers install both Git
dependencies directly because UPM package metadata cannot resolve Git dependencies transitively.
YooAsset retains its Apache-2.0 license. See the root third-party notice and the
[adapter README](../../Packages/com.zwebglframework.yooasset/README.md) for setup and ownership.
