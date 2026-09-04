#!/usr/bin/env bash
set -euo pipefail

test -x scripts/vendor-nativewebsocket.sh
rg -q 'src/NativeWebSocket' scripts/vendor-nativewebsocket.sh
rg -q 'integrations/Unity' scripts/vendor-nativewebsocket.sh
rg -q 'SOURCE_ARCHIVE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'LICENSE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'colyseus.nativewebsocket' scripts/vendor-nativewebsocket.sh
rg -q 'ZWebGLFrameWork.NativeWebSocket.Core' \
  Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/Runtime/Upstream/integrations/Unity/colyseus.nativewebsocket.asmdef
rg -q '^#if !UNITY_WEBGL' \
  Packages/com.zwebglframework.runtime/ThirdParty/NativeWebSocket/Runtime/Upstream/src/NativeWebSocket/WebSocket.cs
rg -q 'Apache-2.0' docs/dependencies/README.md
node -e 'const text=require("fs").readFileSync(process.argv[1],"utf8").replace(/\s+/g," ");const expected="The MIT declarations in `Runtime/Upstream/integrations/Unity/package.json` and `Runtime/Upstream/src/NativeWebSocket/NativeWebSocket.csproj` are preserved metadata conflicts.";if(!text.includes(expected))process.exit(1)' docs/dependencies/README.md
