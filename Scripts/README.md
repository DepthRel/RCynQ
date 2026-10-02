# Scripts

Every script finds the repository by itself, so it can be started from any folder. The packages go to
`artifacts/publish`; the folder of a runtime there (`artifacts/publish/win-x64`, …) is the published program itself
and may hold the `config` and `data` of a node tried out from it, which never go into a package.

## The release bundle

`build-release.ps1` (Windows) builds every package of a release, one script after another: first the programs for
every system, then the installer.

| Step | Script | Makes |
|---|---|---|
| Windows | `App/publish-win.ps1` | `RCynQ-<version>-win-x64.zip` |
| Linux | `App/publish-linux.ps1` (WSL) | `RCynQ-<version>-linux-x64.tar.gz` and `RCynQ-<version>-linux-x64.deb` |
| MacOS | `App/publish-macos.ps1` (WSL) | `RCynQ-<version>-osx-arm64.tar.gz` (Apple silicon) |
| MacOSIntel | `App/publish-macos.ps1 -Runtime osx-x64` (WSL) | `RCynQ-<version>-osx-x64.tar.gz` |
| WindowsSetup | `App/build-installer.ps1 -SkipPublish` | `RCynQ-<version>-setup.exe` from the Windows folder of the first step |

Each runs in a PowerShell process of its own; the first failure stops the bundle (`-ContinueOnError` runs the rest,
`-Skip MacOS,MacOSIntel` leaves steps out, `-Sign` signs the Windows programs and the installer). At the end it lists
the packages of the version with their size and SHA-256 and writes `artifacts/publish/SHA256SUMS-<version>.txt`.

The machine needs the .NET SDK, WSL with Debian or Ubuntu (the first run installs the .NET SDK into `~/.dotnet` there,
without `sudo`) and Inno Setup 6 or newer. The macOS packages built on Linux are not signed: their `README.txt` says how
to start them on a Mac.

## App — the RCynQ application

| Script | Runs on | Makes |
|---|---|---|
| `App/publish-win.ps1` | Windows | `artifacts/publish/<runtime>` with `RCynQ.exe`, and `RCynQ-<version>-<runtime>.zip` |
| `App/build-installer.ps1` | Windows | the same, plus `RCynQ-<version>-setup.exe` (Inno Setup) from `App/rcynq.iss` |
| `App/publish-linux.ps1`, `App/publish-macos.ps1` | Windows (WSL) | the Linux and macOS packages, built inside WSL by `App/publish-in-wsl.sh` (through `App/publish-in-wsl.ps1`) |
| `App/publish-linux.sh` | Linux | `RCynQ-<version>-<runtime>.tar.gz` and, where `dpkg-deb` is, the `.deb` (`App/build-deb.sh`) |
| `App/publish-macos.sh` | macOS, Linux | `RCynQ-<version>-<runtime>.tar.gz`; on macOS with an ad-hoc signature |

`App/publish-common.sh` does the work of both bash scripts.

The packages:

| Package | Installs into | Settings and data |
|---|---|---|
| `RCynQ-<version>-setup.exe` | `%LOCALAPPDATA%\Programs\RCynQ`, no administrator rights; the welcome page and the header carry the RCynQ logo (`Assets/generate-installer-art.ps1`) | `config` and `data` inside the installation (the `rcynq.portable` marker) |
| `RCynQ-<version>-<runtime>.zip`, `.tar.gz` | wherever it is unpacked | `config` and `data` next to the program |
| `RCynQ-<version>-<runtime>.deb` | `/opt/rcynq`, with `rcynq` and `rcynq-ui` in `/usr/bin`, the menu entry, the icons and the AppStream data; depends on `git` and `openssh-client` | `~/.config/RCynQ` and `~/.local/share/RCynQ` of each user |

## Assets — pictures made from sources

| Script | Makes |
|---|---|
| `Assets/generate-icon.ps1` | the icon of the application (`rcynq.ico`, PNG 256/512) from the geometry of `rcynq.svg` |
| `Assets/generate-flags.ps1` | the flags of the language selector |
| `Assets/generate-installer-art.ps1` | the pictures of the setup wizard in `artifacts/installer-art` (the build script runs it) |

## Licenses

Every package carries `LICENSE.txt` (GPL-3.0-or-later) and `THIRD-PARTY-NOTICES.txt` from the root of the repository,
next to the program (`Write-PackageLicenses` and `write_package_licenses` in `Shared/package-notes.*`); the `.deb`
adds `/usr/share/doc/rcynq/copyright`. The setup wizard shows a license page in every language: `Write-InstallerLicenses`
writes `artifacts/installer-license/LICENSE.<code>.txt` from `Shared/License/intro.<code>.txt` and the English GPL.
A new or updated dependency that ends up in a package is written into `THIRD-PARTY-NOTICES.txt` in the same change.

## Package notes

The `README.txt` of every package comes from templates, one per language: `App/Notes/README.<code>.txt`.
`README.en.txt` becomes `README.txt`, the others keep their code; `{version}` and `{runtime}` are filled in. A line
tagged `[win]`, `[linux]`, `[macos]` or `[unix]` (Linux and macOS) goes only into the packages of that system.
A new language is one more template.

## Shared — helpers of the other scripts

| Script | Used by |
|---|---|
| `Shared/package-notes.ps1` | `App/publish-win.ps1` (dot-sourced) |
| `Shared/package-notes.sh` | `App/publish-common.sh` (sourced) |
| `Shared/sign-file.ps1` | `App/publish-win.ps1` and the installer with `-Sign`; the certificate comes from `RCYNQ_SIGN_THUMBPRINT` or `RCYNQ_SIGN_PFX` + `RCYNQ_SIGN_PFX_PASSWORD` |

More: [Docs/En/Development.md](../Docs/En/Development.md).
