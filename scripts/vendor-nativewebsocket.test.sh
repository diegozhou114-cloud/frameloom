#!/usr/bin/env bash
set -euo pipefail

test -x scripts/vendor-nativewebsocket.sh
rg -q 'src/NativeWebSocket' scripts/vendor-nativewebsocket.sh
rg -q 'integrations/Unity' scripts/vendor-nativewebsocket.sh
rg -q 'SOURCE_ARCHIVE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'LICENSE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'colyseus.nativewebsocket' scripts/vendor-nativewebsocket.sh
rg -q 'Apache-2.0' docs/dependencies/README.md
rg -q 'integrations/Unity/package.json.*MIT' docs/dependencies/README.md
