# Разработка RCynQ

[English](../En/Development.md) · [Deutsch](../De/Development.md) · [Español](../Es/Development.md) · [Français](../Fr/Development.md) · [Português](../Pt/Development.md) · **Русский**

## Окружение

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git и клиент OpenSSH (`ssh`, `ssh-keygen`, `ssh-keyscan`): их вызывает продукт и интеграционные тесты.
- VS Code с C# Dev Kit и расширением Avalonia, либо Visual Studio / Rider.
- Windows PowerShell 5.1 или PowerShell 7 для `Scripts/*/*.ps1`, bash для `Scripts/*/*.sh`. Скрипты разложены по
  тому, что они собирают: `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (см. [Scripts/README.md](../../Scripts/README.md)).

## Сборка и тесты

```bash
dotnet build RCynQ.slnx                                   # ожидается 0 предупреждений
dotnet test RCynQ.slnx                                    # все тесты
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # один проект
```

Тесты написаны на xUnit 2 и запускаются через VSTest. Общие тестовые пакеты (xUnit, NSubstitute, FluentAssertions)
подключены в `Directory.Build.props` для каждого проекта `*.Tests`, поэтому файл тестового проекта перечисляет только
ссылки на проекты.

| Проект | Что проверяет |
|---|---|
| RCynQ.Conventions.Tests | правила AGENTS.md, которые не проверяют анализаторы, на исходниках, разобранных Roslyn: один тип в файле, приватные вложенные типы, XML-комментарии, перечисления в папках `Enums`, логические операторы в конце строки, порядок using, время только через `TimeProvider`, схема только через миграции, проекты в своих папках решения |
| RCynQ.Core.Tests | роли узлов, записи, расположение конфигурации (портативная установка), отметка установки, проверка и преобразование конфигураций прежних версий, репозитории в конфигурации |
| RCynQ.Localization.Tests | языковые пакеты: чтение, каталог, выбор языка по культуре, откат на английский, одинаковые ключи и подстановки во всех языках, существование каждого запрошенного ключа (код и экраны), отсутствие русского текста в коде, названия узлов только по ролям |
| RCynQ.Core.Data.Tests | миграции, репозитории, хранилище, права на файлы папки данных |
| RCynQ.Git.Tests | команды Git, бандлы, сборка патчей, хеши, `.gitignore`, экспорт репозитория, remote рабочей копии, интеграция с настоящим Git |
| RCynQ.Ssh.Tests | команды SSH, пробросы портов, ограничения `authorized_keys`, закрепление ключа хоста, `known_hosts`, файл конфигурации клиента |
| RCynQ.Transports.Tests | транспорты и менеджер связей, проверка конечной точки |
| RCynQ.Sync.Tests | патчи (подпись, защита от повтора, доставка, применение «всё или ничего», конфликты и слияние Git), база синхронизации, Git-канал, репозитории, маршруты и их выбор, диагностика, перенос конфигурации, хранение данных |
| RCynQ.Service.Tests | локальный HTTP API, его токен и публичные точки, порядок запуска, сквозные Pull, отправка и ping между двумя службами, репозитории от начала до конца |
| RCynQ.Cli.Tests | команды `rcynq` и их вывод |
| RCynQ.UI.Tests | модели представлений, диалоги, окно «О программе RCynQ» и письмо о проблеме, контроллер процесса службы, клиент API, хранилища языка и настроек |
| RCynQ.UI.Headless.Tests | главное окно и окно «О программе RCynQ» в Avalonia Headless: все тексты меняются на лету вместе с языком |

Тест сам называет язык своих ожиданий: в проверяемый код передаётся `TestTexts.English` или `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`). Ничто в процессе не выбирает язык за спиной теста, поэтому параллельные тесты не мешают
друг другу.

## Локализация

- Всё, что читает пользователь (подписи, сообщения об ошибках и исключениях, события, диагностика, вывод CLI), лежит
  в `Sources/Core/RCynQ.Localization/Languages/<код>.json` и берётся по ключу: `ILocalizer.Get("Key")` /
  `ILocalizer.Format("Key", …)` в коде, `{loc:Loc Key}` в XAML. В коде и разметке нет строк на человеческом языке и
  нет ветвлений «если язык такой-то».
- Языки: английский (по умолчанию), немецкий, испанский, французский, португальский (европейский) и русский. При
  первом запуске берётся язык операционной системы, если для него есть пакет, иначе английский. Ключ добавляется
  сразу во все языковые файлы; перевод, потерявший ключ или подстановку, роняет `RCynQ.Localization.Tests`.
- Узлы называются только по ролям: Workstation, Relay, Origin — одними и теми же английскими словами во всех языках.
  Слова прежних версий (Source, Mediator, Dest) и их переводы в текстах не используются (`RoleWordingTests`).

### Добавление языка

1. Скопируйте `Languages/en.json` в `<код>.json` (двухбуквенный код ISO 639-1), впишите название языка и переведите
   значения. Ключи и подстановки `{0}`, `{1}` не меняются.
2. Нарисуйте его флаг в `Sources/Apps/RCynQ.UI/Assets/Flags/<код>.png` скриптом `Scripts/Assets/generate-flags.ps1`.
3. Переведите то, что живёт вне приложения: мастер установки (`[Languages]`, `[Messages]` и `[CustomMessages]` в
   `Scripts/App/rcynq.iss`, вступление страницы лицензии `Scripts/Shared/License/intro.<код>.txt`), пункты меню
   пакетов для Linux (`Comment[<код>]` в `Scripts/App/build-deb.sh` и `publish-common.sh`), заметки пакетов (`Scripts/App/Notes/README.<код>.txt`), страницу проекта (ещё одна
   запись в `assets/languages.js`, флаг в `assets/flags/<код>.svg` и таблица в `assets/site.js` из
   `Sources/Apps/RCynQ.Pages`) и документацию (ниже).

## Документация

Документы хранятся по языкам: `Docs/En` — основной набор; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` и `Docs/Ru` —
тот же набор на немецком, испанском, французском, португальском и русском. Строка под заголовком каждого документа
ведёт на тот же документ на других языках.

