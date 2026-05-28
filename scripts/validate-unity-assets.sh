#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity}"
PROJECT_SRC="$ROOT/Originals/Effects"
PROJECT_TMP="${TMPDIR:-/tmp}/zombieland-unity-validate"
LOG_DIR="$ROOT/logs"
LOG_FILE="$LOG_DIR/unity-validate-assets.log"

if [[ ! -x "$UNITY" ]]; then
  echo "missing Unity executable: $UNITY" >&2
  exit 2
fi

mkdir -p "$LOG_DIR"
rm -rf "$PROJECT_TMP"
ditto "$PROJECT_SRC" "$PROJECT_TMP"

ZOMBIELAND_RESOURCES_DIR="$ROOT/Resources" "$UNITY" \
  -batchmode \
  -quit \
  -projectPath "$PROJECT_TMP" \
  -executeMethod CreateAssetBundles.ValidateDeployedAssetBundles \
  -logFile "$LOG_FILE"

echo "Validated deployed Unity asset bundles with $UNITY"
echo "Unity log: $LOG_FILE"
