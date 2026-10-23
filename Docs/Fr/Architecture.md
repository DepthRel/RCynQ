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

- RCynQ ne pousse jamais et ne crée dans un dépôt Git d'un Origin ni commit, ni remote, ni réglage, ni référence qui lui soient propres ; il ne rafraîchit pas son index (Git tourne avec `GIT_OPTIONAL_LOCKS=0`).
- L'arbre de travail et la branche extraite de l'Origin ne changent que par ce que l'utilisateur a envoyé : les fichiers d'un patch ou, si l'Origin accepte les commits, les propres commits de l'utilisateur, jusqu'auxquels la branche avance (fast-forward). Le reflog de la branche garde cette avance, et Git l'annule simplement.
- Tout ce que l'Origin conserve (la liste des dépôts, la base de synchronisation, l'historique, les sauvegardes, les commits en attente d'application) se trouve dans `syncbridge.json` et dans le dossier de données du nœud.
- L'entretien Git (créer le dépôt dans un dossier vide, le remote `rcynq`, `refs/remotes/rcynq/*`, l'avance rapide au Pull) n'a lieu que dans la copie de travail de la Workstation.

Des tests de bout en bout comparent chaque fichier sous `.git` avant et après l'enregistrement, la bascule, la mise à disposition et l'inspection d'un dépôt, ainsi qu'avant et après une livraison de commits refusée ou mise en attente.

## Canaux

Les deux canaux utilisent la même connexion et la même autorisation.

**Canal Git (Origin → Workstation).** La Workstation nomme un dépôt et les commits qu'elle possède déjà. L'Origin répond avec un paquet Git incrémental. La Workstation le récupère dans `refs/remotes/rcynq/*` et fait une avance rapide. Rien n'est jamais poussé. Ensuite, l'Origin liste toutes ses branches et `refs/remotes/rcynq/*` est aligné sur cette liste : un paquet ne porte que les branches qui ont de nouveaux commits, donc une branche créée sur un commit que la Workstation possède déjà, une branche ramenée en arrière et une branche supprimée arrivent par ce biais. Un Pull fait suivre `rcynq/<branche>` à la branche courante, pour que Git la compare avec l'Origin et non avec un remote que cet ordinateur ne peut pas joindre.

**Canal des patchs (dans les deux sens).** Un patch est la différence entre l'arbre de travail et la *base de synchronisation* — ce que l'autre nœud est censé avoir. La base de synchronisation est construite automatiquement à partir du commit Git commun aux deux nœuds, plus les fichiers déjà échangés. Livraison : un manifeste signé, puis chaque fichier selon son SHA-256, puis un commit qui applique tout ou rien. Un conflit se résout en gardant le fichier local, en prenant le fichier entrant (avec sauvegarde) ou par une fusion Git à trois voies.

**Livraison de commits (Workstation → Origin).** Les commits préparés sur la Workstation (cherry-picks, fusions) arrivent sur l'Origin tels quels : mêmes hachages, mêmes auteurs, même ordre. La Workstation envoie un bloc de ses commits non envoyés qui commence par le plus ancien, sous forme de paquet Git avec une enveloppe signée ; la session du patch le transporte. L'Origin ne l'accepte que si l'utilisateur a activé **Accepter les commits de la Workstation**, seulement pour la branche extraite et seulement si son `HEAD` est le commit sur lequel repose le bloc ; sinon il refuse aussitôt. Les objets sont décompressés dans une quarantaine à côté du dépôt et comparés à la liste signée (la chaîne des premiers parents, chaque chemin que touche un commit) ; alors seulement les packs sont déplacés dans `.git` et la branche avance. Si l'avance échoue, les packs sont retirés. Tant que l'arbre de travail de l'Origin a des modifications, les commits attendent : l'utilisateur les applique ou les refuse là-bas, et les patchs de fichiers sont refusés entre-temps. Une fois les commits appliqués, un Fetch met la Workstation à jour avec l'Origin. Tant qu'une branche repose directement sur le commit de l'Origin, ses commits ne sont pas des modifications à envoyer : le canal des patchs ne propose que ce qui n'est pas commité, toujours par rapport à la version qu'a l'Origin. Une Workstation annule des commits non envoyés (Soft, Mixed, Hard) seulement si son arbre de travail n'a pas de modifications.

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

## Invitations

Un nœud invite un voisin avec un ticket à usage unique : un secret aléatoire, dont le nœud ne garde que le SHA-256, un rôle et une expiration. L'invitation contient la carte du nœud, les cartes des Relays sur le chemin, le ticket, le jeton de liaison pour la contrepartie et le ticket du Relay quand le nœud en a un.

- **Par le serveur SSH.** Le ticket est accompagné d'une clé Ed25519 à usage unique. Sa ligne dans `authorized_keys` est `restrict,expiry-time=…,command="rcynq enroll --config …"` : elle n'exécute que l'adhésion et le serveur la refuse après l'expiration. Le nœud qui rejoint se connecte avec elle, par les Relays comme hôtes de rebond, et envoie sa carte comme commande ; `rcynq enroll` la transmet au service sur loopback. La ligne est retirée dès que le ticket est utilisé, révoqué ou expiré.
- **Par le port TLS propre.** L'adhésion est envoyée à `/peer/enroll` de ce port ; l'empreinte du certificat de l'invitation est épinglée.

Le nœud qui invite vérifie le ticket, le consomme en une seule étape, importe la carte et autorise la clé. Un Relay répond à une extrémité par un ticket pour l'extrémité opposée, que l'extrémité transmet dans sa propre invitation. La clé d'hôte ou le certificat de chaque nœud est épinglé depuis l'invitation avant tout envoi. L'adhésion automatique passe par un Relay au plus ; un chemin plus long demande des fichiers.

## Sécurité de l'échange

| Protection | Mécanisme |
|---|---|
| Qui peut envoyer | Jeton de liaison partagé et identifiant de pair connu dans chaque requête |
| Intégrité et provenance | Signature ECDSA P-256 du manifeste, nom du dépôt compris |
| Rejeu | L'identifiant et l'âge du patch sont vérifiés ; un patch n'est accepté qu'une fois |
| Sortie de l'arbre | Les chemins d'un patch sont canoniques et doivent rester dans l'arbre de travail |
| Mauvais dépôt | Un patch pour un dépôt avec lequel le destinataire ne travaille pas est refusé |
| Exposition de l'Origin | Un voisin n'apprend que des noms de dépôts, jamais des chemins ; un paquet n'est servi que pour un nom enregistré |
| Commits | Désactivé tant que l'utilisateur ne les autorise pas sur l'Origin ; la branche, la base, chaque commit et le hachage du paquet sont signés ; les objets restent en quarantaine jusqu'à vérification ; uniquement une avance (fast-forward) de la branche extraite |
| Adhésion | Ticket à usage unique avec expiration (seul le hachage est gardé) ; clé SSH à usage unique limitée à la commande d'adhésion, avec `expiry-time` ; clé d'hôte et certificat épinglés depuis l'invitation |

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
