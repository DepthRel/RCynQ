# Architecture de RCynQ

[English](../En/Architecture.md) · [Deutsch](../De/Architecture.md) · [Español](../Es/Architecture.md) · **Français** · [Português](../Pt/Architecture.md) · [Русский](../Ru/Architecture.md)

## Nœuds

Un seul programme remplit trois rôles ; le rôle est choisi dans la configuration.

| Rôle | Ce que c'est | Ce qu'il fait |
|---|---|---|
| **Workstation** | L'ordinateur sur lequel vous travaillez | Garde les copies de travail, récupère les branches, envoie et reçoit les patchs |
| **Origin** | L'ordinateur qui a les dépôts privés | Propose ses dépôts, reçoit et envoie les patchs |
| **Relay** | Un ordinateur que les deux extrémités atteignent | Transfère les connexions SSH. N'a aucun dépôt, ne voit aucun contenu |

Une chaîne est `Workstation → Relay(s) → Origin`. Sans Relay, les deux extrémités se connectent directement.

## Règle : le dépôt de l'Origin est seulement lu

- RCynQ n'écrit rien dans un dépôt Git d'un Origin : ni remote, ni réglage, ni référence, ni commit, ni rafraîchissement de l'index (Git tourne avec `GIT_OPTIONAL_LOCKS=0`).
- Les fichiers de travail de l'Origin ne changent que lorsqu'un patch envoyé par l'utilisateur est appliqué.
- Tout ce que l'Origin conserve (la liste des dépôts, la base de synchronisation, l'historique, les sauvegardes) se trouve dans `syncbridge.json` et dans le dossier de données du nœud.
- L'entretien Git (créer le dépôt dans un dossier vide, le remote `rcynq`, `refs/remotes/rcynq/*`, l'avance rapide au Pull) n'a lieu que dans la copie de travail de la Workstation.

Un test de bout en bout compare chaque fichier sous `.git` avant et après l'enregistrement, la bascule, la mise à disposition et l'inspection d'un dépôt.

## Canaux

Les deux canaux utilisent la même connexion et la même autorisation.

**Canal Git (Origin → Workstation).** La Workstation nomme un dépôt et les commits qu'elle possède déjà. L'Origin répond avec un paquet Git incrémental. La Workstation le récupère dans `refs/remotes/rcynq/*` et fait une avance rapide. Rien n'est jamais poussé.

**Canal des patchs (dans les deux sens).** Un patch est la différence entre l'arbre de travail et la *base de synchronisation* — ce que l'autre nœud est censé avoir. La base de synchronisation est construite automatiquement à partir du commit Git commun aux deux nœuds, plus les fichiers déjà échangés. Livraison : un manifeste signé, puis chaque fichier selon son SHA-256, puis un commit qui applique tout ou rien. Un conflit se résout en gardant le fichier local, en prenant le fichier entrant (avec sauvegarde) ou par une fusion Git à trois voies.

## Comment un nœud accepte les connexions

| Mode | Chemin | Le pair est vérifié par |
|---|---|---|
| Serveur SSH | `ssh -L` vers le port de boucle locale du nœud, par les Relay utilisés comme hôtes de rebond | Clé d'hôte SSH fixée |
| Port TLS propre (par défaut `18444`) | Directement vers le port, ou par un transfert SSH qui se termine sur le dernier Relay | Empreinte du certificat fixée |

Un Relay utilise toujours son serveur SSH. Les clés de `authorized_keys` sont limitées à un transfert de port et ne peuvent exécuter aucune commande.

L'API locale d'un nœud n'écoute que sur la boucle locale (par défaut `18443`) et exige un jeton conservé dans le dossier de données.

## Routes vers un nœud

Un nœud peut être joignable à plusieurs adresses : par exemple par un tunnel sur UDP et par un second tunnel sur TCP. Chacune de ces adresses est une *route* de la liaison. Les clés et les empreintes du nœud sont les mêmes pour toutes les routes ; seule l'adresse diffère.

- **Stockage.** Les routes sont conservées dans la liaison. L'adresse du nœud dans la liaison est toujours celle de la route en usage, si bien que les patchs, le canal Git, le ping et le diagnostic prennent la route choisie sans rien savoir des routes.
- **Vérification.** Toutes les 15 secondes, et aussitôt après un ping échoué, le nœud vérifie chaque route. Un voisin est vérifié par une connexion TCP. Un nœud derrière des Relay est vérifié par un transfert SSH qui se termine à l'adresse de la route : la bannière d'un serveur SSH ou une poignée de main TLS comptent comme réponse.
- **Choix.** La route qui répond avec la priorité la plus basse l'emporte ; à égalité, la plus rapide, si elle est nettement plus rapide que la route en usage. Quand une meilleure route répond de nouveau, le nœud y revient. Tant qu'aucune ne répond, le nœud reste où il est.
- **Choix manuel.** Une route fixée est utilisée qu'elle réponde ou non, jusqu'à ce que le choix soit rendu au nœud.

