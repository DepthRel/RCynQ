# Entwicklung von RCynQ

[English](../En/Development.md) · **Deutsch** · [Español](../Es/Development.md) · [Français](../Fr/Development.md) · [Português](../Pt/Development.md) · [Русский](../Ru/Development.md)

## Umgebung

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git und ein OpenSSH-Client (`ssh`, `ssh-keygen`, `ssh-keyscan`): das Produkt ruft sie auf, die Integrationstests
  ebenso.
- VS Code mit C# Dev Kit und der Avalonia-Erweiterung oder Visual Studio / Rider.
- Windows PowerShell 5.1 oder PowerShell 7 für `Scripts/*/*.ps1`, bash für `Scripts/*/*.sh`. Die Skripte sind danach
  gruppiert, was sie bauen: `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (siehe [Scripts/README.md](../../Scripts/README.md)).

## Build und Tests

```bash
dotnet build RCynQ.slnx                                   # 0 Warnungen erwartet
dotnet test RCynQ.slnx                                    # alle Tests
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # ein Projekt
```

Die Tests sind xUnit 2 auf dem VSTest-Runner. Die gemeinsamen Testpakete (xUnit, NSubstitute, FluentAssertions) sind
in `Directory.Build.props` für jedes `*.Tests`-Projekt eingebunden, daher listet eine Testprojektdatei nur
Projektverweise.

| Projekt | Was es prüft |
|---|---|
| RCynQ.Conventions.Tests | die Regeln aus AGENTS.md, die Analyzer nicht prüfen können, an den mit Roslyn geparsten Quellen: ein Typ je Datei, private verschachtelte Typen, XML-Kommentare, Enums in `Enums`-Ordnern, logische Operatoren am Zeilenende, die Reihenfolge der usings, Zeit nur über `TimeProvider`, das Schema nur über Migrationen, Projekte in ihren Projektmappenordnern |
| RCynQ.Core.Tests | Knotenrollen, Datensätze, Speicherorte der Konfiguration (portable Installation), der Installationsstempel, Prüfung und Umwandlung früherer Konfigurationsdateien, Repositories in der Konfiguration |
| RCynQ.Localization.Tests | Sprachpakete: Lesen, der Katalog, Wahl einer Sprache nach Kultur, der Rückfall auf Englisch, dieselben Schlüssel und Platzhalter in jeder Sprache, jeder angeforderte Schlüssel existiert (Code und Bildschirme), kein russischer Text im Code, Knoten nur nach ihren Rollen benannt |
| RCynQ.Core.Data.Tests | Migrationen, Repositories, Speicher, Dateirechte des Datenordners |
| RCynQ.Git.Tests | Git-Befehle, Bundles, Erstellung von Patches, Hashes, `.gitignore`, der Export eines Repositorys, das Remote der Arbeitskopie, Integration mit einem echten Git |
| RCynQ.Ssh.Tests | SSH-Befehle, Portweiterleitungen, Einschränkungen in `authorized_keys`, Festlegen von Hostschlüsseln, `known_hosts`, die Konfigurationsdatei des Clients |
| RCynQ.Transports.Tests | Transporte und der Verbindungsmanager, die Prüfung eines Endpunkts |
| RCynQ.Sync.Tests | Patches (Signatur, Wiederholung, Zustellung, Anwenden ganz oder gar nicht, Konflikte und Git-Merge), die Synchronisationsbasis, der Git-Kanal, Repositories, Routen und ihre Auswahl, Diagnose, Übertragung der Konfiguration, Aufbewahrung der Daten |
| RCynQ.Service.Tests | die lokale HTTP-API, ihr Token und öffentliche Endpunkte, die Startreihenfolge, Pull, Senden und Ping zwischen zwei Diensten von Ende zu Ende, Repositories von Ende zu Ende |
| RCynQ.Cli.Tests | die Befehle von `rcynq` und ihre Ausgabe |
| RCynQ.UI.Tests | View-Models, Dialoge, das Fenster „Über RCynQ“ und der Problembrief, der Controller des Dienstprozesses, der API-Client, die Speicher für Sprache und Einstellungen |
| RCynQ.UI.Headless.Tests | das Hauptfenster und das Fenster „Über RCynQ“ in Avalonia Headless: jeder Text ändert sich mit der Sprache sofort |

Ein Test nennt die Sprache, in der seine Erwartungen geschrieben sind: `TestTexts.English` oder `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`) wird an den geprüften Code übergeben. Nichts im Prozess wählt hinter seinem Rücken eine
Sprache, daher stören sich parallele Tests nicht.

## Lokalisierung

- Alles, was ein Benutzer liest (Beschriftungen, Fehler- und Ausnahmemeldungen, Ereignisse, Diagnose, Ausgabe der
  CLI), liegt in `Sources/Core/RCynQ.Localization/Languages/<code>.json` und wird per Schlüssel geholt:
  `ILocalizer.Get("Key")` / `ILocalizer.Format("Key", …)` im Code, `{loc:Loc Key}` in XAML. Es gibt keine Zeichenketten
  in menschlicher Sprache in Code oder Markup und keine Verzweigungen „wenn die Sprache … ist“.
- Die Sprachen sind Englisch (Standard), Deutsch, Spanisch, Französisch, Portugiesisch (europäisch) und Russisch. Beim
  ersten Start wird die Sprache des Betriebssystems genommen, wenn es ein Paket dafür gibt, sonst Englisch. Ein
  Schlüssel kommt in alle Sprachdateien zugleich; eine Übersetzung, der ein Schlüssel oder Platzhalter fehlt, lässt
  `RCynQ.Localization.Tests` fehlschlagen.
- Knoten werden nur nach ihren Rollen benannt: Workstation, Relay, Origin — in jeder Sprache dieselben englischen
  Wörter. Die Wörter früherer Versionen (Source, Mediator, Dest) und ihre Übersetzungen werden in Texten nicht
  verwendet (`RoleWordingTests`).

### Eine Sprache hinzufügen

1. Kopieren Sie `Languages/en.json` nach `<code>.json` (ein zweibuchstabiger Code nach ISO 639-1), tragen Sie den
   Namen der Sprache ein und übersetzen Sie die Werte. Schlüssel und die Platzhalter `{0}`, `{1}` ändern sich nicht.
2. Zeichnen Sie ihre Flagge nach `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` mit `Scripts/Assets/generate-flags.ps1`.
3. Übersetzen Sie, was außerhalb der Anwendung lebt: den Installationsassistenten (`[Languages]`, `[Messages]` und
   `[CustomMessages]` in `Scripts/App/rcynq.iss`, die Einleitung seiner Lizenzseite
   `Scripts/Shared/License/intro.<code>.txt`), die Menüeinträge der Linux-Pakete (`Comment[<code>]` in
   `Scripts/App/build-deb.sh` und `publish-common.sh`), die Paketnotizen (`Scripts/App/Notes/README.<code>.txt`), die
   Projektseite (ein weiterer Eintrag in `assets/languages.js`, eine Flagge in `assets/flags/<code>.svg` und eine
   Tabelle in `assets/site.js` von `Sources/Apps/RCynQ.Pages`) und die Dokumentation (unten).

## Dokumentation

Die Dokumente werden nach Sprache gehalten: `Docs/En` ist der Hauptsatz; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` und
`Docs/Ru` enthalten denselben Satz auf Deutsch, Spanisch, Französisch, Portugiesisch und Russisch. Die Zeile unter dem
Titel jedes Dokuments verweist auf dasselbe Dokument in den anderen Sprachen.

