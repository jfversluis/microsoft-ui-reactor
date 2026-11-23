#!/usr/bin/env bash
# Runs the MAUI renderer benchmark (Release) on one platform and saves its result lines.
#
#   tests/Reactor.Maui.Benchmark/run.sh maccatalyst
#   tests/Reactor.Maui.Benchmark/run.sh ios [simulator-udid]      (default: the booted simulator)
#   tests/Reactor.Maui.Benchmark/run.sh android [adb-serial]      (default: the only device)
#
# REACTOR_BENCH_MODE=anchor compares rows as components with and without a pass-through anchor
# per component (spec 067 §5) instead of the two renderers; output then goes to <platform>-anchor.*.
#
# Output: artifacts/maui-bench/<platform>.jsonl, one JSON object per REACTOR_BENCH line
# ({"kind":"ENV"|"RESULT", ...}). Fails if the run reports an error or does not finish.
set -euo pipefail

platform="${1:?usage: run.sh maccatalyst|ios|android [device]}"
device="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
project="$here/Reactor.Maui.Benchmark.csproj"
app_id="com.microsoft.reactor.maui.benchmark"
out="$root/artifacts/maui-bench"
name="$platform${REACTOR_BENCH_MODE:+-$REACTOR_BENCH_MODE}"
log="$out/$name.log"
timeout_seconds="${REACTOR_BENCH_TIMEOUT:-1200}"
mkdir -p "$out"
rm -f "$log"

with_timeout() { perl -e 'alarm shift; exec @ARGV' "$timeout_seconds" "$@"; }

case "$platform" in
  maccatalyst)
    dotnet build "$project" -f net11.0-maccatalyst -c Release
    # The universal bundle; the per-architecture one in a RID folder is not validly signed.
    app="$(ls -d "$here"/bin/Release/net11.0-maccatalyst/*.app | head -1)"
    with_timeout open -W -n "$app" --stderr "$log" --env "REACTOR_BENCH_MODE=${REACTOR_BENCH_MODE:-}" &
    opener=$!
    until grep -qE "REACTOR_BENCH_(DONE|ERROR)" "$log" 2>/dev/null; do
      kill -0 "$opener" 2>/dev/null || break
      sleep 5
    done
    pid="$(pgrep -f "$(basename "$app")/Contents/MacOS" || true)"
    [[ -n "$pid" ]] && kill $pid
    wait "$opener" || true
    ;;
  ios)
    if [[ -z "$device" ]]; then
      device="$(xcrun simctl list devices booted | sed -nE 's/.*\(([0-9A-F-]{36})\) \(Booted\).*/\1/p' | head -1)"
    fi
    [[ -n "$device" ]] || { echo "No booted iOS simulator; boot one or pass its UDID." >&2; exit 2; }
    arch="$( [[ "$(uname -m)" == "arm64" ]] && echo arm64 || echo x64 )"
    dotnet build "$project" -f net11.0-ios -r "iossimulator-$arch" -c Release
    app="$(ls -d "$here"/bin/Release/net11.0-ios/iossimulator-"$arch"/*.app | head -1)"
    xcrun simctl install "$device" "$app"
    SIMCTL_CHILD_REACTOR_BENCH_MODE="${REACTOR_BENCH_MODE:-}" xcrun simctl launch --console-pty --terminate-running-process "$device" "$app_id" > "$log" 2>&1 &
    launcher=$!
    deadline=$((SECONDS + timeout_seconds))
    until grep -qE "REACTOR_BENCH_(DONE|ERROR)" "$log" 2>/dev/null || (( SECONDS > deadline )); do sleep 5; done
    xcrun simctl terminate "$device" "$app_id" 2>/dev/null || true
    wait "$launcher" 2>/dev/null || true
    ;;
  android)
    adb_cmd=(adb)
    [[ -n "$device" ]] && adb_cmd+=(-s "$device")
    abi="$("${adb_cmd[@]}" shell getprop ro.product.cpu.abi | tr -d '\r')"
    case "$abi" in
      arm64-v8a) rid=android-arm64 ;;
      x86_64) rid=android-x64 ;;
      *) echo "Unsupported device ABI '$abi'." >&2; exit 2 ;;
    esac
    dotnet build "$project" -f net11.0-android -r "$rid" -c Release
    "${adb_cmd[@]}" install -r "$here/bin/Release/net11.0-android/$rid/$app_id-Signed.apk"
    activity="$("${adb_cmd[@]}" shell cmd package resolve-activity --brief "$app_id" | tail -1 | tr -d '\r')"
    "${adb_cmd[@]}" logcat -c
    "${adb_cmd[@]}" shell am start -n "$activity" ${REACTOR_BENCH_MODE:+-e REACTOR_BENCH_MODE "$REACTOR_BENCH_MODE"} > /dev/null
    deadline=$((SECONDS + timeout_seconds))
    until "${adb_cmd[@]}" logcat -d -s DOTNET:I | grep -qE "REACTOR_BENCH_(DONE|ERROR)" || (( SECONDS > deadline )); do sleep 10; done
    "${adb_cmd[@]}" logcat -d -s DOTNET:I > "$log"
    "${adb_cmd[@]}" shell am force-stop "$app_id"
    ;;
  *)
    echo "Unknown platform '$platform'." >&2
    exit 2
    ;;
esac

perl -ne 'print qq({"kind":"$1",) . substr($2, 1) . "\n" if /REACTOR_BENCH_(ENV|RESULT) (\{.*\})\s*$/' "$log" > "$out/$name.jsonl"
if grep -q "REACTOR_BENCH_ERROR" "$log" || ! grep -q "REACTOR_BENCH_DONE" "$log"; then
  echo "Benchmark failed or did not finish on $platform; see $log." >&2
  exit 1
fi
cat "$out/$name.jsonl"
