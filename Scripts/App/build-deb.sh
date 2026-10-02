#!/usr/bin/env bash
# Builds the Debian/Ubuntu installer of RCynQ from a published Linux package folder.
# Called by publish-common.sh for linux-* runtimes when dpkg-deb is available; can also be run by hand.
# Usage: build-deb.sh <runtime: linux-x64|linux-arm64> <published folder> <version> <output folder>
#
# Layout of the installed package:
#   /opt/rcynq/{service,cli,ui,dbmigrator}   the published programs, without the rcynq.portable marker: every user
#                                           keeps the configuration in ~/.config/RCynQ and the data in
#                                           ~/.local/share/RCynQ, because /opt is not writable for them
#   /usr/bin/rcynq                           the command line
#   /usr/bin/rcynq-ui                        the application (it starts the service itself)
#   /usr/share/applications/rcynq.desktop    the menu entry
#   /usr/share/icons/hicolor/{scalable,512x512,256x256}/apps/rcynq.{svg,png}   the icon in every size a desktop asks
#   /usr/share/metainfo/io.github.depthrel.RCynQ.metainfo.xml   what software centres show about the application
#   /usr/share/doc/rcynq/copyright           where the license and the notices of the components are
# Git and the OpenSSH client are dependencies: RCynQ calls both.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
assets="$script_dir/../../Sources/Apps/RCynQ.UI/Assets"

runtime="${1:?runtime identifier required}"
published="${2:?published folder required}"
version="${3:?version required}"
output="${4:?output folder required}"

case "$runtime" in
  linux-x64) architecture=amd64 ;;
  linux-arm64) architecture=arm64 ;;
  *) echo "build-deb.sh: no Debian architecture for $runtime" >&2; exit 2 ;;
esac

# The maintainer written into the package; set RCYNQ_DEB_MAINTAINER to "Name <address>" to change it.
maintainer="${RCYNQ_DEB_MAINTAINER:-RCynQ project <project.rcynq@gmail.com>}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/rcynq_${version}_$architecture"

# The programs only: not the data of a tried copy, not the portable marker, not the launchers and notes of the archive.
mkdir -p "$root/opt/rcynq"
for program in service cli ui dbmigrator; do
  cp -R "$published/$program" "$root/opt/rcynq/$program"
done
cp "$published/LICENSE.txt" "$published/THIRD-PARTY-NOTICES.txt" "$root/opt/rcynq/"

mkdir -p "$root/usr/share/doc/rcynq"
cat > "$root/usr/share/doc/rcynq/copyright" <<'COPYRIGHT'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: RCynQ
Source: https://github.com/DepthRel/RCynQ

Files: *
Copyright: RCynQ contributors
License: GPL-3.0-or-later
 The full text is in /opt/rcynq/LICENSE.txt. The components RCynQ contains, their licenses and where to get their
 source are listed in /opt/rcynq/THIRD-PARTY-NOTICES.txt.
COPYRIGHT

mkdir -p "$root/usr/bin"
cat > "$root/usr/bin/rcynq" <<'LAUNCHER'
#!/bin/sh
# The RCynQ command line installed from the Debian package.
exec /opt/rcynq/cli/rcynq "$@"
LAUNCHER
cat > "$root/usr/bin/rcynq-ui" <<'LAUNCHER'
#!/bin/sh
# The RCynQ application installed from the Debian package; it starts the service in the configured role.
cd /opt/rcynq/ui && exec ./RCynQ.UI "$@"
LAUNCHER

mkdir -p "$root/usr/share/applications"
cat > "$root/usr/share/applications/rcynq.desktop" <<'ENTRY'
[Desktop Entry]
Type=Application
Name=RCynQ
GenericName=Remote code synchronization
Comment=Deliver uncommitted code to a remote machine, directly or through a relay
Comment[de]=Nicht committeten Code auf einen entfernten Rechner bringen, direkt oder über ein Relay
Comment[es]=Lleva código sin commit a una máquina remota, directamente o a través de un Relay
Comment[fr]=Envoyer du code non commité sur une machine distante, directement ou par un Relay
Comment[pt]=Levar código sem commit para uma máquina remota, diretamente ou através de um Relay
Comment[ru]=Доставка незакоммиченного кода на удалённую машину, напрямую или через Relay
Exec=rcynq-ui
Icon=rcynq
Terminal=false
Categories=Development;RevisionControl;Network;
Keywords=git;ssh;patch;sync;relay;
ENTRY

