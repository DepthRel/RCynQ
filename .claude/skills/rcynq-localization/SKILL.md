---
name: rcynq-localization
description: Use when adding or changing any text an RCynQ user or site visitor reads, adding a language, or editing documentation in Docs/, package notes, the installer or the project page.
---

# RCynQ localization and documentation

## Overview

The rules are in `AGENTS.md` (Localization); it is already in context. This skill is the map of every surface a
language touches and of what guards it. The languages are English (default), German, Spanish, French, Portuguese
(European, Portugal flag) and Russian; the first start picks the pack by the two-letter code of the OS UI culture.

## Where texts live

| Surface | Files | Guard |
|---|---|---|
| App, service, CLI | `Sources/Core/RCynQ.Localization/Languages/<code>.json` | `RCynQ.Localization.Tests`: same keys, same `{n}` placeholders, keys used in code and screens exist, no Cyrillic in code, nodes named only by roles (the earlier role names and their translations are forbidden in every language) |
| Flags | `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` via `Scripts/Assets/generate-flags.ps1` | — |
| Project page | `Sources/Apps/RCynQ.Pages/assets/languages.js` (`list`, `docs`), `assets/flags/<code>.svg`, the table in `assets/site.js` | keys must equal the English `data-t` keys of `index.html` |
| Installer | `[Languages]`, `[Messages]`, `[CustomMessages]` in `Scripts/App/rcynq.iss` (UTF-8 with BOM); the license page introduction `Scripts/Shared/License/intro.<code>.txt` | same message count per language |
| Package READMEs | `Scripts/App/Notes/README.<code>.txt`, tags `[win] [linux] [macos] [unix]` | — |
| `.deb` menu entry and AppStream data | `Comment[<code>]`, `summary xml:lang="<code>"` in `Scripts/App/build-deb.sh` and `publish-common.sh` | — |
| Docs | `Docs/<En|De|Es|Fr|Pt|Ru>/` same files (`SetupGuide`, `Architecture`, `Development`), the language line under every title, index `Docs/README.md` | manual |

After changing texts: `dotnet test Tests/RCynQ.Localization.Tests`.

## Common mistakes

- A key added to `en.json` and `ru.json` only: the parity test fails for the other four packs.
- A translation that uses "source", "Quelle", "fuente", "fonte", "destino", "destination", "cible", "Ziel" or a word for
  "mediator": `RoleWordingTests` fails; say "code", "the other node", "the receiving node" instead.
- A user-facing string formatted in code (`$"Link {name} is down"`): put it in the packs with a placeholder and take it
  with `ILocalizer.Format`.
- Old role names (Source, Mediator, Dest) in a new text: `Keys_DoNotCarryEarlierRoleNames` fails.
- Tests compare with `TestTexts.English` / `TestTexts.Russian` texts taken by key, not with literals.
- Inline JS/CSS on the site: the Content-Security-Policy forbids it; everything is a file in `assets/`.
- A changed meaning written into `Docs/En` only: every other `Docs/<Code>` must say the same.
