#!/usr/bin/env bash
# Shared publish logic for publish-linux.sh and publish-macos.sh.
# Publishes Service, CLI, UI and DbMigrator and creates a launcher in the artifact root that starts the UI
# (the UI starts the service in the configured role and stops it on exit).
# Usage: publish-common.sh <runtime> [configuration] [output]
set -euo pipefail

runtime="${1:?runtime identifier required}"
configuration="${2:-Release}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/.." && pwd)"
output="${3:-$root/artifacts/publish}"

names=(service cli ui dbmigrator)
projects=(
  "Sources/Apps/RCynQ.Service/RCynQ.Service.csproj"
  "Sources/Apps/RCynQ.Cli/RCynQ.Cli.csproj"
  "Sources/Apps/RCynQ.UI/RCynQ.UI.csproj"
  "Sources/Database/RCynQ.Dbs.SQLite.DbMigrator/RCynQ.Dbs.SQLite.DbMigrator.csproj"
)

for i in "${!names[@]}"; do
  target="$output/$runtime/${names[$i]}"
  echo "==> ${names[$i]} ($runtime, $configuration) -> $target"
  dotnet publish "$root/${projects[$i]}" -c "$configuration" -r "$runtime" --self-contained true -o "$target" -nologo
done

runtime_root="$(cd "$output/$runtime" && pwd)"
chmod +x "$runtime_root/ui/RCynQ.UI" "$runtime_root/service/RCynQ.Service" "$runtime_root/cli/rcynq" \
  "$runtime_root/dbmigrator/RCynQ.Dbs.SQLite.DbMigrator" 2>/dev/null || true
cp "$root/Sources/Apps/RCynQ.UI/Assets/rcynq-512.png" "$runtime_root/rcynq.png"

# Portable installation: configuration goes to config/ and all data to data/ inside this directory.
if [ ! -f "$runtime_root/rcynq.portable" ]; then
  echo "RCynQ portable installation: configuration is stored in config/, all data in data/. Delete this file to use the user profile instead." > "$runtime_root/rcynq.portable"
fi

case "$runtime" in
  linux-*)
    # Desktop entries need absolute paths: run the script again after moving the artifact directory.
    cat > "$runtime_root/RCynQ.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=RCynQ
Comment=RCynQ node: application and services
Exec="$runtime_root/ui/RCynQ.UI"
Path=$runtime_root/ui
Icon=$runtime_root/rcynq.png
Terminal=false
Categories=Development;Network;
EOF
    chmod +x "$runtime_root/RCynQ.desktop"
    echo "Launcher: $runtime_root/RCynQ.desktop"
    ;;
  osx-*)
    cat > "$runtime_root/RCynQ.command" <<'EOF'
#!/bin/bash
cd "$(dirname "$0")/ui" && exec ./RCynQ.UI
EOF
    chmod +x "$runtime_root/RCynQ.command"
    echo "Launcher: $runtime_root/RCynQ.command"
    ;;
esac

# Launcher that works from any location.
cat > "$runtime_root/RCynQ.sh" <<'EOF'
#!/bin/bash
cd "$(dirname "$0")/ui" && exec ./RCynQ.UI "$@"
EOF
chmod +x "$runtime_root/RCynQ.sh"

# Clean package for other nodes: without config/ and data/ (settings, keys and database of this node) and without the
# absolute-path desktop entry.
package="$(cd "$output" && pwd)/RCynQ-$runtime.tar.gz"
tar -czf "$package" -C "$(dirname "$runtime_root")" \
  --exclude="$runtime/config" --exclude="$runtime/data" --exclude="$runtime/RCynQ.desktop" "$runtime"
echo "Package for other nodes (no settings, keys or data): $package"

echo "Done. Artifacts: $runtime_root"
