# RCynQ

RCynQ (рабочее имя SyncBridge) синхронизирует рабочую копию разработчика с закрытым Git-репозиторием через цепочку узлов.

Полное руководство по установке, настройке и использованию: [Docs/UserGuide.md](Docs/UserGuide.md).
Пошаговая настройка MEDIATOR, DEST и SOURCE для тех, кто не разбирается в сетях: [Docs/BeginnerSetupGuide.md](Docs/BeginnerSetupGuide.md).

Опубликованная сборка портативна: настройки (`config/`) и все данные (`data/`) хранятся в папке программы. Для установки на другие узлы раздавайте чистый пакет `RCynQ-<rid>.zip` / `.tar.gz`: в нём нет `config/` и `data/`, то есть личности уже настроенного узла.

- **Source** — рабочая копия разработчика. Отправляет и принимает «живые» патчи от Dest. Git получает только через Dest.
- **Dest** — закрытый репозиторий. Отдаёт ветки через `git-upload-pack` и обменивается патчами с Source.
- **Mediator** — «слепой» ретранслятор. Поднимает транспорт (Direct / AmneziaWG) и работает SSH jump-узлом, содержимое не видит.

Патчи подписываются ECDSA P-256 и защищены от повторов. Применяются атомарно («всё или ничего»). При конфликте выбирается политика:
- оставить локальное;
- применить входящее с резервной копией;
- `git apply --3way`.

## Структура

```
Sources/
  Apps/            RCynQ.Service (локальный API + SignalR), RCynQ.Cli (rcynq), RCynQ.UI (Avalonia)
  Core/            RCynQ.Core (контракты), Core.Data (EF Core SQLite),
                   Core.Data.Migrations (общий раннер FluentMigrator), Core.Data.Migrations.SQLite (скрипты .sql)
  Database/        RCynQ.Dbs.SQLite.DbMigrator (консольный мигратор)
  Infrastructure/  RCynQ.Git, RCynQ.Ssh
  Sync/            RCynQ.Sync (патчи, конфликты, диагностика, конфигурация, автоопределение окружения)
  Transports/      Abstractions, Direct, AmneziaWG
Tests/             модульные и интеграционные тесты по проектам
Scripts/           publish-win.ps1, publish-linux.sh, publish-macos.sh, generate-icon.ps1
Docs/              руководство, спецификация и архитектура
```

## Требования

- .NET SDK 8 или новее (проекты нацелены на `net8.0` и запускаются на более новых рантаймах, `RollForward=Major`).
- `git` и OpenSSH-клиент в `PATH`; на узлах, к которым подключаются соседи, — `sshd`.
- Для транспорта AmneziaWG — `awg` / `awg-quick`.

## Сборка и тесты

```
dotnet build RCynQ.slnx
dotnet test RCynQ.slnx
```

Можно собрать один проект вместе с его зависимостями, без всего solution:

```
dotnet build Sources/Apps/RCynQ.Service/RCynQ.Service.csproj
```

## Отладка в VS Code

Рекомендуемые расширения перечислены в `.vscode/extensions.json`: C# и Avalonia.

1. Откройте «Run and Debug» и выберите профиль: **UI**, **Service**, **CLI**, **DbMigrator** или **Service + UI**.
2. Нажмите **F5**.

Каждый профиль перед запуском выполняет свою задачу сборки (`dotnet build <csproj>`), которая собирает только нужные проекты и их зависимости. Профиль **UI** собирает UI и сервис: приложение само запускает сервис и останавливает его при закрытии. Полная сборка доступна отдельной задачей `build: solution` (Ctrl+Shift+B).

Профили используют локальный отладочный узел в `.vscode/dev`:
- `syncbridge.json` — конфигурация узла;
- `workdir/` — рабочая копия;
- `data/` — база, ключи, логи и токен API.

Для CLI и мигратора аргументы запрашиваются при запуске.

## Публикация и запуск

```
./Scripts/publish-win.ps1                 # win-x64
./Scripts/publish-linux.sh                # linux-x64
./Scripts/publish-macos.sh                # osx-arm64
```

Self-contained Release-сборки Service, CLI, UI и DbMigrator попадают в `artifacts/publish/<rid>/<app>`. В корне этого каталога создаются ярлык запуска (`RCynQ.lnk` на Windows, `RCynQ.desktop` на Linux, `RCynQ.command` на macOS), переносимый запуск `RCynQ.cmd` / `RCynQ.sh` и метка `rcynq.portable`. Рядом появляется чистый пакет `artifacts/publish/RCynQ-<rid>.zip` (Windows) или `.tar.gz`.

Ярлык запускает приложение:
1. при первом запуске открывается окно настройки узла — роль, пути, порт, link-токен;
2. приложение запускает сервис в выбранной роли, в том числе Mediator, и останавливает его вместе с туннелями при закрытии.

На серверах без графики сервис можно запускать отдельно: `RCynQ.Service --config syncbridge.json`.

Обмен конфигурацией между узлами:
- «Экспортировать конфигурацию» определяет адреса, открытость SSH, SSH-ключ и отпечаток ключа хоста средствами ОС и сохраняет файл с публичными данными; из CLI — `rcynq export --auto`;
- «Импортировать конфигурацию» на соседнем узле создаёт звено.

## Иконка

Исходник — `Sources/Apps/RCynQ.UI/Assets/rcynq.svg`. `rcynq.ico` и PNG генерируются скриптом `Scripts/generate-icon.ps1` (Windows).

## Миграции базы

Миграции написаны на FluentMigrator: C#-класс `Versions/M0001_*.cs` запускает встроенный скрипт `Scripts/0001_*.Up.sql` / `.Down.sql`. Сервис и CLI применяют их автоматически. Вручную:

```
RCynQ.Dbs.SQLite.DbMigrator --database <data>/rcynq.db          # применить
RCynQ.Dbs.SQLite.DbMigrator --database <data>/rcynq.db --list   # список применённых версий
```

## Безопасность

- Управляющий API доступен только на loopback и требует токен из `keys/api.token`. Без токена работают только `GET /health` и `POST /patch`; `/patch` проверяет link-токен, подпись и защиту от повторов.
- SSH-ключи ограничиваются через `authorized_keys`:
  - для Git — `restrict,command="git-upload-pack '<repo>'"`;
  - для форвардов — `restrict,port-forwarding,permitopen=...,command="exit 1"`.
- Входящие пути проверяются: запрещены `.git`, выход за корень (zip-slip) и символьные ссылки наружу.
