# RCynQ: development rules

RCynQ (Remote Code sYNchronization, Quick) is a free (GPL-3.0-or-later) cross-platform .NET application that delivers
uncommitted code to a remote machine and brings its Git branches back, without commits or pushes: directly, or through Relay nodes that only
forward encrypted SSH traffic. One program serves three roles: Workstation (where you edit), Origin (where the private
repositories are) and Relay (a computer both can reach). Design documentation:
[Docs/En/Architecture.md](Docs/En/Architecture.md).

You are a senior software engineer. You design reliable, maintainable, secure and well-tested .NET applications.

## Git rules

- Never commit and never push. The owner of the repository does both himself. Leave every change in the working tree.
- Never create, switch or delete branches without an explicit command from the owner. Work in the branch that is
  checked out.
- These rules override any skill, plugin or workflow that asks for a commit, a branch or a worktree.

## General rules

- The latest .NET and C# (currently .NET 10, `LangVersion=latest`); shared settings live in `Directory.Build.props`,
  package versions centrally in `Directory.Packages.props`.
- Only free dependencies compatible with GPL-3.0-or-later. Do not add packages without a need. A dependency that ends
  up in a package is listed with its license text in `THIRD-PARTY-NOTICES.txt` in the same change.
- Domain logic does not depend on the UI, hosting or infrastructure. Interfaces, dependency injection, small focused
  services.
- External commands, file paths, configuration values and everything that came over the network are untrusted data:
  validate them and fail with a clear diagnostic.
- Security-sensitive operations (keys, `authorized_keys`, host keys, tokens, signatures, applying patches) are
  explicit, restricted and auditable.
- The repository on an Origin is only read: RCynQ writes no remote, ref, setting or commit into it. Only the files of a
  patch the user sent change there.
- Time comes from `TimeProvider`, so tests are deterministic.
- Critical logic is covered by unit and integration tests; external dependencies (processes, Git, SSH, files, network,
  transports, database) are replaced with mocks and stubs.
- Prefer deterministic, reproducible and testable implementations. Do not implement future stages unless explicitly
  requested. When a requirement is ambiguous, stop and list the ambiguity instead of guessing.

## Code style

- **One type per file.** Every class, struct, enum, record, interface or delegate that is not `private` lives in a file
  of its own named after it. Only `private` nested types may share a file with their owner.
- **Enumerations live in `Enums` folders.** An enum shared by several projects belongs to the shared core:
  `RCynQ.Core/Enums` (namespace `RCynQ.Core.Enums`). An enum that only one project needs (a way of showing something in
  the UI, a status of one service) lives in the `Enums` folder at the root of that project
  (`RCynQ.UI/Enums`, `RCynQ.Ssh/Enums`), with the narrowest access that works as is: `internal` when nothing public
  exposes it, otherwise `public`. Access is never bent around the enum. A `private` nested enum stays inside its owner.
- **Comments.** Every type, method, property, field, constructor and data structure carries a short but meaningful
  `///` comment: what it is for, not how it is written. Private members and tests included; a missing comment on a
  public member is a build error (CS1591).
- **Design.** Follow SOLID, KISS, DRY and YAGNI: small classes with one responsibility, dependencies through
  interfaces, no copy-paste, no code for imagined needs.
- **Readable methods.** Separate the logical steps of a method with blank lines (checks, preparation, the work, the
  result); never write a whole method as one dense block.
- **Naming.**
  - Constants of any access, local ones too, are `UPPER_SNAKE_CASE`: `const int MAX_KNOWN_COMMITS = 200;`.
  - Private and protected fields, `static readonly` ones too, are `_camelCase`: `_startupTimeout`.
  - Public fields, types, methods, properties, events and enum values are PascalCase.
  - Locals and parameters of a method or constructor are `camelCase`, without an underscore.
- **Braces.** Every construct gets a block in braces, even a single line:

  ```csharp
  if (ready)
  {
      return true;
  }
  ```

  never `if (ready) return true;` or a body on the next line without braces.
- **Indentation.** Four spaces, never tabs (two spaces in XML, JSON and YAML files, see `.editorconfig`).
- **Logical operators** stay at the end of a line; a continuation line never starts with `&&` or `||`. This applies to
  LINQ lambdas and to any other expression:

  ```csharp
  var isPublic = path.Equals(ApiRoutes.HEALTH, StringComparison.OrdinalIgnoreCase) ||
      path.Equals(ApiRoutes.PEER_PING, StringComparison.OrdinalIgnoreCase);
  ```
