# Einrichtungsanleitung für RCynQ

[English](../En/SetupGuide.md) · **Deutsch** · [Español](../Es/SetupGuide.md) · [Français](../Fr/SetupGuide.md) · [Português](../Pt/SetupGuide.md) · [Русский](../Ru/SetupGuide.md)

Drei Rechner sind beteiligt:

| Knoten | Rechner |
|---|---|
| **Workstation** | auf dem Sie das Projekt bearbeiten |
| **Origin** | auf dem die privaten Repositories liegen |
| **Relay** | einer, den die beiden anderen erreichen |

Führen Sie die Schritte der Reihe nach aus. Wo ein Schritt Varianten hat, wählen Sie die, die zum Rechner passt.

## 1. Prüfen, wer wen erreicht

| Von | Nach | Port |
|---|---|---|
| Workstation | Relay | 22 |
| Origin | Relay | 22 |
| Relay | Origin | 22 (Variante A in Schritt 3) oder 18444 (Variante B) |
| Relay | Workstation | 22 (Variante A) oder 18444 (Variante B) |

Wie die Rechner einander erreichen (lokales Netz, ein beliebiges VPN, öffentliche Adresse), entscheiden Sie; RCynQ braucht nur Adressen, die antworten. Notieren Sie die Adresse jedes Rechners so, wie sein Nachbar sie sieht.

**Ohne Relay.** Wenn Workstation und Origin einander direkt erreichen, überspringen Sie Schritt 4 und jede Erwähnung des Relays.

## 2. Installieren

Auf jedem Rechner eines davon:

| System | Paket | Wo Einstellungen und Daten liegen |
|---|---|---|
| Windows | `RCynQ-<version>-setup.exe`: installiert für den aktuellen Benutzer nach `%LOCALAPPDATA%\Programs\RCynQ`, ohne Administratorrechte | `config` und `data` in der Installation |
| Windows | `RCynQ-<version>-win-x64.zip`: in einen beschreibbaren Ordner entpacken, zum Beispiel `C:\RCynQ`, nicht nach `Program Files` | `config` und `data` neben dem Programm |
| Debian, Ubuntu | `RCynQ-<version>-linux-x64.deb`: `sudo apt install ./RCynQ-<version>-linux-x64.deb`; Git und der SSH-Client kommen mit | `~/.config/RCynQ` und `~/.local/share/RCynQ` jedes Benutzers |
| Linux, macOS | `RCynQ-<version>-<platform>.tar.gz`: in einen beschreibbaren Ordner entpacken, zum Beispiel `~/rcynq` | `config` und `data` neben dem Programm |

Starten Sie dann RCynQ: über das Startmenü oder `RCynQ.exe` (Windows), über das Anwendungsmenü oder `rcynq-ui` (`.deb`),
`./RCynQ.sh` (Archive für Linux und macOS). Die `README.txt` des Pakets sagt dasselbe für ihr System.

Installieren Sie auf Workstation und Origin Git und prüfen Sie es: `git --version`.

Verwenden Sie für jeden Rechner ein sauberes Paket. Kopieren Sie keinen Ordner, in dem RCynQ schon gelaufen ist: seine Ordner `config` und `data` sind die Identität dieses Knotens.

## 3. Wählen, wie der Knoten Verbindungen annimmt

Das Relay verwendet immer Variante A. Für Workstation und Origin wählen Sie je Rechner.

### Variante A — SSH-Server

Am schnellsten: **Rolle und Knoten → Konfiguration exportieren → SSH-Server installieren** (oder `rcynq ssh install-server`) und die Administratorabfrage bestätigen. RCynQ installiert, startet und öffnet den Server selbst; geht das nicht, zeigt es die Befehle für die Ausführung von Hand. Die Befehle unten tun dasselbe von Hand.

