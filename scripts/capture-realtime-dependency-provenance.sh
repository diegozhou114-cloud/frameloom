#!/usr/bin/env bash
set -euo pipefail

first_match() {
  awk 'NR == 1 { first = $0 } END { print first }'
}

capture_dependency() {
  local name="$1" repo_url="$2" subtree="$3" lock="$4" license_out="$5" required_source="$6" required_api="$7"
  local work commit archive archive_sha license_path license_sha source_path
  work=$(mktemp -d)
  git -c http.version=HTTP/1.1 clone --filter=blob:none "$repo_url" "$work/repo"
  commit=$(git -C "$work/repo" rev-parse HEAD)
  source_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" |
    rg "$required_source" |
    first_match)
  test -n "$source_path"
  git -C "$work/repo" show "$commit:$source_path" | rg "$required_api" > /dev/null
  license_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" |
    rg '(^|/)(LICENSE|LICENSE\.md|LICENSE\.txt)$' |
    first_match)
  test -n "$license_path"
  archive="$work/source.tar"
  if [[ "$subtree" == "." ]]; then
    git -C "$work/repo" archive --format=tar "$commit" > "$archive"
  else
    git -C "$work/repo" archive --format=tar "$commit" "$subtree" > "$archive"
  fi
  archive_sha=$(shasum -a 256 "$archive" | awk '{print $1}')

  mkdir -p docs/dependencies ThirdPartyNotices
  git -C "$work/repo" show "$commit:$license_path" > "$license_out"
  license_sha=$(shasum -a 256 "$license_out" | awk '{print $1}')
  {
    printf 'UPSTREAM_URL=%s\n' "$repo_url"
    printf 'UPSTREAM_COMMIT=%s\n' "$commit"
    printf 'SOURCE_SUBTREE=%s\n' "$subtree"
    printf 'SOURCE_ARCHIVE_SHA256=%s\n' "$archive_sha"
    printf 'UPSTREAM_LICENSE_PATH=%s\n' "$license_path"
    printf 'LICENSE_SHA256=%s\n' "$license_sha"
    printf 'LICENSE_FILE=%s\n' "$license_out"
  } > "$lock"
  git -C "$work/repo" show --no-patch --format=fuller "$commit"
  rm -rf "$work"
  echo "Review $name commit, source archive hash, and verbatim license before import."
}

capture_dependency NativeWebSocket https://github.com/endel/NativeWebSocket.git . \
  docs/dependencies/nativewebsocket.lock ThirdPartyNotices/NativeWebSocket-LICENSE.txt \
  '^src/NativeWebSocket/WebSocket\.cs$' '(subProtocols|SubProtocol)'
capture_dependency kcp2k https://github.com/MirrorNetworking/kcp2k.git kcp2k \
  docs/dependencies/kcp2k.lock ThirdPartyNotices/kcp2k-LICENSE.txt \
  '(^|/)KcpClient\.cs$' 'TickIncoming'
