#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
PROJECT_SRC="$ROOT/Originals/Effects"
PROJECT_TMP="${TMPDIR:-/tmp}/zombieland-unity-inspect"
LOG_DIR="$ROOT/logs"
LOG_FILE="$LOG_DIR/unity-inspect-assets.log"
RESOURCES_DIR="${ZOMBIELAND_RESOURCES_DIR:-$ROOT/Resources}"

if [[ ! -x "$UNITY" ]]; then
  echo "missing Unity executable: $UNITY" >&2
  exit 2
fi

mkdir -p "$LOG_DIR"
rm -rf "$PROJECT_TMP"
mkdir -p "$PROJECT_TMP"
for dir in Assets ProjectSettings Packages; do
  if [[ -d "$PROJECT_SRC/$dir" ]]; then
    ditto "$PROJECT_SRC/$dir" "$PROJECT_TMP/$dir"
  fi
done

ZOMBIELAND_RESOURCES_DIR="$RESOURCES_DIR" "$UNITY" \
  -batchmode \
  -quit \
  -projectPath "$PROJECT_TMP" \
  -executeMethod CreateAssetBundles.InspectDeployedAssetBundles \
  -logFile "$LOG_FILE"

echo "Inspected deployed Unity asset bundles with $UNITY"
echo "Unity log: $LOG_FILE"