**Windows** (PowerShell als Administrator):

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
```

Schlägt der erste Befehl mit `0x800f0954` fehl, installieren Sie `OpenSSH-Win64-*.msi` aus den [Win32-OpenSSH-Releases](https://github.com/PowerShell/Win32-OpenSSH/releases) mit `msiexec /i <Datei> ADDLOCAL=Server`, starten Sie den Dienst wie oben und öffnen Sie den Port:

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Linux:**

```bash
sudo apt install openssh-server      # oder: sudo dnf install openssh-server
sudo systemctl enable --now ssh      # auf Fedora und RHEL heißt der Dienst sshd
```

**macOS:** Systemeinstellungen → Allgemein → Teilen → Entfernte Anmeldung.

Prüfen: `ssh -V` gibt eine Version aus, und der Dienst läuft (`Get-Service sshd` / `systemctl status ssh`).

### Variante B — eigener TLS-Port, kein SSH-Server

Für einen Rechner, auf dem kein SSH-Server installiert werden kann. Öffnen Sie den eingehenden TCP-Port `18444`.

Am schnellsten: **Rolle und Knoten → Konfiguration exportieren → Port 18444 in der Firewall öffnen** (oder `rcynq endpoint open-port`) und die Administratorabfrage bestätigen. Von Hand:

**Windows** (PowerShell als Administrator):

```powershell
New-NetFirewallRule -Name rcynq -DisplayName 'RCynQ' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 18444
```

**Linux:** `sudo ufw allow 18444/tcp` oder `sudo firewall-cmd --permanent --add-port=18444/tcp && sudo firewall-cmd --reload`.

Der SSH-*Client* wird auf diesem Rechner trotzdem gebraucht, wenn ein Relay verwendet wird: `ssh -V`.

## Am schnellsten: Einladungen

Führen Sie die Schritte 1–3 aus und legen Sie im Einrichtungsfenster die Rolle jedes Rechners fest. Danach richten zwei Textzeilen die ganze Kette ein.

Nach der ersten Einrichtung öffnet sich der Assistent **Knoten verbinden** von selbst; später findet er sich unter **Rolle und Knoten → Knoten verbinden**. Er prüft, ob der Rechner Nachbarn annimmt, bietet die Installation des SSH-Servers an, wenn nicht, und zeichnet die Kette, während er arbeitet.

1. Auf dem Relay: im Assistenten **Weiter**, dann **Einladung erstellen** für **Origin** und kopieren.
2. Auf Origin: **Über ein Relay → Weiter → Ich habe eine Einladung**, einfügen, **Beitreten**. Danach bietet der Assistent **Knoten einladen** an: Einladung für die Workstation erstellen und kopieren.
3. Auf der Workstation: **Über ein Relay → Weiter → Ich habe eine Einladung**, einfügen, **Beitreten**.

Ohne Relay wählen Sie **Direkt**: Origin lädt ein, die Workstation tritt mit dieser Einladung bei. Jede Stufe des Beitritts wird mit ihrem Ergebnis gezeigt; danach geht es mit Schritt 9 weiter.

Muss das Relay einen Knoten mit Administratorrechten zulassen, bestätigen Sie die Abfrage auf dem Relay: Der Beitritt wartet bis zu drei Minuten darauf und läuft dann von selbst weiter.

Eine Einladung ist ein Geheimnis: wer sie hat, kann einmal beitreten, bis sie abläuft (1 Stunde, 24 Stunden oder 7 Tage). Geben Sie sie vertraulich weiter. Sie enthält das Verbindungstoken, nichts muss von Hand kopiert werden. Befehlszeile: `rcynq invite --role Workstation`, `rcynq join <Einladung>`, `rcynq invitation list`, `rcynq invitation revoke <id>`.

Die Schritte 4–8 unten sind dieselbe Einrichtung mit Dateien, falls keine Einladung verwendet werden kann.

## 4. Das Relay einrichten

1. Starten Sie RCynQ. Legen Sie im Einrichtungsfenster die Knotenkennung fest (zum Beispiel `relay-1`), die Rolle **Relay**, und speichern Sie.
2. **Rolle und Knoten → Konfiguration exportieren.** Jede Adresse des Rechners kommt in die Datei; geben Sie zusätzlich einen Hostnamen ein, wenn das Relay unter einem Namen oder hinter NAT erreicht wird. Exportieren Sie `relay.rcynq.json`. Beide Endpunkte importieren diese eine Datei, und jeder nimmt die Adresse, die ihm antwortet.

## 5. Origin einrichten

1. Starten Sie RCynQ. Legen Sie im Einrichtungsfenster fest:
   - die Knotenkennung, zum Beispiel `origin-1`;
   - die Rolle **Origin**;
   - den Ordner des Git-Repositorys (den, der `.git` enthält);
   - **Wie Nachbarn sich mit diesem Knoten verbinden** — *Über den SSH-Server dieses Rechners* (Variante A) oder *Ohne SSH-Server: eigener Port mit TLS* (Variante B).
2. Kopieren Sie das erzeugte **gemeinsame Verbindungstoken**. Die Workstation braucht dasselbe.
3. Speichern Sie.
4. **Rolle und Knoten → Konfiguration exportieren.** Der Schlüssel wird von selbst erstellt, wenn der Rechner keinen hat. Exportieren Sie `origin.rcynq.json`.

Das Repository heißt wie sein Ordner. Unter diesem Namen sieht es die Workstation.

## 6. Die Workstation einrichten

1. Starten Sie RCynQ. Legen Sie im Einrichtungsfenster fest:
   - die Knotenkennung, zum Beispiel `workstation-1`;
   - die Rolle **Workstation**;
   - einen Ordner für die Arbeitskopie. Ein leerer Ordner ist in Ordnung: der erste Pull füllt ihn;
   - das von Origin kopierte **gemeinsame Verbindungstoken**;
   - **Wie Nachbarn sich mit diesem Knoten verbinden** — Variante A oder B wie in Schritt 3.
2. Speichern Sie.
3. **Rolle und Knoten → Konfiguration exportieren.** Exportieren Sie `workstation.rcynq.json`.

## 7. Die Knotendateien austauschen

Auf jedem Rechner: **Rolle und Knoten → Konfiguration importieren**, in beliebiger Reihenfolge. Nach jedem Import führt RCynQ die Prüfung aus und sagt, ob die Verbindung funktioniert.

| Auf | Importieren |
|---|---|
| Workstation | `relay.rcynq.json` und `origin.rcynq.json` |
| Origin | `relay.rcynq.json` und `workstation.rcynq.json` |
| Relay | `workstation.rcynq.json` und `origin.rcynq.json` |

Ohne Relay: die Workstation importiert `origin.rcynq.json`, Origin importiert `workstation.rcynq.json`.

Die Dateien enthalten keine privaten Schlüssel. Geben Sie sie über einen Kanal weiter, dem Sie vertrauen.

## 8. Die Verbindungen erlauben

Nach jedem Import bietet RCynQ an, den Schlüssel des Nachbarn zu erlauben.

- **Variante A:** bestätigen. Unter Windows bestätigen Sie auch die Administratorabfrage. Der Schlüssel ist auf die Weiterleitung eines Ports beschränkt und kann keine Befehle ausführen.
- **Variante B:** nichts zu tun; es gibt keinen SSH-Server einzurichten.

Wenn Sie abgelehnt haben, klicken Sie auf **Rolle und Knoten → Knotenverbindungen erlauben**.

## 9. Prüfen und holen

1. Auf Workstation und Origin: **Diagnose → Prüfung starten**. Beheben Sie, was rot ist; jede fehlgeschlagene Prüfung sagt, wie.
2. Auf der Workstation: **Branches → Pull von Origin**. Der Ordner enthält jetzt das Projekt.
3. Bearbeiten Sie eine Datei, öffnen Sie **Synchronisation**, klicken Sie auf **Änderungen senden**. Die Datei erscheint auf Origin.

Ab jetzt: **Pull**, um neue Commits zu holen, **Änderungen senden**, um Ihre Bearbeitungen zuzustellen. Änderungen auf Origin werden von dort auf dieselbe Weise gesendet.

## 10. Weitere Repositories

1. Auf Origin: **Branches → Repository hinzufügen**, wählen Sie den Ordner eines weiteren Git-Repositorys.
2. Wählen Sie auf einem der Knoten das Repository in der Liste oben im Fenster. Der Knoten wechselt ohne Neustart.
3. Auf der Workstation fragt ein Repository, das noch nicht auf dem Rechner ist, nach einem Ordner und holt sich dann selbst.
4. Der andere Knoten zeigt einen Hinweis: **Ebenfalls wechseln** oder **Bleiben**. Änderungen werden nur ausgetauscht, solange beide Knoten mit demselben Repository arbeiten.

Befehlszeile: `rcynq repo list`, `rcynq repo add <name> <path>`, `rcynq repo use <name>`, `rcynq repo remove <name>`.

Das Entfernen eines Repositorys aus der Liste löscht nie seinen Ordner oder seine Dateien.

## 11. Ersatzrouten und mehrere Relays

Ist bereits ein Knoten dieser Rolle in der Kette, ändert ein Import nichts stillschweigend: er fragt, was die Datei ist.

| Was importiert wird | Auswahl |
|---|---|
| Derselbe Knoten unter einer anderen Adresse | **Eine weitere Route zum selben Knoten** — die Adresse kommt zu den Routen des Knotens hinzu. **Ersatz** — die neue Adresse ersetzt die verwendete |
| Ein anderes Relay | **Ein weiterer Knoten** — das Relay kommt vor dem Endpunkt in die Kette. **Ersatz** — es ersetzt das gewählte Relay |
| Ein anderer Endpunkt | Nur **Ersatz**: eine Kette endet an einem Knoten |

Ein ersetzter Knoten wird samt seinen Schlüsseln vergessen. Klicken Sie nach einem Ersatz auf **Knotenverbindungen erlauben**, damit sein Schlüssel den Zugang verliert.

**Eine Ersatzroute.** Beispiel: zwei Rechner sind durch einen AmneziaWG-Tunnel über UDP verbunden, und daneben läuft ein Tunnel über TCP.

1. Exportieren Sie auf dem Nachbarn die Konfiguration noch einmal und wählen Sie die Adresse des zweiten Tunnels.
2. Importieren Sie die Datei und wählen Sie **Eine weitere Route zum selben Knoten**.
3. Unter dem Knoten in **Knoten der Kette** erscheinen seine Routen mit Zustand und Latenz.

**Automatische Auswahl** ist standardmäßig an: die bevorzugte Route, die antwortet, wird verwendet, unter gleichen die schnellste. Antwortet eine Route nicht mehr, wechselt der Verkehr zur nächsten; antwortet die bessere wieder, kehrt der Knoten zu ihr zurück. Die Pfeile ändern die Priorität (niedriger wird bevorzugt). Die Markierung neben einer Route legt sie von Hand fest, bis Sie zur automatischen Auswahl zurückkehren.

Führt der Weg über ein Relay, importieren Sie die zweite Adresse auch dort: ein Relay leitet nur zu Adressen weiter, die es kennt.

**Eine Kette aus zwei Relays** (`Workstation → Relay 1 → Relay 2 → Origin`).

1. Importieren Sie auf Workstation und Origin beide Relays; wählen Sie für das zweite **Ein weiterer Knoten**. Die Reihenfolge ist die Reihenfolge des Imports, von diesem Knoten aus gezählt.
2. Importieren Sie auf jedem Relay das benachbarte Relay und beide Endpunkte.
3. Klicken Sie auf jedem Knoten auf **Knotenverbindungen erlauben**.

Befehlszeile: `rcynq import <file> --as route|replace|hop [--link <link>]`. Ohne `--as` ändert ein solcher Import nichts und listet die Auswahl auf.

In `syncbridge.json` stehen die Ersatzadressen einer Verbindung in `Routes` (`Host`, optional `Name`, `SshPort`, `SshUser`, `Priority`); die Priorität der Adresse der Verbindung selbst ist `RoutePriority`.

## Aktualisieren von einer früheren Version

Frühere Versionen nannten die Rollen Source, Mediator und Dest. Jetzt heißen sie Workstation, Relay und Origin.

1. Aktualisieren Sie **alle** Knoten gemeinsam: alte und neue Versionen verstehen einander nicht.
2. Starten Sie jeden Knoten. Seine Konfiguration wird selbst umgewandelt; die vorherige Datei bleibt als `syncbridge.json.bak` erhalten.
3. Klicken Sie auf Knoten mit SSH-Server einmal auf **Rolle und Knoten → Knotenverbindungen erlauben**. Das schreibt die alten Schlüsselzeilen neu, die mehr erlaubten als heute nötig.

## Wenn etwas nicht funktioniert

| Symptom | Prüfen |
|---|---|
| Eine Verbindung ist offline | Führen Sie auf diesem Knoten die **Diagnose** aus. Vom Nachbarn aus: `Test-NetConnection <Adresse> -Port 22` (Windows) oder `nc -vz <Adresse> 22`. Für Variante B Port `18444` verwenden. |
| „Das Verbindungstoken fehlt oder ist ungültig“ | Das gemeinsame Verbindungstoken unterscheidet sich zwischen Workstation und Origin. Kopieren Sie es auf der Registerkarte **Rolle und Knoten** erneut. |
| Der Schlüssel eines Knotens fehlt | Exportieren Sie seine Datei erneut mit angehaktem **Öffentlichen SSH-Schlüssel des Knotens einschließen** und importieren Sie sie. |
| „arbeitet mit dem Repository … und der Patch ist für …“ | Die Knoten arbeiten mit verschiedenen Repositories. Schalten Sie einen davon um. |
| Pull verweigert wegen lokaler Änderungen | Die aufgeführten Dateien wurden bearbeitet und nicht gesendet. Senden Sie sie zuerst oder machen Sie die Bearbeitungen rückgängig. |
| Die Adresse eines Knotens hat sich geändert | Exportieren Sie seine Datei mit der neuen Adresse, importieren Sie sie bei den Nachbarn und klicken Sie auf **Knotenverbindungen erlauben**. |

Die Protokolle stehen auf der Registerkarte **Protokolle** und in `data/logs`.
