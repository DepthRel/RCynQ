# SyncBridge — техническое задание на реализацию

## 1. Что строим

Кроссплатформенное .NET-приложение (Avalonia UI) с тремя ролями узла — **Source**, **Mediator**, **Dest** — синхронизирующее незакоммиченные изменения кода между SOURCE и DEST через цепочку из 0..N "слепых" MEDIATOR-узлов.

Ключевые принципы:

- Git-репозиторий существует **только на DEST**. SOURCE — read-only клон (только `pull`/`fetch`) плюс локальная рабочая копия.
- Живые изменения (ещё не закоммиченные) передаются SOURCE → DEST отдельным **патч-каналом** (`git diff --no-index` → `git apply`), без коммитов на любой стороне.
- Весь релей-транспорт — **SSH** (multi-hop ProxyJump + git-over-ssh). MEDIATOR — обычный ограниченный SSH jump-host, не содержит логики приложения и не видит содержимое трафика.
- Транспорт на каждом отдельном звене цепочки настраивается независимо: `Direct` (существующая сеть/корп. VPN) или `AmneziaWG` (обфускация для обхода DPI-блокировок на звеньях, пересекающих цензурируемую границу).
- Приложение кроссплатформенное с первого дня (Windows/Linux/macOS).

---

## 2. Состав решения — какие проекты создаём

| Проект | Тип | Назначение | Паттерны | Где используется |
|---|---|---|---|---|
| `SyncBridge.Core` | Class library | Общие модели и контракты: `NodeRole`, `NodeConfig`, `PeerDescriptor`, `PatchEnvelope`, `SyncState` | POCO/DTO | везде |
| `SyncBridge.Diagnostics` | Class library | Самопроверка окружения перед запуском/по кнопке | Strategy (`IDiagnosticCheck`), Chain of Responsibility | везде |
| `SyncBridge.SshRelay` | Class library | Обёртка над системными `ssh`/`git`: построение ProxyJump-команды, git-over-ssh, управление `authorized_keys` | Adapter (над внешними процессами), Facade | везде |
| `SyncBridge.Tunnel` | Class library | Транспорт конкретного звена: `Direct` / `AmneziaWG`, задел под будущие протоколы | Strategy (`ITransportLink`) | везде |
| `SyncBridge.SyncEngine` | Class library | Снимки рабочей копии, вычисление и применение патчей, разрешение конфликтов | Command, Template Method | только Source и Dest |
| `SyncBridge.Persistence` | Class library | Локальное состояние в SQLite: узлы, пиры, снимки, история синхронизаций | Repository | везде |
| `SyncBridge.Service` | Worker Service (исполняемый) | Фоновый процесс: DI-контейнер, хостинг Kestrel + SignalR (роль Dest), запуск watcher'а (роль Source), IPC-сервер для UI | Factory (`RolePipelineFactory`), Mediator/Observer (событийная шина) | везде |
| `SyncBridge.UI` | Avalonia app (исполняемый) | Десктопный клиент: `TabControl`, MVVM | MVVM, Command (`RelayCommand`) | Source, Dest, опционально Mediator |
| `SyncBridge.Cli` | Console app (исполняемый) | Headless-управление узлом без GUI | Command | обычно Mediator |

`SyncBridge.UI` и `SyncBridge.Cli` — это два взаимозаменяемых фронта над одним и тем же `SyncBridge.Service`; сам Service не знает, кто им управляет.

---

## 3. Пошаговый план реализации

1. **Solution и скелет проектов.** `dotnet new sln`, затем classlib для Core/Diagnostics/SshRelay/Tunnel/SyncEngine/Persistence, worker для Service, `avalonia.app` для UI, console для Cli. Настроить ссылки между проектами согласно таблице выше.

2. **`SyncBridge.Core`.** Определить:
   - `enum NodeRole { Source, Mediator, Dest }`
   - `record NodeConfig(NodeRole Role, string DisplayName, string? RepoPath, string? WorkdirPath, byte[] PublicKey)`
   - `record PeerDescriptor(string NodeId, NodeRole Role, string Address, int Port, string HostKeyFingerprint)`
   - `record PatchEnvelope(Guid Id, DateTimeOffset CreatedAt, byte[] PatchBytes, string BaseSnapshotHash)`
   - `record SyncState(DateTimeOffset LastSyncedAt, string SnapshotHash, IReadOnlyList<string> PendingFiles)`

3. **`SyncBridge.Diagnostics`.** Интерфейс `IDiagnosticCheck { string Name; Task<DiagnosticResult> RunAsync(NodeConfig cfg); }`. Реализовать проверки (см. таблицу в разделе 8), собрать их в `DiagnosticsRunner`, который прогоняет только те, что применимы к текущей роли.

