---
name: rcynq-code-style
description: Use when writing, editing or reviewing any C# (or XAML) code in the RCynQ repository, including tests, before claiming the code is done.
---

# RCynQ code style

## Overview

The rules themselves are in `AGENTS.md` (General rules, Code style); it is already in context, so read them there.
This skill adds what `AGENTS.md` does not say: how to check, and where the rules are easy to break. Code that breaks
them is not finished, even if it builds and tests pass.

## Checking

```bash
dotnet build RCynQ.slnx                                  # CS1591, nullable, IDE0005/0011/0161/1006 are errors
dotnet test Tests/RCynQ.Conventions.Tests                # everything else AGENTS.md says, file:line in the failure
```

A failing convention test lists every offending place as `path:line`; fix them, do not loosen the test.
`.editorconfig` deliberately leaves out `dotnet_sort_system_directives_first`: with that key, `dotnet format` and the
IDE's "Sort usings" sort alphabetically and put `RCynQ.*` before `Microsoft.*`. Remove unused usings with the IDE or
`dotnet format style RCynQ.slnx --diagnostics IDE0005`, but order the blocks by hand.

## Common mistakes

- A small helper record/enum added at the bottom of an existing file (a DTO next to its request, an option record next
  to a view model): give it its own file.
- A new enum put next to the view model or service that uses it (`Services/Hosting/ServiceStartOutcome.cs`): it goes
  in `Enums` of the project (`RCynQ.UI/Enums`), or in `RCynQ.Core/Enums` when two projects need it.
- An enum made `internal` at any cost. When a `public` type exposes it, it stays `public`.
- XAML that cannot find a moved enum: add a prefix for the folder, `xmlns:enums="using:RCynQ.UI.Enums"`.
- `RCynQ.*` left before `Microsoft.*`/`NSubstitute`/`Avalonia.*`/`CliFx.*` because that is alphabetical: libraries
  come before the solution.
- `&&` / `||` at the start of a continuation line out of habit: it ends the previous line.
- `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `DateTime.Now` or `Stopwatch` in product code: take a `TimeProvider`
  (optional constructor parameter, `?? TimeProvider.System`); measure with `GetTimestamp()` / `GetElapsedTime()`.
- A constant in PascalCase out of habit: every `const`, local ones too, is `UPPER_SNAKE_CASE`
  (`ApiRoutes.PEER_PING`); a `private static readonly` field is not a constant and is `_camelCase`.
- A renamed constant or field still named in XAML (`{x:Static ...}`) or in a reflection string (`GetField("_x")`): IDE
  renames do not reach them; search for the old name.
- `[GeneratedRegex]` partial methods and `partial void On…Changed` hooks of CommunityToolkit need a `///` comment too.
- `/// <inheritdoc />` is fine for overrides and interface members; everything else needs real text, tests included.
- Developer-only exception and log messages stay in English; anything a user reads goes through `ILocalizer`.
