# Architektur von RCynQ

[English](../En/Architecture.md) · **Deutsch** · [Español](../Es/Architecture.md) · [Français](../Fr/Architecture.md) · [Português](../Pt/Architecture.md) · [Русский](../Ru/Architecture.md)

## Knoten

Ein Programm erfüllt drei Rollen; die Rolle wird in der Konfiguration gewählt.

| Rolle | Was sie ist | Was sie tut |
|---|---|---|
| **Workstation** | Der Rechner, auf dem Sie arbeiten | Hält Arbeitskopien, holt Branches, sendet und empfängt Patches |
| **Origin** | Der Rechner mit den privaten Repositories | Bietet seine Repositories an, empfängt und sendet Patches |
| **Relay** | Ein Rechner, den beide Enden erreichen | Leitet SSH-Verbindungen weiter. Hat kein Repository, sieht keinen Inhalt |

Eine Kette ist `Workstation → Relay(s) → Origin`. Ohne Relay verbinden sich die beiden Enden direkt.

## Regel: das Repository auf Origin wird nur gelesen

- RCynQ schreibt nichts in ein Git-Repository auf Origin: kein Remote, keine Einstellung, keine Referenz, keinen Commit, keine Index-Aktualisierung (Git läuft mit `GIT_OPTIONAL_LOCKS=0`).
- Arbeitsdateien auf Origin ändern sich nur, wenn ein vom Benutzer gesendeter Patch angewendet wird.
- Alles, was Origin speichert (die Liste der Repositories, die Synchronisationsbasis, den Verlauf, Sicherungen), liegt in `syncbridge.json` und im Datenordner des Knotens.
- Git-Verwaltung (das Repository in einem leeren Ordner anlegen, das Remote `rcynq`, `refs/remotes/rcynq/*`, Fast-Forward bei Pull) findet nur in der Arbeitskopie auf der Workstation statt.

Ein End-to-End-Test vergleicht jede Datei unter `.git` vor und nach dem Registrieren, Wechseln, Bereitstellen und Untersuchen eines Repositorys.

## Kanäle

Beide Kanäle verwenden dieselbe Verbindung und dieselbe Berechtigung.

**Git-Kanal (Origin → Workstation).** Die Workstation nennt ein Repository und die Commits, die sie bereits hat. Origin antwortet mit einem inkrementellen Git-Bundle. Die Workstation holt es nach `refs/remotes/rcynq/*` und macht einen Fast-Forward. Es wird nie etwas gepusht. Danach listet Origin alle seine Branches auf, und `refs/remotes/rcynq/*` wird an diese Liste angeglichen: Ein Bundle trägt nur Branches mit neuen Commits, daher kommen ein Branch, der an einem Commit angelegt wurde, den die Workstation schon hat, ein zurückgesetzter und ein gelöschter Branch auf diesem Weg an. Ein Pull lässt den aktuellen Branch `rcynq/<Branch>` folgen, damit Git ihn mit Origin vergleicht und nicht mit einem Remote, das dieser Computer nicht erreicht.

**Patch-Kanal (beide Richtungen).** Ein Patch ist der Unterschied zwischen dem Arbeitsverzeichnis und der *Synchronisationsbasis* — dem, was der andere Knoten bekanntlich hat. Die Synchronisationsbasis wird automatisch aus dem Git-Commit gebildet, den beide Knoten teilen, plus den bereits ausgetauschten Dateien. Zustellung: ein signiertes Manifest, dann jede Datei nach ihrem SHA-256, dann ein Commit, der alles oder nichts anwendet. Ein Konflikt wird gelöst, indem man die lokale Datei behält, die eingehende übernimmt (mit Sicherung) oder einen Drei-Wege-Merge mit Git ausführt.

## Wie ein Knoten Verbindungen annimmt

| Modus | Weg | Die Gegenstelle wird geprüft durch |
|---|---|---|
| SSH-Server | `ssh -L` zum Loopback-Port des Knotens, über die Relays als Sprungrechner | Festgelegten SSH-Hostschlüssel |
| Eigener TLS-Port (Standard `18444`) | Direkt zum Port oder über eine SSH-Weiterleitung, die auf dem letzten Relay endet | Festgelegten Fingerabdruck des Zertifikats |

