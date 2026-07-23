#!/usr/bin/env bash
set -euo pipefail

work_root=$(mktemp -d)

cleanup() {
  rm -rf "${work_root:-}"
}

trap cleanup EXIT

invalid_lock() {
  echo "invalid dependency lock: $1" >&2
  return 1
}

parse_lock() {
  local lock="$1" line expected_key value count=0
  unset UPSTREAM_URL UPSTREAM_COMMIT SOURCE_SUBTREE SOURCE_ARCHIVE_SHA256 \
    UPSTREAM_LICENSE_PATH LICENSE_SHA256 LICENSE_FILE

  while IFS= read -r line || [[ -n "$line" ]]; do
    count=$((count + 1))
    case "$count" in
      1) expected_key=UPSTREAM_URL ;;
      2) expected_key=UPSTREAM_COMMIT ;;
      3) expected_key=SOURCE_SUBTREE ;;
      4) expected_key=SOURCE_ARCHIVE_SHA256 ;;
      5) expected_key=UPSTREAM_LICENSE_PATH ;;
      6) expected_key=LICENSE_SHA256 ;;
      7) expected_key=LICENSE_FILE ;;
      *) invalid_lock "$lock has extra lines"; return 1 ;;
    esac
    [[ "$line" == "$expected_key="* ]] || {
      invalid_lock "$lock has an unexpected key order"; return 1;
    }
    value=${line#"$expected_key="}
    [[ "$value" =~ ^[A-Za-z0-9._:/-]+$ ]] || {
      invalid_lock "$lock has an unsafe value"; return 1;
    }
    case "$expected_key" in
      UPSTREAM_URL) UPSTREAM_URL=$value ;;
      UPSTREAM_COMMIT) UPSTREAM_COMMIT=$value ;;
      SOURCE_SUBTREE) SOURCE_SUBTREE=$value ;;
      SOURCE_ARCHIVE_SHA256) SOURCE_ARCHIVE_SHA256=$value ;;
      UPSTREAM_LICENSE_PATH) UPSTREAM_LICENSE_PATH=$value ;;
      LICENSE_SHA256) LICENSE_SHA256=$value ;;
      LICENSE_FILE) LICENSE_FILE=$value ;;
    esac
  done < "$lock"

  [[ "$count" -eq 7 ]] || {
    invalid_lock "$lock is missing required lines"; return 1;
  }
}

verify_dependency() {
  local name="$1" lock="$2" expected_url="$3" expected_subtree="$4" expected_license="$5"
  local work archive actual
  test -s "$lock" || { echo "missing $name lock" >&2; return 1; }
  parse_lock "$lock"
  [[ "$UPSTREAM_URL" == "$expected_url" ]]
  [[ "$UPSTREAM_COMMIT" =~ ^[0-9a-f]{40}$ ]]
  [[ "$SOURCE_SUBTREE" == "$expected_subtree" ]]
  [[ "$SOURCE_ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$UPSTREAM_LICENSE_PATH" == LICENSE ]]
  [[ "$LICENSE_SHA256" =~ ^[0-9a-f]{64}$ ]]
  [[ "$LICENSE_FILE" == "$expected_license" ]]
  test -s "$LICENSE_FILE"
  [[ "$(shasum -a 256 "$LICENSE_FILE" | awk '{print $1}')" == "$LICENSE_SHA256" ]]

  work="$work_root/$name"
  mkdir -p "$work"
  git -c http.version=HTTP/1.1 clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
  git -C "$work/repo" config http.version HTTP/1.1
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
  echo "$name provenance verified at $UPSTREAM_COMMIT"
}

verify_dependency NativeWebSocket docs/dependencies/nativewebsocket.lock \
  https://github.com/endel/NativeWebSocket.git . ThirdPartyNotices/NativeWebSocket-LICENSE.txt
verify_dependency kcp2k docs/dependencies/kcp2k.lock \
  https://github.com/MirrorNetworking/kcp2k.git kcp2k ThirdPartyNotices/kcp2k-LICENSE.txt
