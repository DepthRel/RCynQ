RCynQ {version} ({runtime}) : Remote Code sYNchronization, Quick

[win] Démarrage : RCynQ.exe dans ce dossier (vous pouvez créer un raccourci ou l'épingler à la barre des tâches).
[unix] Démarrage : ./RCynQ.sh
[linux] Menu des applications : lancez une fois ./install-desktop-entry.sh.
[macos] Ou double-cliquez sur RCynQ.command.
[win] Ligne de commande : cli\rcynq.exe (lancez « rcynq --help »).
[unix] Ligne de commande : cli/rcynq (lancez « rcynq --help »).
Les réglages sont dans le dossier config et toutes les données (clés, base de données, journaux) dans le dossier data à côté du programme : c'est l'identité de ce nœud. Déplacez ou supprimez le dossier RCynQ en entier, et ne copiez jamais sur un autre ordinateur un dossier dans lequel RCynQ a déjà tourné.
Git est nécessaire sur la Workstation et l'Origin, et un client SSH sur chaque nœud qui passe par un Relay.
[win] Windows affiche un avertissement SmartScreen tant que la version n'est pas signée.
[linux] Installez Git et le client SSH de votre distribution : sudo apt install git openssh-client (ou dnf, pacman, ...).
[macos] La version n'est pas signée par Apple. Avant le premier démarrage, lancez dans ce dossier : xattr -dr com.apple.quarantine .
[macos] Si macOS refuse toujours de la lancer, signez-la sur ce Mac : find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
RCynQ est un logiciel libre sous GNU GPL, version 3 ou ultérieure (LICENSE.txt) ; les composants d'autres auteurs et leurs licences sont listés dans THIRD-PARTY-NOTICES.txt.
Guide d'installation : https://github.com/DepthRel/RCynQ/blob/main/Docs/Fr/SetupGuide.md
