#!/bin/bash
# NotchZero: build and launch. Double-click this file in Finder.
cd "$(dirname "$0")" || exit 1
ROOT="$PWD"
LOG="$ROOT/build.log"

echo "== NotchZero build: $(date) ==" > "$LOG"
echo "Building NotchZero..."

pkill -x NotchZero 2>/dev/null

xcodebuild -project NotchZero.xcodeproj -target NotchZero -configuration Release \
  SYMROOT="$ROOT/build" OBJROOT="$ROOT/build/obj" build >> "$LOG" 2>&1
STATUS=$?

grep -E "(error|warning):" "$LOG" | sort -u > "$ROOT/build-issues.log"

if [ $STATUS -eq 0 ]; then
  rm -rf "$ROOT/NotchZero.app"
  cp -R "$ROOT/build/Release/NotchZero.app" "$ROOT/NotchZero.app"

  # Sign with a stable developer certificate if this Mac has one, so macOS remembers
  # permissions (Location, Spotify/Music) across rebuilds instead of asking again.
  IDENTITY=$(security find-identity -v -p codesigning 2>/dev/null \
    | grep -E '"(Apple Development|Mac Developer|Developer ID Application)' \
    | head -1 | sed -E 's/.*"(.*)".*/\1/')
  if [ -n "$IDENTITY" ]; then
    if codesign --force --deep --sign "$IDENTITY" "$ROOT/NotchZero.app" >> "$LOG" 2>&1; then
      echo "SIGNED: $IDENTITY" | tee -a "$LOG"
    else
      echo "SIGN FAILED (kept ad-hoc signature)" | tee -a "$LOG"
    fi
  else
    echo "NO DEVELOPER CERTIFICATE (ad-hoc signature: macOS may ask for permissions again after each rebuild)" | tee -a "$LOG"
  fi

  echo "BUILD OK" | tee -a "$LOG"
  open "$ROOT/NotchZero.app"
  echo "NotchZero is running. Hover over the notch. You can close this window."
else
  echo "BUILD FAILED ($STATUS)" | tee -a "$LOG"
  grep -E "error:" "$LOG" | sort -u | head -20
fi
