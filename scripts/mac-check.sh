#!/usr/bin/env bash
# macOS verification for tasks.md 9.5. Run on a Mac, from the repository:
#   scripts/mac-check.sh [output-folder]
# It runs the test suite, builds Scanwise.app, runs the app's self-check (menu bar, Dock menu, pasteboard,
# edited dot, Dock badge, Liquid Glass panel) with and without Liquid Glass, and screenshots the window each time.
# Then work through docs/MAC-VERIFICATION.md for what has to be looked at.
# Needs: .NET 8 SDK, Xcode command line tools. The screenshots need Screen Recording permission for the terminal
# (System Settings > Privacy & Security). The self-check replaces the clipboard's contents.
set -euo pipefail
if [ "$(uname)" != Darwin ]; then
  echo "Run this on a Mac." >&2
  exit 1
fi
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
out="${1:-mac-check-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$out"
case "$(uname -m)" in arm64) rid=osx-arm64 ;; *) rid=osx-x64 ;; esac

{
  sw_vers
  echo "appearance: $(defaults read -g AppleInterfaceStyle 2>/dev/null || echo Light)"
  echo "increase contrast: $(defaults read com.apple.universalaccess increaseContrast 2>/dev/null || echo 0)"
  echo "reduce transparency: $(defaults read com.apple.universalaccess reduceTransparency 2>/dev/null || echo 0)"
} > "$out/system.txt"
cat "$out/system.txt"

echo "== tests"
if scripts/test.sh > "$out/tests.txt" 2>&1; then echo "tests passed"; else echo "TESTS FAILED (see $out/tests.txt)"; fi

echo "== Scanwise.app ($rid)"
scripts/publish.sh "$rid" > "$out/publish.txt" 2>&1
app="publish/$rid/Scanwise.app"
{
  plutil -lint "$app/Contents/Info.plist"
  plutil -p "$app/Contents/Info.plist"
  codesign -dv "$app" 2>&1 || echo "not signed (expected until a Developer ID is set up)"
} > "$out/bundle.txt" 2>&1
grep -q "OK" "$out/bundle.txt" && echo "Info.plist OK"

failures=0
for glass in 1 0; do
  report="$out/self-check-glass$glass.txt"
  rm -f "$report"
  echo "== self-check, Liquid Glass $([ $glass = 1 ] && echo on || echo off)"
  SCANWISE_LIQUID_GLASS=$glass "$app/Contents/MacOS/Scanwise" --self-check="$report" --keep-open=4 &
  pid=$!
  for _ in $(seq 60); do [ -s "$report" ] && break; sleep 0.5; done
  if [ -s "$report" ]; then
    number="$(sed -n 's/^INFO  window-number: //p' "$report")"
    [ -n "$number" ] && screencapture -o -x -l "$number" "$out/window-glass$glass.png" || true
  fi
  status=0
  wait "$pid" || status=$?
  [ -s "$report" ] && grep -E "^(FAIL|WARN)" "$report" || true
  echo "exit code $status"
  failures=$((failures + status))
done

echo
echo "Results in $out. Self-check failures: $failures."
echo "Next: docs/MAC-VERIFICATION.md (light/dark, VoiceOver, menus, alerts, share menu, Liquid Glass)."
exit $((failures > 0))