Ein Relay verwendet immer seinen SSH-Server. Schlüssel in `authorized_keys` sind auf eine Portweiterleitung beschränkt und können keine Befehle ausführen.

Die lokale API eines Knotens lauscht nur auf Loopback (Standard `18443`) und verlangt ein Token, das im Datenordner liegt.

## Routen zu einem Knoten

Ein Knoten kann unter mehreren Adressen erreichbar sein: zum Beispiel über einen Tunnel über UDP und über einen zweiten Tunnel über TCP. Jede solche Adresse ist eine *Route* der Verbindung. Schlüssel und Fingerabdrücke des Knotens sind für alle Routen gleich; nur die Adresse unterscheidet sich.

- **Speicherung.** Routen werden in der Verbindung gehalten. Die Adresse des Knotens in der Verbindung ist immer die Adresse der verwendeten Route, sodass Patches, der Git-Kanal, der Ping und die Diagnose die gewählte Route nehmen, ohne von Routen zu wissen.
- **Prüfung.** Alle 15 Sekunden und sofort nach einem fehlgeschlagenen Ping prüft der Knoten jede Route. Ein Nachbar wird mit einer TCP-Verbindung geprüft. Ein Knoten hinter Relays wird über eine SSH-Weiterleitung geprüft, die an der Adresse der Route endet: das Banner eines SSH-Servers oder ein TLS-Handshake zählt als Antwort.
- **Auswahl.** Die antwortende Route mit der niedrigsten Priorität gewinnt; unter gleichen die schnellste, wenn sie deutlich schneller als die verwendete ist. Antwortet eine bessere Route wieder, kehrt der Knoten zu ihr zurück. Solange keine antwortet, bleibt der Knoten, wo er ist.
- **Auswahl von Hand.** Eine festgelegte Route wird verwendet, ob sie antwortet oder nicht, bis die Auswahl dem Knoten zurückgegeben wird.

Eine Verbindung mit einer Adresse hat keine Routen: sie wird weder geprüft noch geändert, und der Knoten arbeitet wie bisher.

Der Weg über mehrere Relays wird vom Endpunkt festgelegt: er listet sie in Reihenfolge auf und übergibt sie selbst an SSH. Ein Relay bleibt passiv; es lässt die Schlüssel der Endpunkte nur zu den Knoten der Gegenrolle und zu den SSH-Servern der benachbarten Relays weiterleiten, unter jeder Adresse, unter der es sie kennt.

## Einladungen

Ein Knoten lädt einen Nachbarn mit einem Einmal-Ticket ein: ein zufälliges Geheimnis, von dem der Knoten nur den SHA-256 behält, eine Rolle und ein Ablaufdatum. Die Einladung enthält die Karte des Knotens, die Karten der Relays auf dem Weg, das Ticket, das Verbindungstoken für das Gegenstück und das Ticket des Relays, wenn der Knoten eines hat.

- **Über den SSH-Server.** Zum Ticket gehört ein Einmal-Ed25519-Schlüssel. Seine Zeile in `authorized_keys` lautet `restrict,expiry-time=…,command="rcynq enroll --config …"`: er führt nur den Beitritt aus, und der Server lehnt ihn nach dem Ablauf ab. Der beitretende Knoten meldet sich damit an, über die Relays als Sprunghosts, und sendet seine Karte als Befehl; `rcynq enroll` übergibt sie dem Dienst auf Loopback. Die Zeile wird entfernt, sobald das Ticket benutzt, widerrufen oder abgelaufen ist.
- **Über den eigenen TLS-Port.** Der Beitritt geht an `/peer/enroll` dieses Ports; der Zertifikat-Fingerabdruck der Einladung wird festgelegt.

Der einladende Knoten prüft das Ticket, verbraucht es in einem Schritt, importiert die Karte und lässt den Schlüssel zu. Ein Relay antwortet einem Endpunkt mit einem Ticket für das Gegenüber, das der Endpunkt in seiner eigenen Einladung weitergibt. Hostschlüssel oder Zertifikat jedes Knotens werden aus der Einladung festgelegt, bevor etwas gesendet wird. Automatisch geht der Beitritt über höchstens ein Relay; ein längerer Weg braucht Dateien.

## Sicherheit des Austauschs