icons="$root/usr/share/icons/hicolor"
mkdir -p "$icons/scalable/apps" "$icons/512x512/apps" "$icons/256x256/apps"
cp "$assets/rcynq.svg" "$icons/scalable/apps/rcynq.svg"
cp "$assets/rcynq-512.png" "$icons/512x512/apps/rcynq.png"
cp "$assets/rcynq-256.png" "$icons/256x256/apps/rcynq.png"

mkdir -p "$root/usr/share/metainfo"
cat > "$root/usr/share/metainfo/io.github.depthrel.RCynQ.metainfo.xml" <<METAINFO
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>io.github.depthrel.RCynQ</id>
  <name>RCynQ</name>
  <summary>Deliver uncommitted code to a remote machine, directly or through a relay</summary>
  <summary xml:lang="de">Nicht committeten Code auf einen entfernten Rechner bringen, direkt oder über ein Relay</summary>
  <summary xml:lang="es">Lleva código sin commit a una máquina remota, directamente o a través de un Relay</summary>
  <summary xml:lang="fr">Envoyer du code non commité sur une machine distante, directement ou par un Relay</summary>
  <summary xml:lang="pt">Levar código sem commit para uma máquina remota, diretamente ou através de um Relay</summary>
  <summary xml:lang="ru">Доставка незакоммиченного кода на удалённую машину, напрямую или через Relay</summary>
  <metadata_license>CC0-1.0</metadata_license>
  <project_license>GPL-3.0-or-later</project_license>
  <developer id="io.github.depthrel">
    <name>DepthRel</name>
  </developer>
  <description>
    <p>RCynQ (Remote Code sYNchronization, Quick) links the computer you work on with a computer that holds the Git
    repositories, even when the two cannot reach each other. Uncommitted edits travel as signed patches in either
    direction, and a branch comes back with one Pull, without a commit or a push.</p>
    <p>A Relay in the middle only forwards encrypted SSH traffic and never sees the code.</p>
  </description>
  <launchable type="desktop-id">rcynq.desktop</launchable>
  <icon type="stock">rcynq</icon>
  <url type="homepage">https://depthrel.github.io/RCynQ/</url>
  <url type="vcs-browser">https://github.com/DepthRel/RCynQ</url>
  <categories>
    <category>Development</category>
  </categories>
  <releases>
    <release version="$version" date="$(date -u +%Y-%m-%d)" />
  </releases>
</component>
METAINFO

# Directories and files are readable by all; only programs (ELF executables, not shared libraries) and the launchers
# may run. A build on a Windows file system marks every file executable, so the bits are set here, not kept.
find "$root" -type d -exec chmod 755 {} +
find "$root" -type f -exec chmod 644 {} +
while IFS= read -r -d '' file; do
  if [ "$(head -c 4 "$file" | od -An -tx1 | tr -d ' ')" = 7f454c46 ] && [[ "$file" != *.so* ]]; then
    chmod 755 "$file"
  fi
done < <(find "$root/opt" -type f -print0)
chmod 755 "$root/usr/bin/rcynq" "$root/usr/bin/rcynq-ui"

installed_size=$(du -sk --exclude=DEBIAN "$root" | cut -f1)
mkdir -p "$root/DEBIAN"
cat > "$root/DEBIAN/control" <<CONTROL
Package: rcynq
Version: $version
Architecture: $architecture
Maintainer: $maintainer
Installed-Size: $installed_size
Depends: git, openssh-client, libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3, libfontconfig1, libx11-6, libice6, libsm6
Section: devel
Priority: optional
Homepage: https://depthrel.github.io/RCynQ/
Description: Deliver uncommitted code to a remote machine, directly or through a relay
 RCynQ (Remote Code sYNchronization, Quick) links the computer you work on with a computer that holds the Git
 repositories, even when the two cannot reach each other. Uncommitted edits travel as signed patches in either
 direction, and a branch comes back with one Pull, without a commit or a push. A Relay in the middle only forwards
 encrypted SSH traffic and never sees the code.
CONTROL

mkdir -p "$output"
package="$(cd "$output" && pwd)/RCynQ-$version-$runtime.deb"
dpkg-deb --root-owner-group --build "$root" "$package" >/dev/null
echo "Package: $package"
