# Third-Party Notices

This repository notice includes every package entry and separately identifies direct dependencies
used only by the host project, validation tooling, CI, and Server tests. Package-relative paths in
the shared entries start at `Packages/com.zwebglframework.runtime`.

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

## Repository-only dependencies
- Unity host: Unity 2022.3.62f3c1 packages locked by Packages/manifest.json and Packages/packages-lock.json, including com.coplaydev.unity-mcp 10.1.2 at 4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50 (MIT), com.unity.ide.rider 3.0.40 (MIT), and com.unity.physics 1.3.15 (Unity Companion License).
- CI: @playwright/test 1.61.1 (Apache-2.0) and yaml 2.9.0 (ISC), locked by ci/package-lock.json.
- Server tests: Microsoft.NET.Test.Sdk 17.11.1, NUnit 4.2.2, and NUnit3TestAdapter 4.6.0 (MIT), declared in Server test projects.
- Workflows: actions/checkout@v4, actions/setup-node@v4, actions/cache@v4, actions/setup-dotnet@v4, game-ci/unity-test-runner@v4, game-ci/unity-builder@v4, actions/upload-artifact@v4, and actions/download-artifact@v4.
- Scope: Host validation and repository tooling only; these dependencies are not distributed in the FrameLoom package. Each remains under its upstream terms, and the named lockfiles own exact resolved closure.

## Contributor Covenant
- Upstream: https://www.contributor-covenant.org/version/2/1/code_of_conduct/
- Revision: 2.1
- License: CC BY 4.0
- Copyright: Contributor Covenant contributors
- Modifications: Approved private contact only; core text unchanged.
- License path: https://creativecommons.org/licenses/by/4.0/
- Distribution: Repository CODE_OF_CONDUCT.md only

CC BY 4.0 applies only to `CODE_OF_CONDUCT.md`; it does not change the MIT license for project-owned
code and documentation, and Contributor Covenant is not distributed in the FrameLoom package.
