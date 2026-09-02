# Third-Party Notices

This notice covers third-party material distributed in the FrameLoom Unity package. Package-relative
paths below start at `Packages/com.zwebglframework.runtime`. FrameLoom's MIT license does not replace
or expand the licenses identified here.

## Kcp2k
- Upstream: https://github.com/MirrorNetworking/kcp2k
- Revision: 66efda6686f649838d42f078fbaabf56ac449de4
- License: MIT
- Copyright: Copyright (c) 2016 limpo1989; Copyright (c) 2020 Paul Pacheco; Copyright (c) 2020 Lymdun; Copyright (c) 2020 vis2k
- Modifications: Upstream source unchanged; package integration metadata added.
- License path: ThirdParty/Kcp2k/LICENSE.txt
- Distribution: ThirdParty/Kcp2k

## NativeWebSocket
- Upstream: https://github.com/endel/NativeWebSocket
- Revision: cbbf04d42522d5f5b466224a0b8e3936676f9b32
- License: Apache-2.0 baseline with file-specific MIT notice
- Copyright: Copyright 2019 Endel Dreyer; Copyright 2018 Jiri Hybek
- Modifications: WebSocket.cs conditional wrapper and package-local asmdefs/metadata added.
- License path: ThirdParty/NativeWebSocket/LICENSE.txt
- Distribution: ThirdParty/NativeWebSocket
- Apache-2.0 baseline: repository-root LICENSE.txt
- MIT file: ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/WebSocket.jspre
- MIT metadata conflicts: ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/package.json; ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj
- FrameLoom relicensing: none

The retained root Apache-2.0 license is the owner-reviewed repository-wide distribution baseline.
`WebSocket.jspre` remains under its file-specific MIT notice. The nested Unity `package.json` and
`NativeWebSocket.csproj` MIT declarations are preserved metadata conflicts; neither relicenses other
NativeWebSocket files nor establishes FrameLoom MIT authority over vendored content.

## UniTask
- Upstream: https://github.com/Cysharp/UniTask
- Revision: e5acc106ee196bc5a32fb14cdf2987b0f96d11e0
- License: MIT
- Copyright: Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.
- Modifications: Not vendored or modified; consumer installs directly.
- License path: Upstream repository LICENSE
- Distribution: Not distributed in the FrameLoom package or declared in its package.json
- Version: 2.5.11

Consumers install UniTask directly through its pinned Git URL because Unity 2022.3 package manifests
cannot express Git dependencies transitively.
