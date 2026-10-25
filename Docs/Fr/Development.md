# Développement de RCynQ

[English](../En/Development.md) · [Deutsch](../De/Development.md) · [Español](../Es/Development.md) · **Français** · [Português](../Pt/Development.md) · [Русский](../Ru/Development.md)

## Environnement

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git et un client OpenSSH (`ssh`, `ssh-keygen`, `ssh-keyscan`) : le produit les appelle, les tests d'intégration
  aussi.
- VS Code avec C# Dev Kit et l'extension Avalonia, ou Visual Studio / Rider.
- Windows PowerShell 5.1 ou PowerShell 7 pour `Scripts/*/*.ps1`, bash pour `Scripts/*/*.sh`. Les scripts sont groupés
  selon ce qu'ils construisent : `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (voir [Scripts/README.md](../../Scripts/README.md)).

## Compilation et tests

```bash
dotnet build RCynQ.slnx                                   # 0 avertissement attendu
dotnet test RCynQ.slnx                                    # tous les tests
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # un projet
```

Les tests sont en xUnit 2 sur l'exécuteur VSTest. Les paquets de test communs (xUnit, NSubstitute, FluentAssertions)
sont référencés dans `Directory.Build.props` pour chaque projet `*.Tests` : un fichier de projet de test ne liste donc
que des références de projets.

| Projet | Ce qu'il vérifie |
|---|---|
| RCynQ.Conventions.Tests | les règles d'AGENTS.md que les analyseurs ne peuvent pas vérifier, sur les sources analysées par Roslyn : un type par fichier, types imbriqués privés, commentaires XML, énumérations dans des dossiers `Enums`, opérateurs logiques en fin de ligne, ordre des using, l'heure seulement par `TimeProvider`, le schéma seulement par des migrations, projets dans leurs dossiers de solution |
| RCynQ.Core.Tests | rôles des nœuds, enregistrements, emplacements de la configuration (installation portable), marque d'installation, validation et conversion des configurations antérieures, dépôts dans la configuration |
| RCynQ.Localization.Tests | paquets de langue : lecture, catalogue, choix de la langue selon la culture, repli sur l'anglais, mêmes clés et marqueurs dans chaque langue, existence de chaque clé demandée (code et écrans), aucun texte russe dans le code, nœuds nommés seulement par leur rôle |
| RCynQ.Core.Data.Tests | migrations, référentiels, stockage, droits des fichiers du dossier de données |
| RCynQ.Git.Tests | commandes Git, paquets, construction des patchs, hashs, `.gitignore`, export d'un dépôt, remote de la copie de travail, intégration avec un vrai Git |
| RCynQ.Ssh.Tests | commandes SSH, transferts de ports, restrictions de `authorized_keys`, fixation des clés d'hôte, `known_hosts`, fichier de configuration du client |
| RCynQ.Transports.Tests | les transports et le gestionnaire de liaisons, la vérification d'une extrémité |
| RCynQ.Sync.Tests | patchs (signature, rejeu, livraison, application tout ou rien, conflits et fusion Git), base de synchronisation, canal Git, dépôts, routes et leur choix, diagnostic, transfert de configuration, conservation des données |
| RCynQ.Service.Tests | l'API HTTP locale, son jeton et ses points publics, l'ordre de démarrage, Pull, envoi et ping de bout en bout entre deux services, dépôts de bout en bout |
| RCynQ.Cli.Tests | les commandes de `rcynq` et leur sortie |
| RCynQ.UI.Tests | modèles de vue, dialogues, la fenêtre « À propos de RCynQ » et le message de signalement d'un problème, le contrôleur du processus du service, le client de l'API, les magasins de langue et de réglages |
| RCynQ.UI.Headless.Tests | la fenêtre principale et la fenêtre « À propos de RCynQ » dans Avalonia Headless : chaque texte change à la volée avec la langue |

Un test nomme la langue dans laquelle ses attentes sont écrites : `TestTexts.English` ou `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`) est transmis au code testé. Rien dans le processus ne choisit une langue à son insu, donc
les tests en parallèle ne se gênent pas.

## Localisation

- Tout ce que lit un utilisateur (libellés, messages d'erreur et d'exception, événements, diagnostic, sortie de la CLI)
  se trouve dans `Sources/Core/RCynQ.Localization/Languages/<code>.json` et se prend par clé : `ILocalizer.Get("Key")` /
  `ILocalizer.Format("Key", …)` dans le code, `{loc:Loc Key}` en XAML. Il n'y a aucune chaîne en langue humaine dans
  le code ou le balisage, ni aucune branche « si la langue est … ».
