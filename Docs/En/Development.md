# Developing RCynQ

**English** · [Deutsch](../De/Development.md) · [Español](../Es/Development.md) · [Français](../Fr/Development.md) · [Português](../Pt/Development.md) · [Русский](../Ru/Development.md)

## Environment

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git, and an OpenSSH client (`ssh`, `ssh-keygen`, `ssh-keyscan`): the product calls them, and so do the integration
  tests.
- VS Code with C# Dev Kit and the Avalonia extension, or Visual Studio / Rider.
- Windows PowerShell 5.1 or PowerShell 7 for `Scripts/*/*.ps1`, bash for `Scripts/*/*.sh`. The scripts are grouped
  by what they build: `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (see [Scripts/README.md](../../Scripts/README.md)).

## Build and tests

```bash
dotnet build RCynQ.slnx                                   # 0 warnings expected
dotnet test RCynQ.slnx                                    # all tests
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # one project
```

The tests are xUnit 2 on the VSTest runner. The common test packages (xUnit, NSubstitute, FluentAssertions) are
referenced in `Directory.Build.props` for every `*.Tests` project, so a test project file lists only project
references.

| Project | What it checks |
|---|---|
| RCynQ.Conventions.Tests | the rules of AGENTS.md that analyzers cannot check, on the sources parsed with Roslyn: one type per file, private nested types, XML comments, enums in `Enums` folders, logical operators at the end of a line, the order of usings, time only through `TimeProvider`, the schema only through migrations, projects in their solution folders |
| RCynQ.Core.Tests | node roles, records, configuration locations (portable installation), the installation stamp, the validation and the conversion of earlier configuration files, repositories in the configuration |
| RCynQ.Localization.Tests | language packs: reading, the catalog, choosing a language by culture, the English fallback, the same keys and placeholders in every language, every requested key exists (code and screens), no Russian text in the code, nodes named only by their roles |
| RCynQ.Core.Data.Tests | migrations, repositories, storage, file permissions of the data folder |
| RCynQ.Git.Tests | Git commands, bundles, patch building, hashes, `.gitignore`, the export of a repository, the remote of the working copy, integration with a real Git |
| RCynQ.Ssh.Tests | SSH commands, port forwards, `authorized_keys` restrictions, host key pinning, `known_hosts`, the client configuration file |
| RCynQ.MergeTools.Tests | the catalog of merge tools, finding them in usual places, in `PATH`, through `vswhere` and in the Git configuration, the templates of their arguments, starting them |
| RCynQ.Transports.Tests | transports and the link manager, the probe of an endpoint |
| RCynQ.Sync.Tests | patches (signature, replay, delivery, all-or-nothing apply, conflicts, Git merge and merge sessions for a merge tool), the sync base, the Git channel, repositories, routes and their choice, diagnostics, configuration transfer, data retention |
| RCynQ.Service.Tests | the local HTTP API, its token and public endpoints, the startup order, end-to-end Pull, send and ping between two services, repositories end to end |
| RCynQ.Cli.Tests | the `rcynq` commands and their output |
| RCynQ.UI.Tests | view models, dialogs, the window "About RCynQ" and the letter about a problem, the service process controller, the API client, the language and settings stores, the merge in a merge tool and the choice of the tool |
| RCynQ.UI.Headless.Tests | the main window and the window "About RCynQ" in Avalonia Headless: every text changes on the fly with the language |

A test names the language its expectations are written in: `TestTexts.English` or `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`) is handed to the code under test. Nothing in the process chooses a language behind its
back, so parallel tests do not disturb each other.

## Localization

- Everything a user reads (labels, error and exception messages, events, diagnostics, CLI output) lives in
  `Sources/Core/RCynQ.Localization/Languages/<code>.json` and is taken by key: `ILocalizer.Get("Key")` /
  `ILocalizer.Format("Key", …)` in code, `{loc:Loc Key}` in XAML. There are no human-language strings in code or
  markup and no "if the language is …" branches.
- The languages are English (the default), German, Spanish, French, Portuguese (European) and Russian. The first start
  takes the language of the operating system when there is a pack for it, otherwise English. A key is added to every
  language file at once; a translation that lost a key or a placeholder fails `RCynQ.Localization.Tests`.
- Nodes are named only by their roles: Workstation, Relay, Origin, the same English words in every language. The
  words of earlier versions (Source, Mediator, Dest) and their translations are not used in texts
  (`RoleWordingTests`).

### Adding a language

1. Copy `Languages/en.json` to `<code>.json` (a two-letter ISO 639-1 code), fill in the name of the language and
   translate the values. Keys and the placeholders `{0}`, `{1}` do not change.
2. Draw its flag into `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` with `Scripts/Assets/generate-flags.ps1`.
3. Translate what lives outside the application: the setup wizard (`[Languages]`, `[Messages]` and
   `[CustomMessages]` of `Scripts/App/rcynq.iss`, the introduction of its license page
   `Scripts/Shared/License/intro.<code>.txt`), the menu entries of the Linux packages (`Comment[<code>]` in
   `Scripts/App/build-deb.sh` and `publish-common.sh`), the package notes (`Scripts/App/Notes/README.<code>.txt`), the project page (one more
   entry in `assets/languages.js`, a flag in `assets/flags/<code>.svg` and a table in `assets/site.js` of
   `Sources/Apps/RCynQ.Pages`) and the documentation (below).

## Documentation

The documents are kept by language: `Docs/En` is the main set; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` and
`Docs/Ru` hold the same set in German, Spanish, French, Portuguese and Russian. The line under the title of every
document links the same document in the other languages.

- the set of files and their names are the same in every language; a change of meaning goes into all languages at
  once;