Une liaison avec une seule adresse n'a pas de routes : elle n'est ni vérifiée ni modifiée, et le nœud fonctionne comme avant.

Le chemin par plusieurs Relay est défini par l'extrémité : elle les liste dans l'ordre et les transmet elle-même à SSH. Un Relay reste passif ; il laisse seulement les clés des extrémités transférer vers les nœuds du rôle opposé et vers les serveurs SSH des Relay voisins, à chaque adresse sous laquelle il les connaît.

## Sécurité de l'échange

| Protection | Mécanisme |
|---|---|
| Qui peut envoyer | Jeton de liaison partagé et identifiant de pair connu dans chaque requête |
| Intégrité et provenance | Signature ECDSA P-256 du manifeste, nom du dépôt compris |
| Rejeu | L'identifiant et l'âge du patch sont vérifiés ; un patch n'est accepté qu'une fois |
| Sortie de l'arbre | Les chemins d'un patch sont canoniques et doivent rester dans l'arbre de travail |
| Mauvais dépôt | Un patch pour un dépôt avec lequel le destinataire ne travaille pas est refusé |
| Exposition de l'Origin | Un voisin n'apprend que des noms de dépôts, jamais des chemins ; un paquet n'est servi que pour un nom enregistré |

## Dépôts

- L'Origin tient un catalogue : nom → dossier. Un nom compte de 1 à 64 caractères parmi `A–Z a–z 0–9 . _ -`.
- La Workstation tient nom → dossier local et ne peut ajouter que des noms proposés par l'Origin.
- Chaque nœud travaille avec un seul dépôt à la fois. La bascule ne demande pas de redémarrage : elle attend la fin de l'envoi, de la réception ou du Pull en cours, puis change l'arbre de travail pour tout en une fois.
- Chaque ping porte, dans les deux sens, le nom du dépôt avec lequel le nœud travaille. Un nœud qui apprend que l'autre a basculé le dit à l'utilisateur et ne le suit pas de lui-même.
- Le nœud tient une base de synchronisation par dépôt. L'historique des patchs est affiché pour le dépôt actuel.

## Stockage

```
<dossier du programme>/
  config/syncbridge.json     réglages du nœud, liaisons, dépôts
  data/
    rcynq.db                 SQLite : pairs, liaisons, patchs, historique, secrets chiffrés (clé de signature)
    keys/                    clé SSH, certificat TLS, jeton de l'API, known_hosts
    patches/  backups/       contenu des patchs et fichiers remplacés par un patch
    snapshots/               base de synchronisation (un dossier par dépôt supplémentaire)
    logs/
```

Un fichier de configuration écrit par une version antérieure est converti au démarrage ; l'original est conservé sous le nom `syncbridge.json.bak`.

## Projets

| Projet | Rôle |
|---|---|
| `RCynQ.Core` | Contrats, configuration, enregistrements de l'API |
| `RCynQ.Localization` | Textes, un fichier JSON par langue |
| `RCynQ.Core.Data`, `…Migrations`, `…Migrations.SQLite` | Stockage SQLite et migrations du schéma |
| `RCynQ.Git` | Commandes Git, manifestes, construction des patchs |
| `RCynQ.Ssh` | Commandes SSH, transferts de ports, gestion des clés et des clés d'hôte |
| `RCynQ.Transports.*` | Liaisons vers les voisins et leur état |
| `RCynQ.Sync` | Patchs, base de synchronisation, canal Git, dépôts, diagnostic |
| `RCynQ.Service` | API HTTP locale et événements SignalR |
| `RCynQ.UI` | Application de bureau (Avalonia) ; démarre le service |
| `RCynQ.Cli` | Ligne de commande `rcynq` |
| `RCynQ.Dbs.SQLite.DbMigrator` | Migrateur de base de données en console |
| `RCynQ.Pages` | Page statique du projet pour GitHub Pages (sans compilation) |

Les tests se trouvent dans `Tests/<Projet>.Tests` ; `RCynQ.Conventions.Tests` vérifie les règles de code de
`AGENTS.md`. Comment compiler, tester et empaqueter : [Développement](Development.md).
