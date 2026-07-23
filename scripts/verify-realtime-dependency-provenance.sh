#!/usr/bin/env bash
set -euo pipefail

verify_dependency() {
  local name="$1" lock="$2" expected_url="$3" expected_subtree="$4" expected_license="$5"
  test -s "$lock" || { echo "missing $name lock" >&2; return 1; }
  unset UPSTREAM_URL UPSTREAM_COMMIT SOURCE_SUBTREE SOURCE_ARCHIVE_SHA256 \
    UPSTREAM_LICENSE_PATH LICENSE_SHA256 LICENSE_FILE
  source "$lock"
  [[ "$UPSTREAM_URL" == "$expected_url" ]]
  [[ "$UPSTREAM_COMMIT" =~ ^[0-9a-f]{40}$ ]]
  [[ "$SOURCE_SUBTREE" == "$expected_subtree" ]]
  [[ "$SOURCE_ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$UPSTREAM_LICENSE_PATH" =~ (^|/)(LICENSE|LICENSE\.md|LICENSE\.txt)$ ]]
  [[ "$LICENSE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$LICENSE_FILE" == "$expected_license" ]]
  test -s "$LICENSE_FILE"
  [[ "$(shasum -a 256 "$LICENSE_FILE" | awk '{print $1}')" == "$LICENSE_SHA256" ]]

  local work archive actual
  work=$(mktemp -d)
  git -c http.version=HTTP/1.1 clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
  git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
  archive="$work/source.tar"
  if [[ "$SOURCE_SUBTREE" == "." ]]; then
    git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" > "$archive"
  else
    git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" "$SOURCE_SUBTREE" > "$archive"
  fi
  actual=$(shasum -a 256 "$archive" | awk '{print $1}')
  [[ "$actual" == "$SOURCE_ARCHIVE_SHA256" ]]
  git -C "$work/repo" show "$UPSTREAM_COMMIT:$UPSTREAM_LICENSE_PATH" > "$work/license"
  cmp -s "$work/license" "$LICENSE_FILE"
  rm -rf "$work"
  echo "$name provenance verified at $UPSTREAM_COMMIT"
}

verify_dependency NativeWebSocket docs/dependencies/nativewebsocket.lock \
  https://github.com/endel/NativeWebSocket.git . ThirdPartyNotices/NativeWebSocket-LICENSE.txt
verify_dependency kcp2k docs/dependencies/kcp2k.lock \
  https://github.com/MirrorNetworking/kcp2k.git kcp2k ThirdPartyNotices/kcp2k-LICENSE.txt
