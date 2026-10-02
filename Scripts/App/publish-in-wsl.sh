#!/usr/bin/env bash
# Builds the Linux or macOS package of RCynQ inside WSL from a repository that lives on the Windows side.
# Called by Scripts/App/publish-linux.ps1 and publish-macos.ps1; can also be run by hand:
#   wsl bash Scripts/App/publish-in-wsl.sh [runtime=linux-x64]
#
# Why not simply run publish-linux.sh on /mnt/c: Windows folders keep no executable bits, so the archive would hold
# files Linux and macOS refuse to start. The sources are therefore copied into the WSL file system, built there, and
# only the finished packages come back to artifacts/publish.
# The .NET SDK is installed into ~/.dotnet with the official script when it is missing (no sudo, nothing system-wide).
set -euo pipefail

runtime="${1:-linux-x64}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_root="$(cd "$script_dir/../.." && pwd)"
work="$HOME/rcynq-build"
dotnet_dir="$HOME/.dotnet"

case "$runtime" in
  linux-x64|linux-arm64|osx-x64|osx-arm64) ;;
  *) echo "Unsupported runtime '$runtime'." >&2; exit 2 ;;
esac

for tool in git tar curl; do
  command -v "$tool" >/dev/null || { echo "'$tool' is missing in WSL: sudo apt install $tool" >&2; exit 2; }
done

# The SDK major version of global.json.
sdk_major="$(grep -o '"version"[^"]*"[0-9]*' "$source_root/global.json" | grep -o '[0-9]*$')"
if ! "$dotnet_dir/dotnet" --list-sdks 2>/dev/null | grep -q "^$sdk_major\."; then
  echo "==> .NET SDK $sdk_major -> $dotnet_dir"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel "$sdk_major.0" --install-dir "$dotnet_dir" --no-path
fi
export DOTNET_ROOT="$dotnet_dir"
export PATH="$dotnet_dir:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Everything git knows about or would add (tracked and new files, nothing ignored), exactly as it is on disk now.
echo "==> sources: $source_root -> $work/src"
rm -rf "$work/src"
mkdir -p "$work/src"
(cd "$source_root" && git -c safe.directory='*' -c core.quotepath=off ls-files -z --cached --others --exclude-standard \
  | tar --null --ignore-failed-read -T - -cf -) | tar -xf - -C "$work/src"

# The scripts must be LF and executable whatever the Windows checkout did to them.
find "$work/src/Scripts" -name '*.sh' -exec sed -i 's/\r$//' {} + -exec chmod +x {} +

# The output of an earlier run is removed first: it lives in the WSL home between runs, and a package of an older
# version left there would be copied back next to the new one.
rm -rf "$work/out"
bash "$work/src/Scripts/App/publish-common.sh" "$runtime" Release "$work/out"

shopt -s nullglob
built=("$work/out"/RCynQ-*-"$runtime".tar.gz "$work/out"/RCynQ-*-"$runtime".deb)
if [ "${#built[@]}" -eq 0 ]; then
  echo "No package was built in $work/out." >&2
  exit 1
fi

mkdir -p "$source_root/artifacts/publish"
cp -f "${built[@]}" "$source_root/artifacts/publish/"
echo
echo "Ready:"
for package in "${built[@]}"; do
  echo "$source_root/artifacts/publish/$(basename "$package")"
done
