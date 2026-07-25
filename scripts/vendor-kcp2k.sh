#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

bash scripts/verify-realtime-dependency-provenance.sh

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

parse_lock docs/dependencies/kcp2k.lock
[[ "$UPSTREAM_URL" == "https://github.com/MirrorNetworking/kcp2k.git" ]]
[[ "$UPSTREAM_COMMIT" =~ ^[0-9a-f]{40}$ ]]
[[ "$SOURCE_SUBTREE" == "kcp2k" ]]
[[ "$SOURCE_ARCHIVE_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$UPSTREAM_LICENSE_PATH" == "LICENSE" ]]
[[ "$LICENSE_SHA256" =~ ^[0-9a-f]{64}$ ]]
[[ "$LICENSE_FILE" == "ThirdPartyNotices/kcp2k-LICENSE.txt" ]]

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

git -c http.version=HTTP/1.1 clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
git -C "$work/repo" config http.version HTTP/1.1
git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" "$SOURCE_SUBTREE" > "$work/kcp2k.tar"
actual=$(shasum -a 256 "$work/kcp2k.tar" | awk '{print $1}')
[[ "$actual" == "$SOURCE_ARCHIVE_SHA256" ]]

tar -xf "$work/kcp2k.tar" -C "$work"
test -d "$work/$SOURCE_SUBTREE/kcp2k"
mkdir -p "$work/Upstream"
cp -R "$work/$SOURCE_SUBTREE/kcp2k/." "$work/Upstream/"
find "$work/Upstream" -name '*.asmdef' -delete
find "$work/Upstream" -name '*.meta' -delete

package_root=Packages/com.zwebglframework.kcp2k
upstream_root="$package_root/Runtime/Upstream"
metadata_root="$work/metadata"
if [[ -d "$upstream_root" ]]; then
  (
    cd "$upstream_root"
    find . -name '*.meta' -print | while IFS= read -r metadata; do
      mkdir -p "$metadata_root/$(dirname "$metadata")"
      cp "$metadata" "$metadata_root/$metadata"
    done
  )
fi

rm -rf "$package_root/Runtime/Upstream"
mkdir -p "$package_root/Runtime"
cp -R "$work/Upstream" "$package_root/Runtime/Upstream"
if [[ -d "$metadata_root" ]]; then
  (
    cd "$metadata_root"
    find . -name '*.meta' -print | while IFS= read -r metadata; do
      asset=${metadata%.meta}
      if [[ -e "$repo_root/$upstream_root/$asset" ]]; then
        mkdir -p "$repo_root/$upstream_root/$(dirname "$metadata")"
        cp "$metadata" "$repo_root/$upstream_root/$metadata"
      fi
    done
  )
fi
cp "$LICENSE_FILE" "$package_root/LICENSE.txt"

printf '%s\n' \
  '{' \
  '  "name": "com.zwebglframework.kcp2k",' \
  '  "version": "0.0.0",' \
  '  "displayName": "ZWebGLFrameWork Vendored kcp2k",' \
  '  "unity": "2022.3",' \
  '  "license": "SEE LICENSE IN LICENSE.txt"' \
  '}' > "$package_root/package.json"
