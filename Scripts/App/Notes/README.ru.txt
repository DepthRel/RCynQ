RCynQ {version} ({runtime}): Remote Code sYNchronization, Quick

[win] Запуск: RCynQ.exe в этой папке (на него можно сделать ярлык или закрепить на панели задач).
[unix] Запуск: ./RCynQ.sh
[linux] Меню приложений: один раз запустите ./install-desktop-entry.sh.
[macos] Или двойной щелчок по RCynQ.command.
[win] Командная строка: cli\rcynq.exe (запустите «rcynq --help»).
[unix] Командная строка: cli/rcynq (запустите «rcynq --help»).
Настройки хранятся в папке config, а все данные (ключи, база, журналы) — в папке data рядом с программой: это личность этого узла. Переносите и удаляйте папку RCynQ целиком и никогда не копируйте на другой компьютер папку, в которой RCynQ уже запускался.
На узлах Workstation и Origin нужен Git, а на каждом узле, который ходит через Relay, — SSH-клиент.
[win] Windows покажет предупреждение SmartScreen, пока сборка не подписана.
[linux] Установите Git и SSH-клиент вашего дистрибутива: sudo apt install git openssh-client (или dnf, pacman, ...).
[macos] Сборка не подписана Apple. Перед первым запуском выполните в этой папке: xattr -dr com.apple.quarantine .
[macos] Если macOS всё равно не запускает программу, подпишите её на этом Mac: find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
RCynQ — свободная программа под лицензией GNU GPL версии 3 или более поздней (LICENSE.txt); компоненты других авторов и их лицензии перечислены в THIRD-PARTY-NOTICES.txt.
Руководство по настройке: https://github.com/DepthRel/RCynQ/blob/main/Docs/Ru/SetupGuide.md