4. **`SyncBridge.SshRelay`.** Обёртка над `Process`:
   - `Task<string> BuildProxyJumpCommandAsync(IReadOnlyList<PeerDescriptor> chain, PeerDescriptor target)` — собирает `ssh -J host1,host2,... user@target`
   - `Task CloneOrPullAsync(string remoteSshUrl, string localPath)` — git-over-ssh, только fetch/pull
   - `Task<byte[]> ComputeDiffAsync(string snapshotPath, string workdirPath)` — `git diff --no-index`
   - `Task ApplyPatchAsync(byte[] patch, string targetPath, bool threeWay)` — `git apply` / `git apply --3way`
   - `Task AddRestrictedAuthorizedKeyAsync(NodeRole forRole, string publicKey, string? permitOpenTarget)` — пишет строку в `authorized_keys` с `restrict,port-forwarding,permitopen="host:port"` (Mediator) или `command="git-upload-pack '<repo>'"` (Dest)

5. **`SyncBridge.Tunnel`.** Интерфейс `ITransportLink { Task StartAsync(); Task StopAsync(); bool IsHealthy { get; } }`. Реализации `DirectLink` (no-op, просто проверка доступности) и `AmneziaWgLink` (запускает/останавливает `awg-quick` через `Process`, парсит статус). `TransportFactory` создаёт нужную реализацию по конфигу звена.

6. **`SyncBridge.SyncEngine`.** `FileSnapshotService` — хранит теневую копию последнего синхронизированного состояния (простое копирование файлов, без git-репозитория). `ConflictResolver` — обрабатывает результат `git apply`, при неудаче на Dest пробует `--3way`, на Source — оставляет `.rej`.

7. **`SyncBridge.Persistence`.** SQLite-схема: `Nodes(Id, Role, DisplayName, RepoPath, WorkdirPath)`, `Peers(NodeId, Role, Address, Port, Fingerprint)`, `Snapshots(Hash, CreatedAt, Path)`, `SyncHistory(Id, Direction, FileCount, Status, Timestamp)`.

8. **`SyncBridge.Service`.** Generic Host, DI-регистрация всех сервисов выше. `RolePipelineFactory` по `NodeConfig.Role` собирает нужный конвейер:
   - **Source pipeline**: `FileSnapshotService` (watcher, ручной триггер) + `SshRelay` (pull) + IPC-сервер для UI
   - **Mediator pipeline**: только `Diagnostics` + статус подключений (никакого `SyncEngine`)
   - **Dest pipeline**: Kestrel minimal API (`POST /patch`) + SignalR hub (`/notifications`) + `SshRelay` (git-серверная часть — фактически просто корректно настроенный `sshd`, приложение только проверяет его состояние)

9. **`SyncBridge.UI`.** `MainWindow` с `TabControl` (раскладка — раздел 6). ViewModels на `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`). Подключение к `SyncBridge.Service` — SignalR-клиент через named pipe (тот же стек, что и для удалённых уведомлений — не заводим два разных транспорта для локального и удалённого IPC).

10. **`SyncBridge.Cli`.** Команды: `syncbridge role set <source|mediator|dest>`, `syncbridge peer export`, `syncbridge peer import <file>`, `syncbridge diagnostics run`, `syncbridge status`, `syncbridge tunnel up|down <peer>`.

11. **Упаковка.** `dotnet publish -r <win-x64|linux-x64|osx-arm64> --self-contained` для каждой платформы. Нативные инсталляторы (MSIX/WiX, `.deb`/AppImage, `.pkg`) — второй итерацией, для MVP достаточно zip-архива с README.

12. **End-to-end проверка.** Три виртуалки/контейнера (Source/Mediator/Dest), сценарий: правка файла на Source → "Отправить изменения" → патч применяется на Dest → намеренно создать конфликт (правка того же файла на Dest до применения) → убедиться, что `--3way` корректно оставляет конфликт-маркеры, а не затирает молча.

---

## 4. Протокол патч-канала — пример конверта

```json
{
  "id": "6f2a2e2a-2f3a-4b1a-9c1a-8b1b2c3d4e5f",
  "createdAt": "2026-09-13T10:15:00Z",
  "baseSnapshotHash": "b7e23ec29af22b0b4e41da31e868d57226121c84",
  "patch": "<base64: unified diff от git diff --no-index>",
  "signature": "<опционально: подпись содержимого, если нужен доп. слой целостности>"
}
```

Передаётся POST-запросом на `https://127.0.0.1:<forwarded-port>/patch`, где порт — локальный конец SSH-туннеля до DEST. MEDIATOR-узлы этот запрос не видят предметно — только зашифрованный SSH-трафик своего звена.

---

## 5. UI — раскладка по вкладкам

Главное окно: `TabControl` сверху, содержимое вкладок адаптируется под роль узла (для Mediator скрыты «Синхронизация» и «Ветки»).

**Обзор**
- Статус по каждому звену цепочки (иконка + подпись: онлайн / нет связи)
- Время последней синхронизации
- Быстрый переход на вкладку с проблемой, если что-то не в порядке

**Роль и узлы**
- Выпадающий список «Роль узла» (Source / Mediator / Dest)
- Поле пути (рабочая копия для Source, репозиторий для Dest) + кнопка «Обзор…»
- Таблица узлов цепочки: Имя, Роль, Адрес, Статус
- Кнопки: «Экспортировать конфигурацию», «Импортировать конфигурацию», «Добавить узел», «Удалить узел» (в строке таблицы)

