#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUTPUT="${MINISIEGE_OUTPUT:-$HOME/Desktop/MiniSiegeDemo}"
EVIDENCE="$OUTPUT/Verification"
APP="$OUTPUT/MiniSiege.app/Contents/MacOS/Mini Siege"
if [[ ! -x "$APP" || ! -f "$OUTPUT/Server/MiniSiegeHost.dll" ]]; then
  echo 'Build the macOS demo first.' >&2; exit 1
fi
if curl --silent --fail http://127.0.0.1:8787/health >/dev/null; then
  echo 'Stop the running demo server before automatic verification.' >&2; exit 1
fi
mkdir -p "$EVIDENCE"
dotnet "$OUTPUT/Server/MiniSiegeHost.dll" --urls http://127.0.0.1:8787 > "$EVIDENCE/server.log" 2>&1 &
SERVER_PID=$!
CLIENT_A_PID=""; CLIENT_B_PID=""
cleanup() {
  for demo_pid in "$CLIENT_A_PID" "$CLIENT_B_PID" "$SERVER_PID"; do
    if [[ -n "$demo_pid" ]]; then kill "$demo_pid" 2>/dev/null || true; fi
  done
}
trap cleanup EXIT INT TERM
for ((attempt=0; attempt<60; attempt++)); do
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$EVIDENCE/server.log" >&2; exit 1; fi
  if curl --silent --fail http://127.0.0.1:8787/health >/dev/null; then break; fi
  sleep 0.5
done
curl --silent --fail http://127.0.0.1:8787/health >/dev/null
for name in a b; do
  "$APP" -siegeAuto -siegeConnect -siegeQuitAfterMatch \
    -siegeEvidence "$EVIDENCE/client-$name.csv" -siegeScreenshot "$EVIDENCE/gameplay-$name.png" \
    -logFile "$EVIDENCE/client-$name.log" > "$EVIDENCE/stdout-$name.log" 2>&1 &
  if [[ "$name" == a ]]; then CLIENT_A_PID=$!; else CLIENT_B_PID=$!; fi
done
wait "$CLIENT_A_PID"
wait "$CLIENT_B_PID"
python3 - "$EVIDENCE" <<'PY'
import csv, json, pathlib, sys
root = pathlib.Path(sys.argv[1])
a = list(csv.DictReader((root / 'client-a.csv').open()))
b = list(csv.DictReader((root / 'client-b.csv').open()))
assert a and len(a) == len(b), 'Both clients must complete the same number of frames'
for index, (left, right) in enumerate(zip(a, b)):
    assert int(left['tick']) == index and int(right['tick']) == index, 'Missing or reordered frame'
    assert all(left[k] == right[k] for k in ('checksum', 'blueHP', 'redHP', 'winner')), f'Desync: {index}'
assert int(a[-1]['winner']) >= 0, 'The match must reach a terminal result'
for name in ('a', 'b'):
    log = (root / f'client-{name}.log').read_text()
    assert 'MATCH COMPLETE' in log and 'DESYNC' not in log, 'Client failed authority validation'
result = dict(passed=True, runtime='Two built Unity macOS clients + .NET 8 authority',
              verifiedFramesPerClient=len(a), everyFrameChecksumMatches=True,
              winner=int(a[-1]['winner']), finalChecksum=a[-1]['checksum'],
              blueHP=int(a[-1]['blueHP']), redHP=int(a[-1]['redHP']))
(root / 'macos-verification.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
PY
