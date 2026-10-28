#!/usr/bin/env bash
# Builds self-contained Scanwise folders (no .NET install needed) for each platform.
# Usage: scripts/publish.sh [rid ...]   default: linux-x64 linux-arm64 win-x64 osx-x64 osx-arm64
# Output: publish/<rid>/  plus publish/scanwise-<version>-<rid>.{tar.gz,zip}
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
rids=("$@")
if [ ${#rids[@]} -eq 0 ]; then
  rids=(linux-x64 linux-arm64 win-x64 osx-x64 osx-arm64)
fi
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
mkdir -p publish
for rid in "${rids[@]}"; do
  out="publish/$rid"
  rm -rf "$out"
  echo "==> $rid"
  dotnet publish src/Scanwise.App/Scanwise.App.csproj -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=false -p:DebugType=none -o "$out" --nologo -v q
  # Bundled native helpers for other platforms are not needed in a per-platform build.
  for dir in "$out"/_*; do
    case "$rid:$(basename "$dir")" in
      linux-x64:_linux|linux-arm64:_linuxarm|win-x64:_win64|win-x64:_win32|osx-x64:_mac|osx-arm64:_macarm) ;;
      *) [ -d "$dir" ] && rm -rf "$dir" ;;
    esac
  done
  case "$rid" in
    win-*) (cd publish && rm -f "scanwise-$version-$rid.zip" && zip -qr "scanwise-$version-$rid.zip" "$rid") ;;
    *) chmod +x "$out/Scanwise"; find "$out" -path '*/_*/tesseract' -exec chmod +x {} +
       tar -C publish -czf "publish/scanwise-$version-$rid.tar.gz" "$rid" ;;
  esac
done
ls -lh publish/*.tar.gz publish/*.zip 2>/dev/null || true