**Синхронизация** (Source / Dest)
- Бейдж роли + «Последняя синхронизация: …»
- Кнопка «Отправить изменения» (только Source)
- Список ожидающих отправки файлов
- История применённых патчей: время, кол-во файлов, статус, кнопка «Просмотреть диф» (только Dest)

**Ветки** (Source / Dest)
- Текущая ветка
- Кнопка «Pull изменения»
- Переключатель ветки + индикатор «расхождение с DEST: N коммитов»

**Туннель**
- Список звеньев цепочки, для каждого — выбор транспорта (Direct / AmneziaWG)
- Кнопка «Поднять тоннель» / «Остановить тоннель» на звеньях с AmneziaWG
- Индикатор состояния по каждому звену

**Диагностика**
- Кнопка «Запустить проверку»
- Список проверок с результатом (✓/✗) и кнопкой «Исправить автоматически» там, где применимо

**Логи**
- Фильтр по уровню (Info/Warning/Error)
- Список записей с таймстемпами
- Кнопка «Экспортировать логи»

---

## 6. Технологический стек

**Должно быть установлено на машине:**

| Компонент | Где | Зачем |
|---|---|---|
| Git ≥ 2.30 | Source, Dest | `diff --no-index` / `apply` на Source; полноценный репозиторий + `git-upload-pack` на Dest |
| OpenSSH-клиент | Source, все Mediator, Dest | ProxyJump-цепочка |
| OpenSSH-сервер (`sshd`) | все Mediator, Dest | jump-host, приём git/patch-соединений |
| AmneziaWG (клиент + драйвер) | на звеньях, пересекающих цензурируемую границу | обход DPI-блокировок |
| .NET 8 Runtime | все узлы, если публикация framework-dependent | выполнение приложения |

**Используется внутри приложения:**

| Слой | Технология |
|---|---|
| UI | Avalonia UI, Fluent-тема, CommunityToolkit.Mvvm |
| Фоновый хост | Generic Host (`Microsoft.Extensions.Hosting`), кроссплатформенная служба |
| SSH-оркестрация | системный `ssh` через `Process` |
| Git-обвязка | `git` через `Process`; LibGit2Sharp — только для быстрого чтения статуса в UI |
| Патч-канал | ASP.NET Core Kestrel (minimal API) на Dest |
| Уведомления/IPC | SignalR — и для удалённых нотификаций, и для локального UI↔Service |
| Обфускация | AmneziaWG через `awg-quick` |
| Локальное состояние | SQLite (`Microsoft.Data.Sqlite`) |
| Логи | Serilog + rolling file sink |
| Упаковка | `dotnet publish` self-contained по RID |

---

## 7. Диагностика по ролям

| Проверка | Source | Mediator | Dest |
|---|---|---|---|
| Git установлен, версия ≥ 2.30 | ✓ | — | ✓ |
| SSH-клиент доступен | ✓ | ✓ | ✓ |
| `sshd` запущен и слушает | — | ✓ | ✓ |
| Рабочая копия доступна для записи | ✓ | — | — |
| Репозиторий валиден, `user.name`/`user.email` заданы | — | — | ✓ |
| Снимок-снапшот существует и не повреждён | ✓ | — | — |
| Сетевая доступность соседних узлов | ✓ | ✓ | ✓ |
| AmneziaWG бинарники и права на драйвер (если включено) | ✓ | ✓ | — |

---

## 8. Безопасность — чеклист

- SSH — единственный транспорт для логики приложения; ключи в `authorized_keys` ограничены (`restrict,port-forwarding,permitopen=...` на Mediator, `command="git-upload-pack ..."` на Dest — без `git-receive-pack`, т.е. без возможности push).
- AmneziaWG — обязателен на звеньях, пересекающих цензурируемую границу; выбор транспорта независим для каждого звена.
- Транспортный слой абстрагирован через `ITransportLink`, чтобы можно было добавить альтернативную обфускацию, если конкретный протокол начнут фингерпринтить.
- Секреты (приватные ключи) — через штатное хранилище ОС (DPAPI на Windows, `libsecret`/keychain на Linux/macOS), не в открытом конфиге.
- Первичный обмен ключами/конфигом — ручной (экспорт/импорт файла), без сетевой автоматики на этапе pairing.

---

## 9. Роадмап

1. **MVP**: прямое соединение Source↔Dest (0 Mediator), патч-канал, ручная кнопка отправки, git pull для веток, базовая диагностика.
2. **Relay**: добавление одного Mediator как SSH jump-host, конфигурируемый транспорт (Direct/AmneziaWG) на звене.
3. **Цепочка**: поддержка произвольного числа Mediator, обработка конфликтов (`--3way` / `--reject`), SignalR-уведомления вместо ручного триггера.
4. **Продакшн**: нативные инсталляторы, полноценный мастер pairing, ротация ключей, второй обфускационный протокол про запас.
