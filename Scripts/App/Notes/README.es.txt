RCynQ {version} ({runtime}): Remote Code sYNchronization, Quick

[win] Inicio: RCynQ.exe en esta carpeta (puedes crear un acceso directo o anclarlo a la barra de tareas).
[unix] Inicio: ./RCynQ.sh
[linux] Menú de aplicaciones: ejecuta una vez ./install-desktop-entry.sh.
[macos] O haz doble clic en RCynQ.command.
[win] Línea de comandos: cli\rcynq.exe (ejecuta «rcynq --help»).
[unix] Línea de comandos: cli/rcynq (ejecuta «rcynq --help»).
Los ajustes se guardan en la carpeta config y todos los datos (claves, base de datos, registros) en la carpeta data junto al programa: son la identidad de este nodo. Mueve o borra la carpeta RCynQ entera y no copies nunca a otro equipo una carpeta en la que RCynQ ya se haya ejecutado.
En la Workstation y el Origin hace falta Git, y en cada nodo que pase por un Relay, un cliente SSH.
[win] Windows muestra un aviso de SmartScreen mientras la compilación no esté firmada.
[linux] Instala Git y el cliente SSH de tu distribución: sudo apt install git openssh-client (o dnf, pacman, ...).
[macos] La compilación no está firmada por Apple. Antes del primer inicio ejecuta en esta carpeta: xattr -dr com.apple.quarantine .
[macos] Si macOS sigue sin iniciarlo, fírmalo en este Mac: find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
RCynQ es software libre bajo la GNU GPL, versión 3 o posterior (LICENSE.txt); los componentes de otros autores y sus licencias están en THIRD-PARTY-NOTICES.txt.
Guía de configuración: https://github.com/DepthRel/RCynQ/blob/main/Docs/Es/SetupGuide.md