- набор файлов и их имена одинаковы во всех языках; изменение смысла вносится сразу во все языки;
- ссылки извне (README) ведут в `Docs/En`; страница проекта открывает документы на языке, выбранном на ней;
- новый язык — новая папка `Docs/<Код>` с тем же набором и ещё одна строка в таблице `docs` файла
  `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Запуск из VS Code

`.vscode/launch.json`:

| Профиль | Что делает |
|---|---|
| Service | собирает и запускает `RCynQ.Service` с `.vscode/dev/syncbridge.json` |
| UI | собирает и запускает `RCynQ.UI` с той же конфигурацией; службу UI запускает сам |
| CLI | собирает и запускает `rcynq` с аргументами из запроса (`RCYNQ_CONFIG` указывает на ту же конфигурацию) |
| DbMigrator | собирает и запускает мигратор с аргументами из запроса |
| Service + UI | оба сразу, чтобы отлаживать два процесса одновременно |

Задачи сборки (`.vscode/tasks.json`) вызывают `dotnet build <csproj>`: собирается только выбранный проект и то, от
чего он зависит. Полная сборка решения — отдельная задача `build: solution`.

Переменные окружения:

| Переменная | Назначение |
|---|---|
| `RCYNQ_CONFIG` | файл конфигурации вместо портативного `config/syncbridge.json` |
| `RCYNQ_SERVICE_EXE` | исполняемый файл службы, который запускает UI (также `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | как CLI находит запущенную службу |
| `RCYNQ_*` | любая настройка `syncbridge.json` для службы (`RCYNQ_SyncBridge__ListenPort`, …) |

## База данных

SQLite. Схема создаётся только миграциями FluentMigrator; EF Core лишь отображает модель на таблицы. `EnsureCreated` и
миграции EF Core не используются. Чтобы изменить схему:

1. Добавьте `Scripts/000N_Name.Up.sql` и `000N_Name.Down.sql` в `RCynQ.Core.Data.Migrations.SQLite`.
2. Добавьте класс `Versions/M000N_Name.cs` с `[Migration(N, "...")]`.
3. Обновите сущности, отображение и `RCynQDbContext`.

Служба применяет миграции при запуске. Вручную:

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## Лицензия

