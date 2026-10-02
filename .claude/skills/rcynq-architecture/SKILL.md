---
name: rcynq-architecture
description: Use when working anywhere in the RCynQ repository and you need to know which module owns a behaviour, how patches, the Git channel, routes, Relays, SSH forwards or the node roles work, or which design constraints a change must respect.
---

# RCynQ architecture

## Overview

RCynQ (Remote Code sYNchronization, Quick) is a GPL-3.0-or-later .NET 10 / Avalonia 12 application that delivers uncommitted code to a
remote machine and brings its Git branches back without commits or pushes. One program, three roles chosen in the
configuration: **Workstation** (you edit here), **Origin** (the private repositories), **Relay** (both ends reach it; it
forwards SSH and sees no content). A chain is `Workstation → Relay(s) → Origin`, or direct.
Full design: `Docs/En/Architecture.md`; setup from scratch: `Docs/En/SetupGuide.md`.

## Module map (`Sources/`)

| Folder | Owns |
|---|---|
| `Core/RCynQ.Core` | contracts, `SyncBridgeOptions` (`syncbridge.json`), `ConfigLocations` (portable `rcynq.portable`), API records (`Api/`), events, `Enums/` |
| `Core/RCynQ.Localization` | language packs (`Languages/en.json`, `ru.json`), `ILocalizer`, `Localizer` |
| `Core/RCynQ.Core.Data*` | EF Core (`RCynQDbContext`) + FluentMigrator SQL migrations (SQLite) |
| `Infrastructure/RCynQ.Git` | Git commands, bundles, manifests, patch building |
| `Infrastructure/RCynQ.Ssh` | ssh commands, local forwards, `authorized_keys` restrictions, host key pinning |
| `Transports/*` | links to neighbours (`Direct`, `AmneziaWG`) and their state |
| `Sync/RCynQ.Sync` | patches, sync base, Git channel, repositories catalog, routes (`Routing/`), diagnostics and auto-fixes |
| `Apps/RCynQ.Service` | local HTTP API (loopback, token), SignalR events, role pipelines (`WorkstationPipeline`, `OriginPipeline`, `RelayPipeline`) |
| `Apps/RCynQ.UI` | Avalonia MVVM app; starts and stops the service |
| `Apps/RCynQ.Cli` | `rcynq` command line (CliFx) over the service API |
| `Apps/RCynQ.Pages` | static GitHub Pages site (no build step) |

Dependencies point inwards; Core depends on nothing app-level.

## Ideas a change must keep

- **The Origin repository is only read:** no remote, ref, setting, commit or index refresh (`GIT_OPTIONAL_LOCKS=0`);
  an end-to-end test compares every file under `.git` before and after. Git housekeeping happens only in the
  Workstation working copy (remote `rcynq`, `refs/remotes/rcynq/*`, fast-forward on Pull).
- **Two channels, one connection:** Git channel (Origin → Workstation, incremental bundles, never a push) and patch
  channel (both ways: signed manifest → files by SHA-256 → all-or-nothing commit; conflicts: keep, take with backup, or
  three-way merge).
- **Trust nothing from the network or disk:** ECDSA P-256 signature over the manifest (repository name included),
  replay protection, canonical paths inside the working tree, a patch for another repository is refused, a neighbour
  learns repository names only, never paths.
- **Relays are passive:** keys in `authorized_keys` may only forward one port and run no command; the endpoint lists
  the Relays and passes them as SSH jump hosts.
- **Routes:** several addresses of one node; checked every 15 s, lowest priority that answers wins, a pinned route
  wins always. A link with one address has no routes and is never touched.
- **Portable data:** `config/` and `data/` next to the program when `rcynq.portable` is there; otherwise the profile.

The general rules (time, schema, texts, dependencies, git) are in `AGENTS.md`.

## Common mistakes

- Writing anything into the Origin repository "just to make Git happy".
- Trusting a path, a repository name or a peer id from a request without validation.
- An SSH behaviour assumed from documentation and never tried against a real OpenSSH server (Windows
  `System32\OpenSSH\ssh.exe` and Git's `usr\bin\ssh.exe` both matter).
- Naming nodes Source / Mediator / Dest: the roles are Workstation / Relay / Origin (old names survive only in node ids
  and in the converter of old configurations).
