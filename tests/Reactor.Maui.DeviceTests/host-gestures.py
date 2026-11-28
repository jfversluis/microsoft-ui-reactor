#!/usr/bin/env python3
"""Performs device-test drags as real XCUITest touches on an iOS simulator (spec 070 §5.1).

An app can't inject real touches into itself on iOS, so fixtures that need one log
`REACTOR-GESTURE <id> <x> <y> <dx> <moveMs> <holdMs>` (window points) and wait for UIKit to report
the touch. This script follows the run's log and performs each drag through an Appium XCUITest
session (`mobile: dragFromToWithVelocity`, which presses, drags at a velocity and holds before lifting).

    appium --address 127.0.0.1 --port 4723 &   # with the xcuitest driver installed; loopback only:
                                               # an Appium server drives the simulator for whoever reaches it
    python3 tests/Reactor.Maui.DeviceTests/host-gestures.py <udid> <ios-version> &
    REACTOR_HOST_GESTURES=1 REACTOR_DEVICE_TEST_FILTER=Characterize_Carousel \
      tests/Reactor.Maui.DeviceTests/run.sh ios <udid>

Android needs none of this: run.sh answers REACTOR-GESTURE itself with `adb shell input motionevent`.
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get('APPIUM_URL', 'http://127.0.0.1:4723')
HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, '..', '..', 'artifacts', 'device-tests', 'ios.log')
# The whole request, integers only, at the end of its log line.
PATTERN = re.compile(r'REACTOR-GESTURE (\d{1,6}) (-?\d{1,5}) (-?\d{1,5}) (-?\d{1,5}) (\d{1,5}) (\d{1,5})$', re.MULTILINE)


def call(method, path, body=None, timeout=1200):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(BASE + path, method=method, data=data, headers={'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise RuntimeError(error.read().decode()[:2000]) from error


def session(udid, version):
    # Headless: the simulators run without Simulator.app. The first session builds WebDriverAgent.
    caps = {
        'platformName': 'iOS', 'appium:automationName': 'XCUITest', 'appium:udid': udid,
        'appium:platformVersion': version, 'appium:noReset': True, 'appium:isHeadless': True,
        'appium:wdaLaunchTimeout': 600000, 'appium:wdaConnectionTimeout': 600000, 'appium:newCommandTimeout': 7200,
    }
    sid = call('POST', '/session', {'capabilities': {'alwaysMatch': caps}})['value']['sessionId']
    # XCUITest waits for the app to be idle before each action; an app that's always animating (a
    # fixture waiting in a loop) would delay the first drag past the fixture's patience.
    call('POST', f'/session/{sid}/appium/settings', {'settings': {'waitForIdleTimeout': 0, 'animationCoolOffTimeout': 0}})
    return sid


def follow(sid):
    done, position = set(), 0
    print('host-gestures: following', os.path.normpath(LOG), flush=True)
    while True:
        try:
            size = os.path.getsize(LOG)
        except FileNotFoundError:
            time.sleep(0.2)
            continue
        if size < position:
            # run.sh started a new run: gesture ids start again.
            position = 0
            done.clear()
        with open(LOG, 'r', errors='replace') as log:
            log.seek(position)
            data = log.read()
            position = log.tell()
        for match in PATTERN.finditer(data):
            gid, x, y, dx, move, hold = (int(v) for v in match.groups())
            if gid in done:
                continue
            done.add(gid)
            args = {'pressDuration': 0.05, 'holdDuration': hold / 1000.0, 'velocity': abs(dx) / max(move / 1000.0, 0.01),
                    'fromX': x, 'fromY': y, 'toX': x + dx, 'toY': y}
            started = time.time()
            try:
                call('POST', f'/session/{sid}/execute/sync', {'script': 'mobile: dragFromToWithVelocity', 'args': [args]}, timeout=120)
                print(f'gesture {gid}: {x},{y} dx {dx} over {move} ms, hold {hold} ms ({time.time() - started:.2f} s)', flush=True)
            except Exception as error:  # noqa: BLE001 - a failed gesture is reported, the fixture times out
                print(f'gesture {gid} failed: {error}', flush=True)
        time.sleep(0.05)


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    follow(session(sys.argv[1], sys.argv[2]))