- die Menge der Dateien und ihre Namen sind in jeder Sprache gleich; eine Änderung der Bedeutung kommt in alle Sprachen
  zugleich;
- Verweise von außen (das README) führen nach `Docs/En`; die Projektseite öffnet die Dokumente in der dort gewählten
  Sprache;
- eine neue Sprache ist ein neuer Ordner `Docs/<Code>` mit demselben Satz plus eine weitere Zeile in der Tabelle
  `docs` von `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Start aus VS Code

`.vscode/launch.json`:

| Profil | Was es tut |
|---|---|
| Service | baut und startet `RCynQ.Service` mit `.vscode/dev/syncbridge.json` |
| UI | baut und startet `RCynQ.UI` mit derselben Konfiguration; die UI startet den Dienst selbst |
| CLI | baut und startet `rcynq` mit Argumenten aus einer Abfrage (`RCYNQ_CONFIG` zeigt auf dieselbe Konfiguration) |
| DbMigrator | baut und startet den Migrator mit Argumenten aus einer Abfrage |
| Service + UI | beide, um die zwei Prozesse gleichzeitig zu debuggen |

Die Build-Tasks (`.vscode/tasks.json`) rufen `dotnet build <csproj>` auf: gebaut wird nur das gewählte Projekt und das,
wovon es abhängt. Ein vollständiger Build der Projektmappe ist ein eigener Task, `build: solution`.

Umgebungsvariablen:

| Variable | Zweck |
|---|---|
| `RCYNQ_CONFIG` | die Konfigurationsdatei anstelle der portablen `config/syncbridge.json` |
| `RCYNQ_SERVICE_EXE` | die ausführbare Datei des Dienstes, den die UI startet (auch `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | wie die CLI einen laufenden Dienst erreicht |
| `RCYNQ_*` | jede Einstellung von `syncbridge.json` für den Dienst (`RCYNQ_SyncBridge__ListenPort`, …) |

## Datenbank

SQLite. Das Schema wird nur durch FluentMigrator-Migrationen erstellt; EF Core bildet das Modell nur auf die Tabellen
ab. `EnsureCreated` und EF-Core-Migrationen werden nicht verwendet. Um das Schema zu ändern:

1. Fügen Sie `Scripts/000N_Name.Up.sql` und `000N_Name.Down.sql` zu `RCynQ.Core.Data.Migrations.SQLite` hinzu.
2. Fügen Sie eine Klasse `Versions/M000N_Name.cs` mit `[Migration(N, "...")]` hinzu.
3. Aktualisieren Sie die Entitäten, die Abbildung und `RCynQDbContext`.

