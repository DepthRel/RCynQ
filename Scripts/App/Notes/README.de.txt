RCynQ {version} ({runtime}): Remote Code sYNchronization, Quick

[win] Start: RCynQ.exe in diesem Ordner (Sie können eine Verknüpfung dazu anlegen oder es an die Taskleiste anheften).
[unix] Start: ./RCynQ.sh
[linux] Anwendungsmenü: führen Sie einmal ./install-desktop-entry.sh aus.
[macos] Oder Doppelklick auf RCynQ.command.
[win] Befehlszeile: cli\rcynq.exe (führen Sie „rcynq --help“ aus).
[unix] Befehlszeile: cli/rcynq (führen Sie „rcynq --help“ aus).
Die Einstellungen liegen im Ordner config und alle Daten (Schlüssel, Datenbank, Protokolle) im Ordner data neben dem Programm: sie sind die Identität dieses Knotens. Verschieben oder löschen Sie den Ordner RCynQ als Ganzes und kopieren Sie nie einen Ordner, in dem RCynQ schon gelaufen ist, auf einen anderen Rechner.
Auf Workstation und Origin wird Git gebraucht, und auf jedem Knoten, der über ein Relay geht, ein SSH-Client.
[win] Windows zeigt eine SmartScreen-Warnung, solange der Build nicht signiert ist.
[linux] Installieren Sie Git und den SSH-Client Ihrer Distribution: sudo apt install git openssh-client (oder dnf, pacman, ...).
[macos] Der Build ist nicht von Apple signiert. Führen Sie vor dem ersten Start in diesem Ordner aus: xattr -dr com.apple.quarantine .
[macos] Wenn macOS ihn trotzdem nicht startet, signieren Sie ihn auf diesem Mac: find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
RCynQ ist freie Software unter der GNU GPL, Version 3 oder später (LICENSE.txt); die Komponenten anderer Autoren und ihre Lizenzen stehen in THIRD-PARTY-NOTICES.txt.
Einrichtungsanleitung: https://github.com/DepthRel/RCynQ/blob/main/Docs/De/SetupGuide.md