- Les langues sont l'anglais (par défaut), l'allemand, l'espagnol, le français, le portugais (européen) et le russe.
  Au premier démarrage, la langue du système d'exploitation est prise s'il existe un paquet pour elle, sinon l'anglais.
  Une clé est ajoutée à tous les fichiers de langue à la fois ; une traduction qui a perdu une clé ou un marqueur fait
  échouer `RCynQ.Localization.Tests`.
- Les nœuds sont nommés seulement par leur rôle : Workstation, Relay, Origin, les mêmes mots anglais dans toutes les
  langues. Les mots des versions antérieures (Source, Mediator, Dest) et leurs traductions ne sont pas utilisés dans
  les textes (`RoleWordingTests`).

### Ajouter une langue

1. Copiez `Languages/en.json` vers `<code>.json` (un code ISO 639-1 de deux lettres), indiquez le nom de la langue et
   traduisez les valeurs. Les clés et les marqueurs `{0}`, `{1}` ne changent pas.
2. Dessinez son drapeau dans `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` avec `Scripts/Assets/generate-flags.ps1`.
3. Traduisez ce qui vit hors de l'application : l'assistant d'installation (`[Languages]`, `[Messages]` et
   `[CustomMessages]` de `Scripts/App/rcynq.iss`, l'introduction de sa page de licence
   `Scripts/Shared/License/intro.<code>.txt`), les entrées de menu des paquets Linux (`Comment[<code>]` dans
   `Scripts/App/build-deb.sh` et `publish-common.sh`), les notes des paquets (`Scripts/App/Notes/README.<code>.txt`), la
   page du projet (une entrée de plus dans `assets/languages.js`, un drapeau dans `assets/flags/<code>.svg` et une table
   dans `assets/site.js` de `Sources/Apps/RCynQ.Pages`) et la documentation (ci-dessous).

## Documentation

Les documents sont rangés par langue : `Docs/En` est le jeu principal ; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` et
`Docs/Ru` contiennent le même jeu en allemand, espagnol, français, portugais et russe. La ligne sous le titre de chaque
document renvoie au même document dans les autres langues.

- l'ensemble des fichiers et leurs noms sont les mêmes dans chaque langue ; un changement de sens va dans toutes les
  langues à la fois ;
- les liens venant de l'extérieur (le README) mènent à `Docs/En` ; la page du projet ouvre les documents dans la langue
  choisie sur elle ;
- une nouvelle langue est un nouveau dossier `Docs/<Code>` avec le même jeu, plus une ligne dans la table `docs` de
  `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Lancement depuis VS Code

`.vscode/launch.json` :

| Profil | Ce qu'il fait |
|---|---|
| Service | compile et lance `RCynQ.Service` avec `.vscode/dev/syncbridge.json` |
| UI | compile et lance `RCynQ.UI` avec la même configuration ; l'UI démarre le service elle-même |
| CLI | compile et lance `rcynq` avec des arguments demandés à l'utilisateur (`RCYNQ_CONFIG` pointe vers la même configuration) |
| DbMigrator | compile et lance le migrateur avec des arguments demandés à l'utilisateur |
| Service + UI | les deux, pour déboguer les deux processus à la fois |

Les tâches de compilation (`.vscode/tasks.json`) appellent `dotnet build <csproj>` : seul le projet choisi et ce dont il
dépend sont compilés. Une compilation complète de la solution est une tâche à part, `build: solution`.

Variables d'environnement :

