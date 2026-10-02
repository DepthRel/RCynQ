RCynQ {version} ({runtime}): Remote Code sYNchronization, Quick

[win] Start: RCynQ.exe in this folder (you can make a shortcut to it or pin it to the taskbar).
[unix] Start: ./RCynQ.sh
[linux] Application menu: run ./install-desktop-entry.sh once.
[macos] Or double-click RCynQ.command.
[win] Command line: cli\rcynq.exe (run "rcynq --help").
[unix] Command line: cli/rcynq (run "rcynq --help").
The settings are kept in the config folder and all data (keys, database, logs) in the data folder next to the program: they are the identity of this node. Move or delete the RCynQ folder as a whole, and never copy a folder in which RCynQ has already run to another computer.
Git is needed on the Workstation and the Origin, and an SSH client on every node that goes through a Relay.
[win] Windows shows a SmartScreen warning while the build is not signed.
[linux] Install Git and the SSH client of your distribution: sudo apt install git openssh-client (or dnf, pacman, ...).
[macos] The build is not signed by Apple. Before the first start run, in this folder: xattr -dr com.apple.quarantine .
[macos] If macOS still refuses to start it, sign it on this Mac: find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
RCynQ is free software under the GNU GPL, version 3 or later (LICENSE.txt); the components of other authors and their licenses are listed in THIRD-PARTY-NOTICES.txt.
Setup guide: https://github.com/DepthRel/RCynQ/blob/main/Docs/En/SetupGuide.md
