#!/usr/bin/env bash
set -euo pipefail

work_root=$(mktemp -d)
stage_root="$work_root/staged"
mkdir -p "$stage_root"

publish_targets=(
  docs/dependencies/nativewebsocket.lock
  ThirdPartyNotices/NativeWebSocket-LICENSE.txt
  docs/dependencies/kcp2k.lock
  ThirdPartyNotices/kcp2k-LICENSE.txt
)
stage_files=(
  "$stage_root/NativeWebSocket.lock"
  "$stage_root/NativeWebSocket-license.txt"
  "$stage_root/kcp2k.lock"
  "$stage_root/kcp2k-license.txt"
)
publish_temps=("" "" "" "")
rollback_files=("" "" "" "")
target_existed=(0 0 0 0)
rollback_failed=(0 0 0 0)
publish_started=0
publish_complete=0

cleanup() {
  local exit_code=$? i
  trap - EXIT
  trap - HUP INT TERM
  set +e

  if [[ "$publish_started" -eq 1 && "$publish_complete" -eq 0 ]]; then
    for ((i = 0; i < 4; i++)); do
      if [[ "${target_existed[$i]}" -eq 1 ]]; then
        if test -n "${rollback_files[$i]}" && test -e "${rollback_files[$i]}"; then
          if ! mv -f "${rollback_files[$i]}" "${publish_targets[$i]}"; then
            rollback_failed[$i]=1
            echo "rollback failed; backup retained at ${rollback_files[$i]}" >&2
          fi
        else
          rollback_failed[$i]=1
          echo "rollback failed; backup missing at ${rollback_files[$i]}" >&2
        fi
      else
        if ! rm -f "${publish_targets[$i]}"; then
          rollback_failed[$i]=1
          echo "rollback failed to remove new target ${publish_targets[$i]}; state file retained at ${rollback_files[$i]}" >&2
        fi
      fi
    done
  fi

  for ((i = 0; i < 4; i++)); do
    if test -n "${publish_temps[$i]}"; then
      rm -f "${publish_temps[$i]}"
    fi
    if test -n "${rollback_files[$i]}" && [[ "${rollback_failed[$i]}" -eq 0 ]]; then
      rm -f "${rollback_files[$i]}"
    fi
  done
  rm -rf "${work_root:-}"
  for ((i = 0; i < 4; i++)); do
    if [[ "${rollback_failed[$i]}" -eq 1 && "$exit_code" -eq 0 ]]; then
      exit_code=1
    fi
  done
  exit "$exit_code"
}

trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

first_match() {
  awk 'NR == 1 { first = $0 } END { print first }'
}

validate_staged_dependency() {
  local lock="$1" license="$2" repo_url="$3" commit="$4" subtree="$5" archive_sha="$6" license_path="$7" license_sha="$8" license_out="$9"
  [[ "$(wc -l < "$lock" | tr -d ' ')" == 7 ]]
  [[ "$(sed -n '1p' "$lock")" == "UPSTREAM_URL=$repo_url" ]]
  [[ "$(sed -n '2p' "$lock")" == "UPSTREAM_COMMIT=$commit" ]]
  [[ "$(sed -n '3p' "$lock")" == "SOURCE_SUBTREE=$subtree" ]]
  [[ "$(sed -n '4p' "$lock")" == "SOURCE_ARCHIVE_SHA256=$archive_sha" ]]
  [[ "$(sed -n '5p' "$lock")" == "UPSTREAM_LICENSE_PATH=$license_path" ]]
  [[ "$(sed -n '6p' "$lock")" == "LICENSE_SHA256=$license_sha" ]]
  [[ "$(sed -n '7p' "$lock")" == "LICENSE_FILE=$license_out" ]]
  test -s "$license"
  [[ "$(shasum -a 256 "$license" | awk '{print $1}')" == "$license_sha" ]]
}