| Variable | Rôle |
|---|---|
| `RCYNQ_CONFIG` | le fichier de configuration à la place du `config/syncbridge.json` portable |
| `RCYNQ_SERVICE_EXE` | l'exécutable du service que l'UI lance (aussi `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | comment la CLI atteint un service en cours d'exécution |
| `RCYNQ_*` | n'importe quel réglage de `syncbridge.json` pour le service (`RCYNQ_SyncBridge__ListenPort`, …) |

## Base de données

SQLite. Le schéma n'est créé que par les migrations FluentMigrator ; EF Core ne fait que relier le modèle aux tables.
`EnsureCreated` et les migrations EF Core ne sont pas utilisés. Pour changer le schéma :

1. Ajoutez `Scripts/000N_Name.Up.sql` et `000N_Name.Down.sql` à `RCynQ.Core.Data.Migrations.SQLite`.
2. Ajoutez une classe `Versions/M000N_Name.cs` avec `[Migration(N, "...")]`.
3. Mettez à jour les entités, la correspondance et `RCynQDbContext`.

Le service applique les migrations au démarrage. À la main :

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## Licence

RCynQ est un logiciel libre sous la Licence publique générale GNU, version 3 ou ultérieure (`LICENSE.txt`).

- Seules des dépendances compatibles avec GPL-3.0-or-later. Une dépendance qui finit dans un paquet est inscrite avec le
  texte de sa licence dans `THIRD-PARTY-NOTICES.txt` dans le même changement.
- Chaque paquet contient `LICENSE.txt` et `THIRD-PARTY-NOTICES.txt` à côté du programme (`Write-PackageLicenses` et
  `write_package_licenses` dans `Scripts/Shared/package-notes.*`) ; le `.deb` ajoute `/usr/share/doc/rcynq/copyright`.
- L'assistant d'installation affiche la page de licence dans chaque langue : l'introduction de
  `Scripts/Shared/License/intro.<code>.txt` suivie du texte anglais de la GPL, le seul qui ait valeur juridique.
- La fenêtre « À propos de RCynQ » des réglages indique la licence et ouvre son texte et la liste des composants.

## Règles du code

Voir [AGENTS.md](../../AGENTS.md) : un type par fichier, un commentaire XML sur chaque membre (CS1591 est une erreur),
des accolades autour de chaque bloc, des lignes vides entre les étapes logiques d'une méthode, les opérateurs logiques
en fin de ligne, les constantes en `UPPER_SNAKE_CASE`, les énumérations dans des dossiers `Enums`, les directives
`using` en blocs System, bibliothèques, RCynQ, l'heure par `TimeProvider`, les entrées non fiables toujours validées.
La compilation (analyseurs de `.editorconfig`) et `RCynQ.Conventions.Tests` les vérifient.

## Empaquetage

| Script | Résultat |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` et `RCynQ-<version>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | la même archive plus `RCynQ-<version>-setup.exe` (Inno Setup 6 ou plus récent) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<version>-<runtime>.tar.gz` et, là où `dpkg-deb` existe (Debian, Ubuntu, WSL), `RCynQ-<version>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<version>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | les mêmes paquets pour Linux et macOS, compilés depuis Windows dans WSL |
| `Scripts/build-release.ps1 [-Skip <étapes>] [-ContinueOnError]` | chaque paquet d'une version, l'un après l'autre, et `SHA256SUMS-<version>.txt` |

La compilation est autonome ; l'ordinateur cible n'a pas besoin de .NET, seulement de Git et d'un client SSH. Un paquet
contient le service, la CLI, l'UI et le migrateur de base de données dans `service/`, `cli/`, `ui/` et `dbmigrator/`.
La racine du paquet Windows contient `RCynQ.exe` avec l'icône de l'application : le même hôte .NET que l'UI (la cible
`RCynQRootLauncher` de `RCynQ.UI.csproj`), seulement avec le chemin relatif `ui\RCynQ.UI.dll`.

Les archives et l'installateur Windows pour l'utilisateur actuel sont portables : la marque `rcynq.portable` à la
racine garde la configuration dans `config/` et toutes les données dans `data/` à côté du programme. L'installateur pour
tous les utilisateurs place le programme dans `Program Files` sans la marque, si bien que chaque utilisateur garde la
configuration dans `%APPDATA%\RCynQ` et les données dans `%LOCALAPPDATA%\RCynQ`. Le paquet `.deb` s'installe dans `/opt/rcynq`
sans la marque, si bien que chaque utilisateur garde la configuration dans `~/.config/RCynQ` et les données dans
`~/.local/share/RCynQ`.

Les paquets Linux et macOS sont compilés sous Linux (WSL compte aussi) : c'est seulement là que les droits d'exécution
survivent dans l'archive. Le paquet macOS compilé ainsi n'est pas signé ; voir les notes qu'il contient.

## Flux de travail GitHub

| Flux | Quand | Quoi |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | chaque push sur `main`, chaque pull request, à la main | compile la solution et lance tous les tests sous Windows et Linux ; avec les analyseurs de style et les tests de conventions, une règle enfreinte rend l'exécution rouge |
| **Packages** (`.github/workflows/packages.yml`) | à la main, ou une étiquette de version `X.Y.Z` égale à `<Version>` de `Directory.Build.props` | compile l'installateur et le `.zip` Windows, le `.deb` et le `.tar.gz` Linux, les archives macOS (sous macOS, avec signature ad hoc) ; une étiquette les joint avec `SHA256SUMS-<version>.txt` à un **brouillon** de version |
| **Pages** (`.github/workflows/pages.yml`) | une modification de `Sources/Apps/RCynQ.Pages` sur `main` | publie la page du projet |

Une version : augmentez `<Version>`, poussez l'étiquette (`git tag 0.2.0; git push origin 0.2.0`), écrivez les notes du
brouillon de version, vérifiez les fichiers, publiez-la, et seulement ensuite remplissez `updates.json` de la page du
projet.
