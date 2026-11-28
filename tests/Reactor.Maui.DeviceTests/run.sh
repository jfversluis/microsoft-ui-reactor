#!/usr/bin/env bash
# Runs the Reactor MAUI device tests on one platform and exits non-zero on any failure.
#
#   tests/Reactor.Maui.DeviceTests/run.sh maccatalyst
#   tests/Reactor.Maui.DeviceTests/run.sh ios [simulator-udid]      (default: the booted simulator)
#   tests/Reactor.Maui.DeviceTests/run.sh android [adb-serial]      (default: the only device)
#
# REACTOR_DEVICE_TEST_FILTER=<substring>[|<substring>...] runs only matching fixtures (and on-demand ones it names).
# REACTOR_HOST_GESTURES=1 (iOS) tells fixtures that host-gestures.py is performing their REACTOR-GESTURE drags.
# On Android, a fixture that logs REACTOR-KEY-BACK gets a real back key (adb shell input keyevent 4):
# an app can't inject key events into itself.
# The TAP report is written to artifacts/device-tests/<platform>.tap.
set -euo pipefail

platform="${1:?usage: run.sh maccatalyst|ios|android [device]}"
device="${2:-}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
project="$here/Reactor.Maui.DeviceTests.csproj"
app_id="com.microsoft.reactor.maui.devicetests"
out="$root/artifacts/device-tests"
tap="$out/$platform.tap"
# The whole run, from launch to the done marker. The suite is over 200 fixtures, so this leaves room
# for a slow CI simulator.
timeout_seconds="${REACTOR_DEVICE_TEST_TIMEOUT:-1500}"
launched=$SECONDS
done_marker="# REACTOR-DEVICE-TESTS-DONE"
mkdir -p "$out"

# Runs a command with a time limit (macOS has no timeout(1)).
with_timeout() { launched=$SECONDS; perl -e 'alarm shift; exec @ARGV' "$timeout_seconds" "$@"; }

# Keeps the TAP lines, dropping the NSLog ("date time App[pid:tid] ") or logcat ("... DOTNET : ")
# prefix in front of them.
extract_tap() {
  perl -ne 's/^\d{4}-\d\d-\d\d [\d:.]+ \S+\[\d+:\d+\] //; s/^.*?\bDOTNET\s*: //; print if /^((not )?ok \d+|#|1\.\.\d+)/'
}

