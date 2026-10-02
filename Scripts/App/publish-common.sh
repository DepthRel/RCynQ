#!/usr/bin/env bash
# Shared publish logic for publish-linux.sh and publish-macos.sh.
# Publishes Service, CLI, UI and DbMigrator as self-contained Release builds into <output>/<runtime>, adds the launchers
# and the notes, and packs a clean copy (no config/ or data/) into RCynQ-<version>-<runtime>.tar.gz. On Linux, where
# dpkg-deb is available, build-deb.sh makes RCynQ-<version>-<runtime>.deb from the same folder.
# Usage: publish-common.sh <runtime> [configuration=Release] [output=artifacts/publish]
set -euo pipefail

runtime="${1:?runtime identifier required}"
configuration="${2:-Release}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$script_dir/../.." && pwd)"
output="${3:-$root/artifacts/publish}"

case "$runtime" in
  linux-x64|linux-arm64) platform=linux ;;
  osx-x64|osx-arm64) platform=macos ;;
  *) echo "Unsupported runtime '$runtime'. Use linux-x64, linux-arm64, osx-x64 or osx-arm64." >&2; exit 2 ;;
esac

ui_project="$root/Sources/Apps/RCynQ.UI/RCynQ.UI.csproj"
version="$(dotnet msbuild "$ui_project" -nologo -getProperty:Version | tr -d '[:space:]')"
if [ -z "$version" ]; then
  echo "Cannot read the version from the project." >&2
  exit 1
fi

names=(service cli ui dbmigrator)
projects=(
  "Sources/Apps/RCynQ.Service/RCynQ.Service.csproj"
  "Sources/Apps/RCynQ.Cli/RCynQ.Cli.csproj"
  "Sources/Apps/RCynQ.UI/RCynQ.UI.csproj"
  "Sources/Database/RCynQ.Dbs.SQLite.DbMigrator/RCynQ.Dbs.SQLite.DbMigrator.csproj"
)

mkdir -p "$output"
output="$(cd "$output" && pwd)"
stage="$output/$runtime"

# The programs are rebuilt; config/ and data/ of a node tried out from this folder stay.
for name in "${names[@]}"; do
  rm -rf "${stage:?}/$name"
done

for i in "${!names[@]}"; do
  target="$stage/${names[$i]}"
  echo "==> ${names[$i]} ($runtime, $configuration) -> $target"
  dotnet publish "$root/${projects[$i]}" -c "$configuration" -r "$runtime" --self-contained true -o "$target" -nologo -p:DebugType=none
done

chmod +x "$stage/ui/RCynQ.UI" "$stage/service/RCynQ.Service" "$stage/cli/rcynq" \
  "$stage/dbmigrator/RCynQ.Dbs.SQLite.DbMigrator"
cp "$root/Sources/Apps/RCynQ.UI/Assets/rcynq-512.png" "$stage/rcynq.png"
chmod 644 "$stage/rcynq.png"

# Portable installation: configuration goes to config/ and all data to data/ inside this directory.
if [ ! -f "$stage/rcynq.portable" ]; then
  echo "RCynQ portable installation: configuration is stored in config/, all data in data/. Delete this file to use the user profile instead." > "$stage/rcynq.portable"
fi

# Files of earlier versions of this script: a desktop entry with the absolute paths of the build machine.
rm -f "$stage/RCynQ.desktop"

# The launcher that works from any location.
cat > "$stage/RCynQ.sh" <<'LAUNCHER'
#!/bin/bash
# Starts RCynQ from any location: the UI starts the service in the configured role and stops it on exit.
cd "$(dirname "$0")/ui" && exec ./RCynQ.UI "$@"
LAUNCHER
chmod +x "$stage/RCynQ.sh"

case "$platform" in
  linux)
    cat > "$stage/install-desktop-entry.sh" <<'DESKTOP'
#!/bin/bash
# Registers RCynQ in the application menu of the current user (paths are absolute: run again after moving the folder).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$HOME/.local/share/applications"
cat > "$HOME/.local/share/applications/rcynq.desktop" <<ENTRY
[Desktop Entry]
Type=Application
Name=RCynQ
Comment=Deliver uncommitted code to a remote machine, directly or through a relay
Comment[de]=Nicht committeten Code auf einen entfernten Rechner bringen, direkt oder über ein Relay
Comment[es]=Lleva código sin commit a una máquina remota, directamente o a través de un Relay
Comment[fr]=Envoyer du code non commité sur une machine distante, directement ou par un Relay
Comment[pt]=Levar código sem commit para uma máquina remota, diretamente ou através de um Relay
Comment[ru]=Доставка незакоммиченного кода на удалённую машину, напрямую или через Relay
Exec="$here/ui/RCynQ.UI"
Path=$here/ui
Icon=$here/rcynq.png
Terminal=false
Categories=Development;RevisionControl;Network;
ENTRY
if command -v update-desktop-database >/dev/null; then
  update-desktop-database "$HOME/.local/share/applications" || true
fi
echo "RCynQ added to the application menu."
DESKTOP
    chmod +x "$stage/install-desktop-entry.sh"
    ;;
  macos)
    cat > "$stage/RCynQ.command" <<'LAUNCHER'
#!/bin/bash
# Starts RCynQ with a double click in Finder.
cd "$(dirname "$0")/ui" && exec ./RCynQ.UI
LAUNCHER
    chmod +x "$stage/RCynQ.command"
    if command -v codesign >/dev/null; then
      # Ad-hoc signature: required for arm64 binaries; not a Developer ID signature.
      find "$stage" -type f \( -perm -u+x -o -name '*.dylib' \) -exec codesign --force --sign - {} \; 2>/dev/null ||
        echo "warning: ad-hoc signing failed" >&2
    fi
    ;;
esac

# The note of the package in every language, from the templates in Notes.
# shellcheck source=../Shared/package-notes.sh
source "$script_dir/../Shared/package-notes.sh"
write_package_notes "$script_dir/Notes" "$stage" "$platform" "$version" "$runtime"
write_package_licenses "$stage"

# Clean package for other nodes: without config/ and data/ (settings, keys and database of this node), under RCynQ/.
package="$output/RCynQ-$version-$runtime.tar.gz"
if tar --version 2>/dev/null | grep -q 'GNU tar'; then
  rename=(--transform "s,^$runtime,RCynQ,")
else
  rename=(-s ",^$runtime,RCynQ,")
fi
tar -czf "$package" -C "$output" "${rename[@]}" \
  --exclude="$runtime/config" --exclude="$runtime/data" "$runtime"
echo "Package for other nodes (no settings, keys or data): $package"

# The installer of Debian, Ubuntu and their relatives, where dpkg-deb is at hand (any of them, WSL included).
if [ "$platform" = linux ]; then
  if command -v dpkg-deb >/dev/null; then
    bash "$script_dir/build-deb.sh" "$runtime" "$stage" "$version" "$output"
  else
    echo "dpkg-deb is missing: no .deb package (it comes with Debian and Ubuntu, WSL included)." >&2
  fi
fi

echo "Done. Artifacts: $stage"