capture_dependency() {
  local name="$1" repo_url="$2" subtree="$3" lock="$4" license_out="$5" required_source="$6" required_api="$7"
  local work commit archive archive_sha license_path license_sha source_path stage_lock stage_license
  work="$work_root/$name"
  stage_lock="$stage_root/$name.lock"
  stage_license="$stage_root/$name-license.txt"
  mkdir -p "$work"
  git -c http.version=HTTP/1.1 clone --filter=blob:none "$repo_url" "$work/repo"
  git -C "$work/repo" config http.version HTTP/1.1
  commit=$(git -C "$work/repo" rev-parse HEAD)
  source_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" |
    rg "$required_source" |
    first_match)
  test -n "$source_path"
  git -C "$work/repo" show "$commit:$source_path" | rg "$required_api" > /dev/null
  license_path=$(git -C "$work/repo" ls-tree -r --name-only "$commit" |
    rg '(^|/)(LICENSE|LICENSE\.md|LICENSE\.txt)$' |
    first_match)
  [[ "$license_path" == LICENSE ]]
  archive="$work/source.tar"
  if [[ "$subtree" == "." ]]; then
    git -C "$work/repo" archive --format=tar "$commit" > "$archive"
  else
    git -C "$work/repo" archive --format=tar "$commit" "$subtree" > "$archive"
  fi
  archive_sha=$(shasum -a 256 "$archive" | awk '{print $1}')

  git -C "$work/repo" show "$commit:$license_path" > "$stage_license"
  license_sha=$(shasum -a 256 "$stage_license" | awk '{print $1}')
  {
    printf 'UPSTREAM_URL=%s\n' "$repo_url"
    printf 'UPSTREAM_COMMIT=%s\n' "$commit"
    printf 'SOURCE_SUBTREE=%s\n' "$subtree"
    printf 'SOURCE_ARCHIVE_SHA256=%s\n' "$archive_sha"
    printf 'UPSTREAM_LICENSE_PATH=%s\n' "$license_path"
    printf 'LICENSE_SHA256=%s\n' "$license_sha"
    printf 'LICENSE_FILE=%s\n' "$license_out"
  } > "$stage_lock"
  validate_staged_dependency "$stage_lock" "$stage_license" "$repo_url" "$commit" \
    "$subtree" "$archive_sha" "$license_path" "$license_sha" "$license_out"
  git -C "$work/repo" show --no-patch --format=fuller "$commit"
  echo "Review $name commit, source archive hash, and verbatim license before import."
}

prepare_publication() {
  local i

  for ((i = 0; i < 4; i++)); do
    rollback_files[$i]=$(mktemp "${publish_targets[$i]}.rollback.XXXXXX")
    if test -e "${publish_targets[$i]}"; then
      target_existed[$i]=1
      cp -p "${publish_targets[$i]}" "${rollback_files[$i]}"
      cmp -s "${publish_targets[$i]}" "${rollback_files[$i]}"
    fi
  done

  for ((i = 0; i < 4; i++)); do
    publish_temps[$i]=$(mktemp "${publish_targets[$i]}.publish.XXXXXX")
    cp -p "${stage_files[$i]}" "${publish_temps[$i]}"
    cmp -s "${stage_files[$i]}" "${publish_temps[$i]}"
  done
}

publish_all() {
  local i
  publish_started=1
  for ((i = 0; i < 4; i++)); do
    mv -f "${publish_temps[$i]}" "${publish_targets[$i]}"
  done
  publish_complete=1
}

capture_dependency NativeWebSocket https://github.com/endel/NativeWebSocket.git . \
  docs/dependencies/nativewebsocket.lock ThirdPartyNotices/NativeWebSocket-LICENSE.txt \
  '^src/NativeWebSocket/WebSocket\.cs$' '(subProtocols|SubProtocol)'
capture_dependency kcp2k https://github.com/MirrorNetworking/kcp2k.git kcp2k \
  docs/dependencies/kcp2k.lock ThirdPartyNotices/kcp2k-LICENSE.txt \
  '(^|/)KcpClient\.cs$' 'TickIncoming'

mkdir -p docs/dependencies ThirdPartyNotices
prepare_publication
publish_all
