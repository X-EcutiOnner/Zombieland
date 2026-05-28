#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

dotnet build Source/ZombieLand.csproj -v:q -clp:ErrorsOnly "$@"
