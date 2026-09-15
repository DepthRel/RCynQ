#!/usr/bin/env bash
# Publishes RCynQ (Service, CLI, UI, DbMigrator) as self-contained Release builds for Linux.
# Usage: ./Scripts/publish-linux.sh [runtime=linux-x64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/publish-common.sh" "${1:-linux-x64}" "${2:-Release}" ${3:+"$3"}