Der Dienst wendet die Migrationen beim Start an. Von Hand:

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## Lizenz

RCynQ ist freie Software unter der GNU General Public License, Version 3 oder später (`LICENSE.txt`).

- Nur Abhängigkeiten, die mit GPL-3.0-or-later vereinbar sind. Eine Abhängigkeit, die in ein Paket gelangt, wird mit
  ihrem Lizenztext in derselben Änderung in `THIRD-PARTY-NOTICES.txt` eingetragen.
- Jedes Paket enthält `LICENSE.txt` und `THIRD-PARTY-NOTICES.txt` neben dem Programm (`Write-PackageLicenses` und
  `write_package_licenses` in `Scripts/Shared/package-notes.*`); das `.deb` fügt `/usr/share/doc/rcynq/copyright` hinzu.
- Der Installationsassistent zeigt die Lizenzseite in jeder Sprache: die Einleitung aus
  `Scripts/Shared/License/intro.<code>.txt`, gefolgt vom englischen Text der GPL, dem einzig rechtlich verbindlichen.
- Das Fenster „Über RCynQ“ in den Einstellungen nennt die Lizenz und öffnet den Lizenztext und die Liste der
  Komponenten.

## Code-Regeln

Siehe [AGENTS.md](../../AGENTS.md): ein Typ je Datei, ein XML-Kommentar an jedem Member (CS1591 ist ein Fehler),
geschweifte Klammern um jeden Block, Leerzeilen zwischen den logischen Schritten einer Methode, logische Operatoren am
Zeilenende, Konstanten in `UPPER_SNAKE_CASE`, Aufzählungen in `Enums`-Ordnern, `using`-Direktiven in den Blöcken
System, Bibliotheken, RCynQ, Zeit über `TimeProvider`, nicht vertrauenswürdige Eingaben werden immer geprüft. Der Build
(Analyzer der `.editorconfig`) und `RCynQ.Conventions.Tests` prüfen sie.

## Verpacken

| Skript | Ergebnis |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` und `RCynQ-<version>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | dasselbe Archiv plus `RCynQ-<version>-setup.exe` (Inno Setup 6 oder neuer) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<version>-<runtime>.tar.gz` und, wo `dpkg-deb` vorhanden ist (Debian, Ubuntu, WSL), `RCynQ-<version>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<version>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | dieselben Pakete für Linux und macOS, unter Windows in WSL gebaut |
| `Scripts/build-release.ps1 [-Skip <Schritte>] [-ContinueOnError]` | jedes Paket eines Releases nacheinander und `SHA256SUMS-<version>.txt` |

Der Build ist eigenständig; der Zielrechner braucht kein .NET, nur Git und einen SSH-Client. Ein Paket enthält den
Dienst, die CLI, die UI und den Datenbank-Migrator in `service/`, `cli/`, `ui/` und `dbmigrator/`. Im Stamm des
Windows-Pakets liegt `RCynQ.exe` mit dem Anwendungssymbol: derselbe .NET-Host wie die UI (das Ziel `RCynQRootLauncher`
in `RCynQ.UI.csproj`), nur mit dem relativen Pfad `ui\RCynQ.UI.dll`.

Die Archive und der Windows-Installer sind portabel: die Markierung `rcynq.portable` im Stamm hält die Konfiguration in
`config/` und alle Daten in `data/` neben dem Programm. Das `.deb`-Paket installiert ohne die Markierung nach
`/opt/rcynq`, daher hält jeder Benutzer die Konfiguration in `~/.config/RCynQ` und die Daten in
`~/.local/share/RCynQ`.

Die Pakete für Linux und macOS werden unter Linux gebaut (WSL zählt auch): nur dort bleiben die Ausführungsrechte im
Archiv erhalten. Das so gebaute macOS-Paket ist nicht signiert; siehe die Notizen darin.

## GitHub-Workflows

| Workflow | Wann | Was |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | jeder Push nach `main`, jeder Pull Request, von Hand | baut die Projektmappe und führt jeden Test unter Windows und Linux aus; mit den Stil-Analyzern und den Konventionstests macht eine gebrochene Regel den Lauf rot |
| **Packages** (`.github/workflows/packages.yml`) | von Hand oder ein Versions-Tag `X.Y.Z` gleich `<Version>` aus `Directory.Build.props` | baut den Windows-Installer und das `.zip`, das Linux-`.deb` und `.tar.gz`, die macOS-Archive (unter macOS, mit Ad-hoc-Signatur); ein Tag hängt sie mit `SHA256SUMS-<version>.txt` an einen **Entwurf** eines Releases |
| **Pages** (`.github/workflows/pages.yml`) | eine Änderung von `Sources/Apps/RCynQ.Pages` in `main` | veröffentlicht die Projektseite |

Ein Release: `<Version>` erhöhen, den Tag pushen (`git tag 0.2.0; git push origin 0.2.0`), die Notizen des
Release-Entwurfs schreiben, die Dateien prüfen, veröffentlichen und erst dann `updates.json` der Projektseite füllen.
