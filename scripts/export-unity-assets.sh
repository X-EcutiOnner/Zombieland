#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
PROJECT_SRC="$ROOT/Originals/Effects"
PROJECT_TMP="${TMPDIR:-/tmp}/zombieland-unity-export"
EXPORT_RESOURCES_TMP="${TMPDIR:-/tmp}/zombieland-unity-export-staging-resources"
LOG_DIR="$ROOT/logs"
LOG_FILE="$LOG_DIR/unity-export.log"
RESOURCES_DIR="${ZOMBIELAND_RESOURCES_DIR:-$ROOT/Resources}"

if [[ ! -x "$UNITY" ]]; then
  echo "missing Unity executable: $UNITY" >&2
  exit 2
fi

mkdir -p "$LOG_DIR"
rm -rf "$PROJECT_TMP"
rm -rf "$EXPORT_RESOURCES_TMP"
ditto "$PROJECT_SRC" "$PROJECT_TMP"
mkdir -p "$EXPORT_RESOURCES_TMP"

ZOMBIELAND_RESOURCES_DIR="$EXPORT_RESOURCES_TMP" "$UNITY" \
  -batchmode \
  -quit \
  -projectPath "$PROJECT_TMP" \
  -executeMethod CreateAssetBundles.BuildStandaloneAssetBundles \
  -logFile "$LOG_FILE"

for bundle in "$EXPORT_RESOURCES_TMP"/{MacOS,Win64,Linux}/zombieland; do
  if [[ ! -s "$bundle" ]]; then
    echo "missing exported bundle: $bundle" >&2
    exit 1
  fi
done

for arch in MacOS Win64 Linux; do
  mkdir -p "$RESOURCES_DIR/$arch"
  cp "$EXPORT_RESOURCES_TMP/$arch/zombieland" "$RESOURCES_DIR/$arch/zombieland"
done

echo "Exported Unity asset bundles with $UNITY"
echo "Unity log: $LOG_FILE"
