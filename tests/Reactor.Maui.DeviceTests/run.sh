#!/usr/bin/env bash
# Runs the Reactor MAUI device tests on one platform and exits non-zero on any failure.
#
#   tests/Reactor.Maui.DeviceTests/run.sh maccatalyst
#   tests/Reactor.Maui.DeviceTests/run.sh ios [simulator-udid]      (default: the booted simulator)
#   tests/Reactor.Maui.DeviceTests/run.sh android [adb-serial]      (default: the only device)
#
# REACTOR_DEVICE_TEST_FILTER=<substring> runs only matching fixtures (Mac Catalyst and iOS).
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
timeout_seconds="${REACTOR_DEVICE_TEST_TIMEOUT:-600}"
done_marker="# REACTOR-DEVICE-TESTS-DONE"
mkdir -p "$out"

# Runs a command with a time limit (macOS has no timeout(1)).
with_timeout() { perl -e 'alarm shift; exec @ARGV' "$timeout_seconds" "$@"; }

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
    with_timeout open -W -n "$app" --stderr "$log" --env "REACTOR_DEVICE_TEST_FILTER=${REACTOR_DEVICE_TEST_FILTER:-}"
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
    SIMCTL_CHILD_REACTOR_DEVICE_TEST_FILTER="${REACTOR_DEVICE_TEST_FILTER:-}" \
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
    "${adb_cmd[@]}" logcat -c
    "${adb_cmd[@]}" shell am start -n "$activity" > /dev/null
    deadline=$((SECONDS + timeout_seconds))
    until "${adb_cmd[@]}" logcat -d -s DOTNET:I | grep -q "$done_marker"; do
      if (( SECONDS > deadline )); then echo "Timed out waiting for the device tests." >&2; break; fi
      sleep 2
    done
    "${adb_cmd[@]}" logcat -d -s DOTNET:I > "$out/android.log"
    extract_tap < "$out/android.log" > "$tap"
    ;;
  *)
    echo "Unknown platform '$platform'." >&2
    exit 2
    ;;
esac

cat "$tap"
# Exactly one run's report, and it passed.
[[ "$(grep -c "^$done_marker" "$tap")" == 1 ]] && grep -q "^$done_marker exit=0" "$tap" || { echo "Device tests failed or did not finish on $platform." >&2; exit 1; }