RCynQ — свободная программа под Стандартной общественной лицензией GNU версии 3 или более поздней (`LICENSE.txt`).

- Только зависимости, совместимые с GPL-3.0-or-later. Зависимость, попадающая в пакет, вносится вместе с текстом её
  лицензии в `THIRD-PARTY-NOTICES.txt` в том же изменении.
- Каждый пакет содержит `LICENSE.txt` и `THIRD-PARTY-NOTICES.txt` рядом с программой (`Write-PackageLicenses` и
  `write_package_licenses` в `Scripts/Shared/package-notes.*`); `.deb` добавляет `/usr/share/doc/rcynq/copyright`.
- Мастер установки показывает страницу лицензии на каждом языке: вступление из
  `Scripts/Shared/License/intro.<код>.txt` и английский текст GPL — юридическую силу имеет только он.
- Окно «О программе RCynQ» в настройках называет лицензию и открывает её текст и список компонентов.

## Правила кода

См. [AGENTS.md](../../AGENTS.md): один тип в файле, XML-комментарий у каждого члена (CS1591 — ошибка), фигурные скобки
у каждого блока, пустые строки между логическими шагами метода, логические операторы в конце строки, константы в
`UPPER_SNAKE_CASE`, перечисления в папках `Enums`, директивы `using` блоками System, библиотеки, RCynQ, время через
`TimeProvider`, недоверенный ввод всегда проверяется. Их проверяют сборка (анализаторы `.editorconfig`) и
`RCynQ.Conventions.Tests`.

## Упаковка

| Скрипт | Результат |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` и `RCynQ-<версия>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | тот же архив и `RCynQ-<версия>-setup.exe` (Inno Setup 6 или новее) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<версия>-<runtime>.tar.gz` и, где есть `dpkg-deb` (Debian, Ubuntu, WSL), `RCynQ-<версия>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<версия>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | те же пакеты для Linux и macOS, собранные из Windows внутри WSL |
| `Scripts/build-release.ps1 [-Skip <шаги>] [-ContinueOnError]` | все пакеты выпуска по очереди и `SHA256SUMS-<версия>.txt` |

Сборка автономная: на целевой машине не нужен .NET, только Git и SSH-клиент. Пакет содержит службу, CLI, UI и
мигратор базы в `service/`, `cli/`, `ui/` и `dbmigrator/`. В корне пакета для Windows лежит `RCynQ.exe` со значком
приложения: тот же хост .NET, что и у UI (цель `RCynQRootLauncher` в `RCynQ.UI.csproj`), только с относительным путём
`ui\RCynQ.UI.dll`.

Архивы и установщик для Windows портативны: метка `rcynq.portable` в корне держит конфигурацию в `config/`, а все
данные в `data/` рядом с программой. Пакет `.deb` ставится в `/opt/rcynq` без метки, поэтому каждый пользователь хранит
конфигурацию в `~/.config/RCynQ`, а данные в `~/.local/share/RCynQ`.

Пакеты для Linux и macOS собираются в Linux (WSL тоже подходит): только там в архиве сохраняются права на запуск.
Собранный так пакет для macOS не подписан; см. заметку внутри него.

## Workflow на GitHub

| Workflow | Когда | Что делает |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | каждый push в `main`, каждый pull request, вручную | собирает решение и запускает все тесты на Windows и Linux; вместе с анализаторами стиля и тестами соглашений нарушенное правило делает прогон красным |
| **Packages** (`.github/workflows/packages.yml`) | вручную или по тегу версии `X.Y.Z`, равному `<Version>` из `Directory.Build.props` | собирает установщик и `.zip` для Windows, `.deb` и `.tar.gz` для Linux, архивы для macOS (на macOS, с ad-hoc подписью); тег прикладывает их вместе с `SHA256SUMS-<версия>.txt` к **черновику** выпуска |
| **Pages** (`.github/workflows/pages.yml`) | изменение `Sources/Apps/RCynQ.Pages` в `main` | публикует страницу проекта |

Выпуск: поднимите `<Version>`, отправьте тег (`git tag 0.2.0; git push origin 0.2.0`), напишите заметки черновика
выпуска, проверьте файлы, опубликуйте и только после этого заполните `updates.json` страницы проекта.