case "$platform" in
  maccatalyst)
    dotnet build "$project" -f net11.0-maccatalyst -c Debug
    app="$(ls -d "$here"/bin/Debug/net11.0-maccatalyst/*/*.app | head -1)"
    log="$out/maccatalyst.log"
    rm -f "$log" # open --stderr appends; a stale report must not count.
    # NSLog writes Console output to stderr. open -W waits for the app to exit.
    with_timeout open -W -n "$app" --stderr "$log" --env "REACTOR_DEVICE_TEST_FILTER=${REACTOR_DEVICE_TEST_FILTER:-}" || true
    extract_tap < "$log" > "$tap"
    ;;
  ios)
    if [[ -z "$device" ]]; then
      device="$(xcrun simctl list devices booted | sed -nE 's/.*\(([0-9A-F-]{36})\) \(Booted\).*/\1/p' | head -1)"
    fi
    [[ -n "$device" ]] || { echo "No booted iOS simulator; boot one or pass its UDID." >&2; exit 2; }
    arch="$( [[ "$(uname -m)" == "arm64" ]] && echo arm64 || echo x64 )"
    dotnet build "$project" -f net11.0-ios -r "iossimulator-$arch" -c Debug
    app="$(ls -d "$here"/bin/Debug/net11.0-ios/iossimulator-"$arch"/*.app | head -1)"
    xcrun simctl install "$device" "$app"
    SIMCTL_CHILD_REACTOR_DEVICE_TEST_FILTER="${REACTOR_DEVICE_TEST_FILTER:-}" SIMCTL_CHILD_REACTOR_HOST_GESTURES="${REACTOR_HOST_GESTURES:-}" \
      with_timeout xcrun simctl launch --console-pty --terminate-running-process "$device" "$app_id" > "$out/ios.log" 2>&1 || true
    extract_tap < "$out/ios.log" > "$tap"
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
    # Debug APKs fast-deploy their assemblies; embed them so a plain install runs.
    dotnet build "$project" -f net11.0-android -r "$rid" -c Debug -p:EmbedAssembliesIntoApk=true
    apk="$here/bin/Debug/net11.0-android/$rid/$app_id-Signed.apk"
    "${adb_cmd[@]}" install -r "$apk"
    activity="$("${adb_cmd[@]}" shell cmd package resolve-activity --brief "$app_id" | tail -1 | tr -d '\r')"
    # A process left from an earlier run would answer pidof below.
    "${adb_cmd[@]}" shell am force-stop "$app_id"
    "${adb_cmd[@]}" logcat -c
    # adb shell re-parses the command on the device, so the filter goes in single quotes (it may
    # contain | or [ ]); fixture names never contain one, so a filter with ' is rejected.
    if [[ "${REACTOR_DEVICE_TEST_FILTER:-}" == *"'"* ]]; then echo "REACTOR_DEVICE_TEST_FILTER must not contain '" >&2; exit 2; fi
    "${adb_cmd[@]}" shell am start -n "$activity" ${REACTOR_DEVICE_TEST_FILTER:+-e REACTOR_DEVICE_TEST_FILTER "'$REACTOR_DEVICE_TEST_FILTER'"} > /dev/null
    launched=$SECONDS
    deadline=$((SECONDS + timeout_seconds))
    # Requests are taken only from the app's own process: logcat is scoped to its PID, and each request
    # line must match its exact shape (integers only) before any of it reaches the shell.
    app_pid=""
    for _ in $(seq 1 100); do
      app_pid="$("${adb_cmd[@]}" shell pidof -s "$app_id" 2>/dev/null | tr -d '\r' || true)"
      [[ "$app_pid" =~ ^[0-9]+$ ]] && break
      app_pid=""
      sleep 0.1
    done
    if [[ -z "$app_pid" ]]; then echo "The app's process didn't start." >&2; exit 1; fi
    request='^I/DOTNET[[:space:]]*\([[:space:]]*[0-9]+\):[[:space:]]'
    gesture_re="${request}REACTOR-GESTURE ([0-9]{1,6}) (-?[0-9]{1,5}) (-?[0-9]{1,5}) (-?[0-9]{1,5}) ([0-9]{1,5}) ([0-9]{1,5})\$"
    key_back_re="${request}REACTOR-KEY-BACK [0-9]{1,6}\$"
    to_front_re="${request}REACTOR-BRING-TO-FRONT\$"
    # A decimal integer the regex already matched (an optional minus and digits), without octal surprises.
    int_of() { local v="$1" sign=1; [[ "$v" == -* ]] && { sign=-1; v="${v#-}"; }; echo $((sign * 10#$v)); }
    # Fixtures that need a real back key (an app can't inject one into itself) log REACTOR-KEY-BACK;
    # each one gets one key event to the focused window. The runner logs REACTOR-BRING-TO-FRONT when a
    # fixture left the app in the background; the shell can bring it back, which the app itself can't.
    # The log is followed as a stream, so a request is answered at once: polling full dumps fell
    # seconds behind on a loaded machine, and a key that came after its fixture gave up landed on the
    # next one.
    fifo="$out/android-logcat.fifo"
    rm -f "$fifo"
    mkfifo "$fifo"
    "${adb_cmd[@]}" logcat --pid="$app_pid" -v brief -s DOTNET:I > "$fifo" &
    logcat_pid=$!
    trap 'kill "$logcat_pid" 2>/dev/null || true; rm -f "$fifo"' EXIT
    exec 3< "$fifo"
    while :; do
      if (( SECONDS > deadline )); then echo "Timed out waiting for the device tests." >&2; break; fi
      if ! IFS= read -r -t 1 line <&3; then
        kill -0 "$logcat_pid" 2>/dev/null || { echo "adb logcat stopped." >&2; break; }
        continue
      fi
      line="${line%$'\r'}"
      if [[ "$line" == *"$done_marker"* ]]; then
        break
      elif [[ "$line" =~ $key_back_re ]]; then
        "${adb_cmd[@]}" shell input keyevent 4
      elif [[ "$line" =~ $to_front_re ]]; then
        "${adb_cmd[@]}" shell am start -n "$activity" > /dev/null
      elif [[ "$line" =~ $gesture_re ]]; then
        # A real touch drag from the shell: REACTOR-GESTURE <id> <x> <y> <dx> <moveMs> <holdMs> (screen pixels).
        gx=$(int_of "${BASH_REMATCH[2]}"); gy=$(int_of "${BASH_REMATCH[3]}"); gdx=$(int_of "${BASH_REMATCH[4]}")
        ghold=$(int_of "${BASH_REMATCH[6]}")
        steps=4; script="input motionevent DOWN $gx $gy"
        for ((i = 1; i <= steps; i++)); do script+="; input motionevent MOVE $((gx + gdx * i / steps)) $gy"; done
        (( ghold > 0 )) && script+="; sleep $((ghold / 1000)).$(printf '%03d' $((ghold % 1000)))"
        script+="; input motionevent UP $((gx + gdx)) $gy"
        "${adb_cmd[@]}" shell "$script" > /dev/null 2>&1 &
      fi
    done
    exec 3<&-
    kill "$logcat_pid" 2>/dev/null || true
    wait "$logcat_pid" 2>/dev/null || true
    rm -f "$fifo"
    "${adb_cmd[@]}" logcat -d --pid="$app_pid" -s DOTNET:I > "$out/android.log"
    extract_tap < "$out/android.log" > "$tap"
    ;;
  *)
    echo "Unknown platform '$platform'." >&2
    exit 2
    ;;
esac

cat "$tap"
# Exactly one run's report, and it passed.
if ! grep -q "^$done_marker" "$tap" && (( SECONDS - launched >= timeout_seconds )); then
  echo "Device tests on $platform were stopped after the ${timeout_seconds} s limit (REACTOR_DEVICE_TEST_TIMEOUT), before they finished." >&2
fi
[[ "$(grep -c "^$done_marker" "$tap")" == 1 ]] && grep -q "^$done_marker exit=0" "$tap" || { echo "Device tests failed or did not finish on $platform." >&2; exit 1; }
