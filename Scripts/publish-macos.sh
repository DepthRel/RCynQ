#!/usr/bin/env bash
# Publishes RCynQ (Service, CLI, UI, DbMigrator) as self-contained Release builds for macOS.
# Usage: ./Scripts/publish-macos.sh [runtime=osx-arm64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/publish-common.sh" "${1:-osx-arm64}" "${2:-Release}" ${3:+"$3"}