- links from outside (the README) lead to `Docs/En`; the project page opens the documents in the language chosen on
  it;
- a new language is a new folder `Docs/<Code>` with the same set, plus one more line in the `docs` table of
  `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Running from VS Code

`.vscode/launch.json`:

| Profile | What it does |
|---|---|
| Service | builds and starts `RCynQ.Service` with `.vscode/dev/syncbridge.json` |
| UI | builds and starts `RCynQ.UI` with the same configuration; the UI starts the service itself |
| CLI | builds and starts `rcynq` with arguments from a prompt (`RCYNQ_CONFIG` points at the same configuration) |
| DbMigrator | builds and starts the migrator with arguments from a prompt |
| Service + UI | both, for debugging the two processes at once |

The build tasks (`.vscode/tasks.json`) call `dotnet build <csproj>`: only the chosen project and what it depends on
are built. A full build of the solution is a separate task, `build: solution`.

Environment variables:

| Variable | Purpose |
|---|---|
| `RCYNQ_CONFIG` | the configuration file instead of the portable `config/syncbridge.json` |
| `RCYNQ_SERVICE_EXE` | the service executable the UI starts (also `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | how the CLI reaches a running service |
| `RCYNQ_*` | any setting of `syncbridge.json` for the service (`RCYNQ_SyncBridge__ListenPort`, …) |

## Database

SQLite. The schema is created only by FluentMigrator migrations; EF Core only maps the model to the tables.
`EnsureCreated` and EF Core migrations are not used. To change the schema:

1. Add `Scripts/000N_Name.Up.sql` and `000N_Name.Down.sql` to `RCynQ.Core.Data.Migrations.SQLite`.
2. Add a class `Versions/M000N_Name.cs` with `[Migration(N, "...")]`.
3. Update the entities, the mapping and `RCynQDbContext`.

The service applies migrations at start. By hand:

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## License

RCynQ is free software under the GNU General Public License, version 3 or later (`LICENSE.txt`).

- Only dependencies compatible with GPL-3.0-or-later. A dependency that ends up in a package is listed with its
  license text in `THIRD-PARTY-NOTICES.txt` in the same change.
- Every package carries `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` next to the program (`Write-PackageLicenses`
  and `write_package_licenses` in `Scripts/Shared/package-notes.*`); the `.deb` adds `/usr/share/doc/rcynq/copyright`.
- The setup wizard shows the license page in every language: the introduction of
  `Scripts/Shared/License/intro.<code>.txt` followed by the English text of the GPL, the only legally binding one.
- The window "About RCynQ" in the settings tells the license and opens the license text and the list of components.

## Code rules

See [AGENTS.md](../../AGENTS.md): one type per file, an XML comment on every member (CS1591 is an error), braces
around every block, blank lines between the logical steps of a method, logical operators at the end of a line,
constants in `UPPER_SNAKE_CASE`, enumerations in `Enums` folders, `using` directives in the blocks System, libraries,
RCynQ, time through `TimeProvider`, untrusted input is always validated. The build (analyzers of `.editorconfig`) and
`RCynQ.Conventions.Tests` check them.

## Packaging

| Script | Result |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` and `RCynQ-<version>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | the same archive plus `RCynQ-<version>-setup.exe` (Inno Setup 6 or newer) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<version>-<runtime>.tar.gz` and, where `dpkg-deb` is (Debian, Ubuntu, WSL), `RCynQ-<version>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<version>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | the same Linux and macOS packages, built from Windows inside WSL |
| `Scripts/build-release.ps1 [-Skip <steps>] [-ContinueOnError]` | every package of a release, one after another, and `SHA256SUMS-<version>.txt` |

The build is self-contained; the target machine needs no .NET, only Git and an SSH client. A package holds the
service, the CLI, the UI and the database migrator in `service/`, `cli/`, `ui/` and `dbmigrator/`. The root of the
Windows package holds `RCynQ.exe` with the application icon: the same .NET host as the UI (the `RCynQRootLauncher`
target in `RCynQ.UI.csproj`), only with the relative path `ui\RCynQ.UI.dll`.

The archives and the Windows installer for the current user are portable: the `rcynq.portable` marker in the root
keeps the configuration in `config/` and all data in `data/` next to the program. The installer for all users puts the
program into `Program Files` without the marker, so every user keeps the configuration in `%APPDATA%\RCynQ` and the
data in `%LOCALAPPDATA%\RCynQ`. The `.deb` package installs into `/opt/rcynq` without the
marker, so every user keeps the configuration in `~/.config/RCynQ` and the data in `~/.local/share/RCynQ`.

The Linux and macOS packages are built on Linux (WSL counts): only there do the executable permissions survive in the
archive. The macOS package built this way is not signed; see the notes inside it.

## GitHub workflows

| Workflow | When | What |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | every push to `main`, every pull request, by hand | builds the solution and runs every test on Windows and Linux; with the style analyzers and the convention tests a broken rule makes the run red |
| **Packages** (`.github/workflows/packages.yml`) | by hand, or a version tag `X.Y.Z` equal to `<Version>` of `Directory.Build.props` | builds the Windows installer and `.zip`, the Linux `.deb` and `.tar.gz`, the macOS archives (on macOS, with an ad-hoc signature); a tag attaches them with `SHA256SUMS-<version>.txt` to a **draft** release |
| **Pages** (`.github/workflows/pages.yml`) | a change of `Sources/Apps/RCynQ.Pages` in `main` | publishes the project page |

A release: bump `<Version>`, push the tag (`git tag 0.2.0; git push origin 0.2.0`), write the notes of the draft
release, check the files, publish it, and only then fill in `updates.json` of the project page.
