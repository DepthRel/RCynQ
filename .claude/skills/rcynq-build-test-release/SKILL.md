---
name: rcynq-build-test-release
description: Use when building, testing, running, packaging or releasing RCynQ or its project page, or when an RCynQ test fails unexpectedly.
---

# RCynQ build, test and release

## Quick reference

```bash
dotnet build RCynQ.slnx                                      # 0 warnings expected
dotnet test RCynQ.slnx                                       # xUnit 2 on VSTest, ~850 tests
dotnet test Tests/RCynQ.Sync.Tests --no-build                # one project after a build
dotnet run --project Sources/Apps/RCynQ.UI -- --config .vscode/dev/syncbridge.json
```

| Need | Command / place |
|---|---|
| Debug the service, UI, CLI or migrator | VS Code profiles in `.vscode/launch.json` (config `.vscode/dev/syncbridge.json`) |
| Windows folder + zip | `Scripts/App/publish-win.ps1` → `artifacts/publish/win-x64`, `RCynQ-<version>-win-x64.zip` |
| Windows installer | `Scripts/App/build-installer.ps1 [-SkipPublish]` (Inno Setup 6+, found in Program Files or PATH) |
| Linux `.tar.gz` + `.deb` from Windows | `Scripts/App/publish-linux.ps1` (WSL, Ubuntu) |
| macOS `.tar.gz` from Windows | `Scripts/App/publish-macos.ps1 [-Runtime osx-x64]` (WSL; unsigned) |
| Whole release | `Scripts/build-release.ps1 [-Skip …] [-ContinueOnError] [-Sign]` → packages + `SHA256SUMS-<version>.txt` |
| Installer pictures | `Scripts/Assets/generate-installer-art.ps1` (the installer build runs it) |
| Site locally | any static server in `Sources/Apps/RCynQ.Pages` (CSP blocks `file://`) |

## Test facts

- Tests name their language: `TestTexts.English` / `TestTexts.Russian` from `Tests/Shared/TestTexts.cs` (linked into
  every test project except `RCynQ.Conventions.Tests`).
- Git and SSH integration tests call the real `git` and `ssh-keygen`; they need both on `PATH`.
- `RCynQ.Service.Tests` start real services on loopback ports and take the longest (~40 s).

## Release

1. Bump `<Version>` in `Directory.Build.props`.
2. The owner commits, tags `X.Y.Z` (equal to `<Version>`) and pushes himself (see the Git rules of `AGENTS.md`). The
   **Packages** workflow builds every package (Windows, Linux, macOS arm64/x64 on macOS) and `SHA256SUMS-<version>.txt`
   into a **draft** release. Locally the same packages come from `Scripts/build-release.ps1`.
3. Write the notes, check the files, publish the release; only then fill in `Sources/Apps/RCynQ.Pages/updates.json`
   (the download buttons of the page read it; an early update sends visitors to a 404).

CI (`.github/workflows/ci.yml`) builds and tests every push to `main` and every PR on Windows and Linux; code signing
belongs in the Windows job of `packages.yml`.

`artifacts/publish/<runtime>` is the published program itself and may hold `config/` and `data/` of a node tried from
it; the scripts never put them into a package. Never copy such a folder to another machine.

## Environment gotchas (Windows dev box)

- `core.autocrlf=true`: `.cs/.md/.json` are CRLF in the working tree, `*.sh` LF (`.gitattributes`).
- Backslashes in `bash -c`/heredoc/`sed` replacements get eaten or turned into control characters (`\r`): edit files
  that contain backslashes with an editor tool or a script file, not inline shell.
- WSL builds copy the sources into `~/rcynq-build` and install the .NET SDK into `~/.dotnet` on the first run.
