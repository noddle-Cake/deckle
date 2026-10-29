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

# OCR works out of the box: English and orientation data ship in <app>/tessdata (more languages download from
# Settings). Pinned to a tessdata_fast commit and checked, cached in .cache/tessdata.
tessdata_commit=87416418657359cb625c412a48b6e1d6d41c29bd
declare -A tessdata_sha=(
  [eng]=7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2
  [osd]=9cf5d576fcc47564f11265841e5ca839001e7e6f38ff7f7aacf46d15a96b00ff
)
mkdir -p .cache/tessdata
for lang in "${!tessdata_sha[@]}"; do
  file=".cache/tessdata/$lang.traineddata"
  if ! echo "${tessdata_sha[$lang]}  $file" | sha256sum -c --status 2>/dev/null; then
    curl -sSfL -o "$file" "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/$tessdata_commit/$lang.traineddata"
    echo "${tessdata_sha[$lang]}  $file" | sha256sum -c --quiet
  fi
done

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
  mkdir -p "$out/tessdata"
  cp .cache/tessdata/*.traineddata "$out/tessdata/"
  case "$rid" in
    win-*) (cd publish && rm -f "scanwise-$version-$rid.zip" && zip -qr "scanwise-$version-$rid.zip" "$rid") ;;
    *) chmod +x "$out/Scanwise"; find "$out" -path '*/_*/tesseract' -exec chmod +x {} +
       tar -C publish -czf "publish/scanwise-$version-$rid.tar.gz" "$rid" ;;
  esac
done
ls -lh publish/*.tar.gz publish/*.zip 2>/dev/null || true
