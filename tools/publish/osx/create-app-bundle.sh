#!/bin/bash
# Creates the macOS .app bundle for MewUI Gallery.
# Usage: ./create-app-bundle.sh [--publish]
#   --publish  Also runs dotnet publish before assembling the bundle.
#
# The publish profile already outputs to .artifacts/apps/MewUI.Gallery.app/Contents/MacOS/,
# so this script only needs to copy Info.plist, generate the icns, and optionally sign.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

APP_BUNDLE="$REPO_ROOT/.artifacts/apps/MewUI.Gallery.app"
CONTENTS="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"

GALLERY_PROJ="$REPO_ROOT/samples/MewUI.Gallery/MewUI.Gallery.csproj"
PROFILE="osx-arm64-trimmed"

# ── Parse args ──────────────────────────────────────────────
DO_PUBLISH=false
for arg in "$@"; do
    case "$arg" in
        --publish) DO_PUBLISH=true ;;
        *) echo "Unknown argument: $arg"; exit 1 ;;
    esac
done

# ── Publish ─────────────────────────────────────────────────
if [ "$DO_PUBLISH" = true ]; then
    echo "▸ Publishing MewUI.Gallery (osx-arm64)..."
    dotnet publish "$GALLERY_PROJ" -p:PublishProfile="$PROFILE"
fi

# ── Verify publish output exists ────────────────────────────
if [ ! -d "$MACOS_DIR" ]; then
    echo "Error: $MACOS_DIR does not exist. Run with --publish or publish manually first."
    exit 1
fi

# ── Info.plist ──────────────────────────────────────────────
echo "▸ Copying Info.plist..."
cp "$SCRIPT_DIR/Info.plist" "$CONTENTS/Info.plist"

# ── Icon (ico → iconset → icns) ────────────────────────────
echo "▸ Generating appicon.icns..."
mkdir -p "$RESOURCES_DIR"

ICO_SOURCE="$REPO_ROOT/assets/icon/appicon.ico"
ICONSET_DIR="$RESOURCES_DIR/appicon.iconset"
ICNS_OUTPUT="$RESOURCES_DIR/appicon.icns"

if [ -f "$ICO_SOURCE" ] && command -v sips &>/dev/null && command -v iconutil &>/dev/null; then
    # Extract largest PNG from ico via sips, then build iconset.
    TMP_PNG="$(mktemp /tmp/appicon_XXXX.png)"
    sips -s format png "$ICO_SOURCE" --out "$TMP_PNG" >/dev/null 2>&1 || true

    if [ -f "$TMP_PNG" ] && [ -s "$TMP_PNG" ]; then
        rm -rf "$ICONSET_DIR"
        mkdir -p "$ICONSET_DIR"

        # Generate required sizes.
        for size in 16 32 128 256 512; do
            sips -z $size $size "$TMP_PNG" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null 2>&1
            double=$((size * 2))
            sips -z $double $double "$TMP_PNG" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null 2>&1
        done

        iconutil -c icns "$ICONSET_DIR" -o "$ICNS_OUTPUT"
        rm -rf "$ICONSET_DIR" "$TMP_PNG"
        echo "  → appicon.icns created."
    else
        echo "  ⚠ Could not convert ico to png. Skipping icon."
        rm -f "$TMP_PNG"
    fi
else
    echo "  ⚠ sips/iconutil not available or ico not found. Skipping icon."
fi

# ── Ad-hoc code sign ───────────────────────────────────────
if command -v codesign &>/dev/null; then
    echo "▸ Ad-hoc signing..."
    codesign --force --deep --sign "-" "$APP_BUNDLE"
fi

echo "✓ App bundle ready: $APP_BUNDLE"
