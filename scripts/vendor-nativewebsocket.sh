#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/.." && pwd)
cd "$repo_root"
lock=docs/dependencies/nativewebsocket.lock
test -s "$lock"

expected=(UPSTREAM_URL UPSTREAM_COMMIT SOURCE_SUBTREE SOURCE_ARCHIVE_SHA256 \
  UPSTREAM_LICENSE_PATH LICENSE_SHA256 LICENSE_FILE)
values=()
index=0
while IFS= read -r line || [[ -n "$line" ]]; do
  [[ "$index" -lt "${#expected[@]}" ]]
  key=${expected[$index]}
  [[ "$line" == "$key="* ]]
  value=${line#"$key="}
  [[ "$value" =~ ^[A-Za-z0-9._:/-]+$ ]]
  values+=("$value")
  index=$((index + 1))
done < "$lock"
[[ "$index" -eq 7 ]]

UPSTREAM_URL=${values[0]}
UPSTREAM_COMMIT=${values[1]}
SOURCE_SUBTREE=${values[2]}
SOURCE_ARCHIVE_SHA256=${values[3]}
UPSTREAM_LICENSE_PATH=${values[4]}
LICENSE_SHA256=${values[5]}
LICENSE_FILE=${values[6]}
[[ "$UPSTREAM_URL" == https://github.com/endel/NativeWebSocket.git ]]
[[ "$UPSTREAM_COMMIT" == cbbf04d42522d5f5b466224a0b8e3936676f9b32 ]]
[[ "$SOURCE_SUBTREE" == . ]]
[[ "$SOURCE_ARCHIVE_SHA256" == b89e5afd349b091c7861993fa4267fa5e1423bd64fe1187936c082ff92d46538 ]]
[[ "$UPSTREAM_LICENSE_PATH" == LICENSE ]]
[[ "$(shasum -a 256 "$LICENSE_FILE" | awk '{print $1}')" == "$LICENSE_SHA256" ]]

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
git -c http.version=HTTP/1.1 clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" > "$work/source.tar"
[[ "$(shasum -a 256 "$work/source.tar" | awk '{print $1}')" == "$SOURCE_ARCHIVE_SHA256" ]]
git -C "$work/repo" show "$UPSTREAM_COMMIT:$UPSTREAM_LICENSE_PATH" > "$work/LICENSE"
cmp -s "$work/LICENSE" "$LICENSE_FILE"
git -C "$work/repo" archive "$UPSTREAM_COMMIT" \
  src/NativeWebSocket integrations/Unity | tar -x -C "$work"

package=Packages/com.zwebglframework.nativewebsocket
saved_meta="$work/meta"
if [[ -d "$package" ]]; then
  (cd "$package" && find . -name '*.meta' -print | while IFS= read -r file; do
    mkdir -p "$saved_meta/$(dirname "$file")"; cp "$file" "$saved_meta/$file"; done)
fi
rm -rf "$package"
mkdir -p "$package/Runtime/Upstream/src" "$package/Runtime/Upstream/integrations"
cp -R "$work/src/NativeWebSocket" "$package/Runtime/Upstream/src/"
cp -R "$work/integrations/Unity" "$package/Runtime/Upstream/integrations/"
cp "$LICENSE_FILE" "$package/LICENSE.txt"
native_websocket="$package/Runtime/Upstream/src/NativeWebSocket/WebSocket.cs"
native_websocket_source=$(<"$native_websocket")
printf '#if !UNITY_WEBGL || UNITY_EDITOR\n%s\n#endif\n' "$native_websocket_source" > "$native_websocket"
cat > "$package/Runtime/Upstream/src/NativeWebSocket/ZWebGLFrameWork.NativeWebSocket.Core.asmdef" <<'EOF'
{
  "name": "ZWebGLFrameWork.NativeWebSocket.Core",
  "references": [],
  "includePlatforms": ["Editor", "WebGL"],
  "autoReferenced": false
}
EOF
cat > "$package/Runtime/Upstream/integrations/Unity/colyseus.nativewebsocket.asmdef" <<'EOF'
{
  "name": "colyseus.nativewebsocket",
  "references": ["ZWebGLFrameWork.NativeWebSocket.Core"],
  "includePlatforms": ["Editor", "WebGL"]
}
EOF
printf '%s\n' '{' \
  '  "name": "com.zwebglframework.nativewebsocket",' \
  '  "version": "0.0.0",' \
  '  "displayName": "ZWebGLFrameWork Vendored NativeWebSocket",' \
  '  "unity": "2022.3",' \
  '  "license": "SEE LICENSE IN LICENSE.txt"' \
  '}' > "$package/package.json"
if [[ -d "$saved_meta" ]]; then
  (cd "$saved_meta" && find . -name '*.meta' -print | while IFS= read -r file; do
    asset=${file%.meta}; [[ -e "$repo_root/$package/$asset" ]] || continue
    mkdir -p "$repo_root/$package/$(dirname "$file")"; cp "$file" "$repo_root/$package/$file"; done)
fi
rg -q '"name"[[:space:]]*:[[:space:]]*"colyseus.nativewebsocket"' \
  "$package/Runtime/Upstream/integrations/Unity"
