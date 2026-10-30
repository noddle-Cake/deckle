#!/usr/bin/env bash
# Runs the test suite with SANE's virtual "test" scanner enabled (no root needed).
# Optional: SCANWISE_TEST_OSD_DIR=/path/with/osd.traineddata to cover orientation detection.
set -euo pipefail
# Prefer a per-user .NET 10 SDK (dotnet-install.sh) over an older system one.
if [ -x "$HOME/.dotnet/dotnet" ]; then export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; fi
root="$(cd "$(dirname "$0")/.." && pwd)"
sane_dir="$(mktemp -d)"
trap 'rm -rf "$sane_dir"' EXIT
echo test > "$sane_dir/dll.conf"
# A colored pattern instead of the default solid black page (better screenshots, meaningful deskew).
printf 'test-picture "Color pattern"\n' > "$sane_dir/test.conf"
SANE_CONFIG_DIR="$sane_dir" dotnet test "$root/Scanwise.sln" "$@"