| Schutz | Mechanismus |
|---|---|
| Wer senden darf | Gemeinsames Verbindungstoken und eine bekannte Kennung der Gegenstelle in jeder Anfrage |
| Integrität und Herkunft | ECDSA-P-256-Signatur über das Manifest, einschließlich des Repository-Namens |
| Wiederholung | Patch-Kennung und Alter werden geprüft; ein Patch wird einmal angenommen |
| Pfadausbruch | Pfade in einem Patch sind kanonisch und müssen im Arbeitsverzeichnis bleiben |
| Falsches Repository | Ein Patch für ein Repository, mit dem der Empfänger nicht arbeitet, wird abgelehnt |
| Offenlegung von Origin | Ein Nachbar erfährt nur Repository-Namen, nie Pfade; ein Bundle wird nur für einen registrierten Namen bereitgestellt |
| Beitritt | Einmal-Ticket mit Ablauf (gespeichert wird der Hash); Einmal-SSH-Schlüssel nur für den Beitrittsbefehl, mit `expiry-time`; Hostschlüssel und Zertifikat aus der Einladung festgelegt |

## Repositories

- Origin hält einen Katalog: Name → Ordner. Ein Name besteht aus 1–64 Zeichen aus `A–Z a–z 0–9 . _ -`.
- Die Workstation hält Name → lokaler Ordner und darf nur Namen hinzufügen, die Origin anbietet.
- Jeder Knoten arbeitet jeweils mit einem Repository. Ein Wechsel braucht keinen Neustart: er wartet auf das laufende Senden, Empfangen oder Pull und ändert dann das Arbeitsverzeichnis für alles auf einmal.
- Jeder Ping trägt in beide Richtungen den Namen des Repositorys, mit dem der Knoten arbeitet. Ein Knoten, der erfährt, dass der andere gewechselt hat, sagt es dem Benutzer und folgt nicht von selbst.
- Der Knoten hält eine Synchronisationsbasis je Repository. Der Patch-Verlauf wird für das aktuelle Repository angezeigt.

## Speicherung

```
<Programmordner>/
  config/syncbridge.json     Einstellungen des Knotens, Verbindungen, Repositories
  data/
    rcynq.db                 SQLite: Gegenstellen, Verbindungen, Patches, Verlauf, verschlüsselte Geheimnisse (Signaturschlüssel)
    keys/                    SSH-Schlüssel, TLS-Zertifikat, API-Token, known_hosts
    patches/  backups/       Inhalt der Patches und durch einen Patch ersetzte Dateien
    snapshots/               Synchronisationsbasis (ein Ordner je weiterem Repository)
    logs/
```

Eine von einer früheren Version geschriebene Konfigurationsdatei wird beim Start umgewandelt; das Original bleibt als `syncbridge.json.bak` erhalten.

## Projekte

| Projekt | Zweck |
|---|---|
| `RCynQ.Core` | Verträge, Konfiguration, API-Datensätze |
| `RCynQ.Localization` | Texte, eine JSON-Datei je Sprache |
| `RCynQ.Core.Data`, `…Migrations`, `…Migrations.SQLite` | SQLite-Speicher und Schemamigrationen |
| `RCynQ.Git` | Git-Befehle, Manifeste, Erstellung von Patches |
| `RCynQ.Ssh` | SSH-Befehle, Portweiterleitungen, Umgang mit Schlüsseln und Hostschlüsseln |
| `RCynQ.Transports.*` | Verbindungen zu Nachbarn und ihr Zustand |
| `RCynQ.Sync` | Patches, Synchronisationsbasis, Git-Kanal, Repositories, Diagnose |
| `RCynQ.Service` | Lokale HTTP-API und SignalR-Ereignisse |
| `RCynQ.UI` | Desktop-Anwendung (Avalonia); startet den Dienst |
| `RCynQ.Cli` | Befehlszeile `rcynq` |
| `RCynQ.Dbs.SQLite.DbMigrator` | Konsolen-Migrator der Datenbank |
| `RCynQ.Pages` | Statische Projektseite für GitHub Pages (ohne Build-Schritt) |

Die Tests liegen in `Tests/<Projekt>.Tests`; `RCynQ.Conventions.Tests` prüft die Code-Regeln aus `AGENTS.md`. Wie man
baut, testet und verpackt: [Entwicklung](Development.md).
