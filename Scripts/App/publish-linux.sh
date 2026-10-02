#!/usr/bin/env bash
# Publishes RCynQ (Service, CLI, UI, DbMigrator) as self-contained Release builds for Linux: the .tar.gz archive and,
# where dpkg-deb is available, the .deb installer.
# Usage: ./Scripts/App/publish-linux.sh [runtime=linux-x64] [configuration=Release] [output=artifacts/publish]
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/publish-common.sh" "${1:-linux-x64}" "${2:-Release}" ${3:+"$3"}
