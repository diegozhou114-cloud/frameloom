#!/usr/bin/env bash
set -euo pipefail

repo_root="${1:-$(pwd)}"
assets_root="$repo_root/Assets"
failures=0

if [[ ! -d "$assets_root" ]]; then
  printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] FAIL missing Assets directory: %s\n' "$assets_root"
  exit 1
fi

while IFS= read -r path; do
  [[ "$path" == "$assets_root" ]] && continue
  relative="${path#"$assets_root"/}"
  basename="${path##*/}"
  [[ "$basename" == .* ]] && continue
  [[ "$path" == *.meta ]] && continue
  if [[ ! -f "$path.meta" ]]; then
    printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] missing-meta Assets/%s\n' "$relative"
    failures=$((failures + 1))
  fi
done < <(find "$assets_root" \( -type f -o -type d \) -print | LC_ALL=C sort)

while IFS= read -r meta; do
  target="${meta%.meta}"
  if [[ ! -e "$target" ]]; then
    printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] orphan-meta %s\n' "${meta#"$repo_root"/}"
    failures=$((failures + 1))
  fi
done < <(find "$assets_root" -type f -name '*.meta' -print | LC_ALL=C sort)

if (( failures > 0 )); then
  printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] FAIL count=%d\n' "$failures"
  exit 1
fi

printf '[RepositoryMetaAudit_HasNoMissingOrOrphanedMeta] PASS count=0\n'