- File-scoped namespaces; the namespace repeats the folder path inside the project.
- **`using` directives.** No unused ones. They go in three blocks, one right after another without blank lines:
  `System` and `System.*`, then the libraries (`Avalonia`, `CliFx`, `Microsoft`, `NSubstitute`, …), then the solution
  (`RCynQ.*`); each block is in alphabetical order. `using static` and aliases come after the blocks:

  ```csharp
  using System.Globalization;
  using Microsoft.Extensions.Logging;
  using RCynQ.Core.Enums;
  using RCynQ.Localization;
  ```

The rules are checked, not only written down. The build fails on a missing XML comment of a public member (CS1591),
on braces, block namespaces, naming and unused usings (analyzers of `.editorconfig`, run by `EnforceCodeStyleInBuild`).
`Tests/RCynQ.Conventions.Tests` parses the sources with Roslyn and checks the rest: one type per file, private nested
types, XML comments of every member, enums in `Enums`, logical operators, the order of usings, time only through
`TimeProvider`, the schema only through migrations and projects in their solution folders.

## Structure

```
Sources/
  Apps/            applications: the service RCynQ.Service (local HTTP API, SignalR events, role pipelines), the
                   desktop application RCynQ.UI (Avalonia, starts the service), the command line RCynQ.Cli (rcynq),
                   the project page RCynQ.Pages (static, GitHub Pages)
  Core/            core contracts and configuration, localization, data (EF Core) and FluentMigrator migrations
  Database/        console database migrator
  Infrastructure/  Git (RCynQ.Git) and SSH (RCynQ.Ssh)
  Sync/            patches, sync base, Git channel, repositories, routes, diagnostics
  Transports/      links to neighbours: abstractions, Direct, AmneziaWG
Tests/             test projects (<Project>.Tests), Shared (the localizers of the tests)
Docs/              documentation by language: En (the main set), De, Es, Fr, Pt, Ru (the same set translated)
Scripts/           packaging: App (the application and its installers), Assets (pictures), Shared (helpers of the
                   others); build-release.ps1 builds a whole release; see Scripts/README.md
```

A new project is added to `RCynQ.slnx` in the solution folder that repeats its path on disk.

## Localization

- Everything a user reads (labels, error and exception messages, events, diagnostics, CLI output) lives in
  `Sources/Core/RCynQ.Localization/Languages/<code>.json` and is taken by key through an injected `ILocalizer`:
  `_localizer.Get("Key")` / `_localizer.Format("Key", …)` in code, `{loc:Loc Key}` in XAML. There are no
  human-language strings in code or markup and no "if the language is …" branches: a language is the chosen pack, not
  a condition. Developer-only log messages stay in English.
- A key is added to every language file at once; English is the default language. The languages are English,
  German, Spanish, French, Portuguese (European) and Russian. The first start takes the language of the operating
  system when there is a pack for it.
- Nodes are named only by their roles: Workstation, Relay, Origin, the same English words in every language. The
  names of earlier versions (Source, Mediator, Dest) and their translations do not appear in texts.
- Every language has a flag (`Scripts/Assets/generate-flags.ps1`), a table on the project page
  (`Sources/Apps/RCynQ.Pages/assets/site.js`), messages and a license page in the setup wizard
  (`Scripts/App/rcynq.iss`, `Scripts/Shared/License/intro.<code>.txt`) and package notes
  (`Scripts/App/Notes/README.<code>.txt`).
- The documentation is kept in every language at once: the set of files in `Docs/En`, `Docs/De`, `Docs/Es`,
  `Docs/Fr`, `Docs/Pt` and `Docs/Ru` is the same, and a change of meaning goes into all of them. Links from outside lead to `Docs/En`. Details:
  [Docs/En/Development.md](Docs/En/Development.md).

## Databases

SQLite, the schema only through FluentMigrator migrations (SQL scripts in
`RCynQ.Core.Data.Migrations.SQLite/Scripts`), data access through EF Core. `EnsureCreated` and EF Core migrations are
not used.
