#!/usr/bin/env bash
# Compile FaceWorkerBridge.m into a device arm64 static library for the MAUI app.
set -euo pipefail

SRC_DIR="$(cd "$(dirname "$0")/.." && pwd)"
NATIVE="$SRC_DIR/Native"
OUT_DIR="${1:?usage: build_face_worker_bridge.sh <output-dir>}"
mkdir -p "$OUT_DIR"

SDKROOT="$(xcrun --sdk iphoneos --show-sdk-path)"
OBJ="$OUT_DIR/FaceWorkerBridge.o"
LIB="$OUT_DIR/libFaceWorkerBridge.a"

xcrun clang -c "$NATIVE/FaceWorkerBridge.m" \
  -o "$OBJ" \
  -fobjc-arc \
  -isysroot "$SDKROOT" \
  -arch arm64 \
  -miphoneos-version-min=15.0 \
  -I "$NATIVE" \
  -F "$SRC_DIR/Frameworks"

# FaceSdkCompat keeps the JSON the C# layer parses while the engine's own API moved on.
COMPAT_OBJ="$OUT_DIR/FaceSdkCompat.o"
xcrun clang -c "$NATIVE/FaceSdkCompat.mm" \
  -o "$COMPAT_OBJ" \
  -fobjc-arc \
  -std=c++17 \
  -isysroot "$SDKROOT" \
  -arch arm64 \
  -miphoneos-version-min=15.0 \
  -I "$NATIVE" \
  -F "$SRC_DIR/Frameworks"

rm -f "$LIB"
xcrun ar rcs "$LIB" "$OBJ" "$COMPAT_OBJ"
echo "$LIB"
