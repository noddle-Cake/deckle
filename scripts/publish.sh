#!/usr/bin/env bash
# Builds self-contained Deckle folders (no .NET install needed) for each platform.
# Usage: scripts/publish.sh [rid ...]   default: linux-x64 linux-arm64 win-x64 osx-x64 osx-arm64
# Output: publish/<rid>/  plus publish/deckle-<version>-<rid>.{tar.gz,zip}
#         macOS: publish/osx-*/Deckle.app, zipped (unsigned; see README)
set -euo pipefail
# Prefer a per-user .NET 10 SDK (dotnet-install.sh) over an older system one.
if [ -x "$HOME/.dotnet/dotnet" ]; then export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; fi
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
  dotnet publish src/Deckle.App/Deckle.App.csproj -c Release -r "$rid" --self-contained true \
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
  # Deckle's license and the notices its bundled components require travel with every build.
  cp -r licenses "$out/licenses"
  cp LICENSE "$out/licenses/LICENSE"
  case "$rid" in
    win-*) (cd publish && rm -f "deckle-$version-$rid.zip" && zip -qr "deckle-$version-$rid.zip" "$rid") ;;
    osx-*)
      # A real app bundle (Dock name and icon, bundle ID for permissions such as local network access).
      # Everything the publish produced lives in Contents/MacOS, so the app finds tessdata and natives as before.
      chmod +x "$out/Deckle"; find "$out" -path '*/_*/tesseract' -exec chmod +x {} +
      app="$out/Deckle.app"
      rm -rf "$out.contents"
      mv "$out" "$out.contents"
      mkdir -p "$app/Contents/Resources"
      mv "$out.contents" "$app/Contents/MacOS"
      sed "s/@VERSION@/$version/g" packaging/macos/Info.plist > "$app/Contents/Info.plist"
      printf 'APPL????' > "$app/Contents/PkgInfo"
      cp packaging/macos/Deckle.icns "$app/Contents/Resources/Deckle.icns"
      (cd "$out" && rm -f "../deckle-$version-$rid.zip" && zip -qry "../deckle-$version-$rid.zip" Deckle.app) ;;
    *) chmod +x "$out/Deckle"; find "$out" -path '*/_*/tesseract' -exec chmod +x {} +
       tar -C publish -czf "publish/deckle-$version-$rid.tar.gz" "$rid" ;;
  esac
done
ls -lh publish/*.tar.gz publish/*.zip 2>/dev/null || true
