#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -n "${RIMWORLD_MOD_DIR:-}" ]]; then
	running_rimworld="$(ps ax -o pid=,comm= | awk '$0 ~ /\/RimWorld by Ludeon Studios$/ { print }' || true)"
	if [[ -n "$running_rimworld" ]]; then
		printf 'Refusing deploy build because RimWorld is still running:\n%s\n' "$running_rimworld" >&2
		printf 'Stop RimWorld through GABS before rebuilding with RIMWORLD_MOD_DIR set.\n' >&2
		exit 2
	fi
fi

dotnet build Source/ZombieLand.csproj -v:q -clp:ErrorsOnly "$@"
