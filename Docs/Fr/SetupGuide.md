# Guide d'installation de RCynQ

[English](../En/SetupGuide.md) · [Deutsch](../De/SetupGuide.md) · [Español](../Es/SetupGuide.md) · **Français** · [Português](../Pt/SetupGuide.md) · [Русский](../Ru/SetupGuide.md)

Trois ordinateurs participent :

| Nœud | Ordinateur |
|---|---|
| **Workstation** | celui où vous modifiez le projet |
| **Origin** | celui où se trouvent les dépôts privés |
| **Relay** | un ordinateur que les deux autres peuvent atteindre |

Suivez les étapes dans l'ordre. Lorsqu'une étape a des variantes, choisissez celle qui convient à l'ordinateur.

## 1. Vérifier qui atteint qui

| De | Vers | Port |
|---|---|---|
| Workstation | Relay | 22 |
| Origin | Relay | 22 |
| Relay | Origin | 22 (variante A de l'étape 3) ou 18444 (variante B) |
| Relay | Workstation | 22 (variante A) ou 18444 (variante B) |

La façon dont les ordinateurs s'atteignent (réseau local, n'importe quel VPN, adresse publique) vous appartient ; RCynQ a seulement besoin que les adresses répondent. Notez l'adresse de chaque ordinateur telle que son voisin la voit.

**Sans Relay.** Si la Workstation et l'Origin s'atteignent directement, sautez l'étape 4 et toute mention du Relay.

## 2. Installer

Sur chaque ordinateur, au choix :

| Système | Paquet | Où vont les réglages et les données |
|---|---|---|
| Windows | `RCynQ-<version>-setup.exe`, **installer pour moi uniquement** (le choix par défaut) : dans `%LOCALAPPDATA%\Programs\RCynQ`, sans droits d'administrateur | `config` et `data` dans l'installation |
| Windows | `RCynQ-<version>-setup.exe`, **installer pour tous les utilisateurs** : dans `Program Files`, Windows demande les droits d'administrateur | `%APPDATA%\RCynQ` et `%LOCALAPPDATA%\RCynQ` de chaque utilisateur |
| Windows | `RCynQ-<version>-win-x64.zip` : décompressez-le dans un dossier accessible en écriture, par exemple `C:\RCynQ`, pas dans `Program Files` | `config` et `data` à côté du programme |
| Debian, Ubuntu | `RCynQ-<version>-linux-x64.deb` : `sudo apt install ./RCynQ-<version>-linux-x64.deb` ; Git et le client SSH sont installés avec lui | `~/.config/RCynQ` et `~/.local/share/RCynQ` de chaque utilisateur |
| Linux, macOS | `RCynQ-<version>-<platform>.tar.gz` : décompressez-le dans un dossier accessible en écriture, par exemple `~/rcynq` | `config` et `data` à côté du programme |

Lancez ensuite RCynQ : depuis le menu Démarrer ou `RCynQ.exe` (Windows), depuis le menu des applications ou `rcynq-ui`
(`.deb`), `./RCynQ.sh` (archives pour Linux et macOS). Le `README.txt` du paquet dit la même chose pour son système.

Sur la Workstation et l'Origin, installez Git et vérifiez-le : `git --version`.

Utilisez un paquet propre pour chaque ordinateur. Ne copiez pas un dossier dans lequel RCynQ a déjà tourné : ses dossiers `config` et `data` sont l'identité de ce nœud.

## 3. Choisir comment le nœud accepte les connexions

Le Relay utilise toujours la variante A. Pour la Workstation et l'Origin, choisissez pour chaque ordinateur.

### Variante A — serveur SSH

Le plus rapide : **Rôle et nœuds → Exporter la configuration → Installer le serveur SSH** (ou `rcynq ssh install-server`), puis confirmez la demande d'administrateur. RCynQ installe, démarre et ouvre le serveur lui-même ; si c'est impossible, il affiche les commandes à exécuter à la main. Les commandes ci-dessous font la même chose à la main.

**Windows** (PowerShell en tant qu'administrateur) :

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
```

Si la première commande échoue avec `0x800f0954`, installez `OpenSSH-Win64-*.msi` depuis les [versions de Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH/releases) avec `msiexec /i <fichier> ADDLOCAL=Server`, démarrez le service comme ci-dessus et ouvrez le port :

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Linux :**

```bash
sudo apt install openssh-server      # ou : sudo dnf install openssh-server
sudo systemctl enable --now ssh      # sous Fedora et RHEL le service s'appelle sshd
```

**macOS :** Réglages Système → Général → Partage → Session à distance.

Vérifiez : `ssh -V` affiche une version et le service tourne (`Get-Service sshd` / `systemctl status ssh`).

### Variante B — port TLS propre, sans serveur SSH

Pour un ordinateur où l'on ne peut pas installer de serveur SSH. Ouvrez le port TCP entrant `18444`.

Le plus rapide : **Rôle et nœuds → Exporter la configuration → Ouvrir le port 18444 dans le pare-feu** (ou `rcynq endpoint open-port`), puis confirmez la demande d'administrateur. À la main :

**Windows** (PowerShell en tant qu'administrateur) :

```powershell
New-NetFirewallRule -Name rcynq -DisplayName 'RCynQ' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 18444
```

**Linux :** `sudo ufw allow 18444/tcp` ou `sudo firewall-cmd --permanent --add-port=18444/tcp && sudo firewall-cmd --reload`.

Le *client* SSH reste nécessaire sur cet ordinateur quand un Relay est utilisé : `ssh -V`.

## Le plus rapide : les invitations

Faites les étapes 1 à 3 et fixez le rôle de chaque ordinateur dans la fenêtre de configuration. Ensuite, deux lignes de texte configurent toute la chaîne .

Après la première configuration, l'assistant **Connecter un nœud** s'ouvre de lui-même ; ensuite, il se trouve sous **Rôle et nœuds → Connecter un nœud**. Il vérifie que l'ordinateur accepte des voisins, propose d'installer le serveur SSH sinon, et dessine la chaîne pendant qu'il travaille.

1. Sur le Relay : dans l'assistant **Suivant**, puis **Créer l'invitation** pour l'**Origin**, et copiez-la.
2. Sur l'Origin : **Par un Relay → Suivant → J'ai une invitation**, collez-la, **Rejoindre**. L'assistant propose ensuite **Inviter un nœud** : créez l'invitation pour la Workstation et copiez-la.
3. Sur la Workstation : **Par un Relay → Suivant → J'ai une invitation**, collez-la, **Rejoindre**.

Sans Relay, choisissez **Directement** : l'Origin invite, la Workstation rejoint avec son invitation. Chaque étape de l'adhésion s'affiche avec son résultat ; poursuivez ensuite avec l'étape 9.

Si le Relay doit autoriser un nœud avec les droits d'administrateur, confirmez la demande sur le Relay : l'adhésion l'attend jusqu'à trois minutes, puis continue d'elle-même.

Une invitation est un secret : quiconque la possède peut rejoindre une fois jusqu'à son expiration (1 heure, 24 heures ou 7 jours). Transmettez-la en privé. Elle contient le jeton de liaison, rien n'est à copier à la main. Ligne de commande : `rcynq invite --role Workstation`, `rcynq join <invitation>`, `rcynq invitation list`, `rcynq invitation revoke <id>`.

Les étapes 4 à 8 ci-dessous sont la même configuration avec des fichiers, quand une invitation ne peut pas servir.

## 4. Configurer le Relay

1. Lancez RCynQ. Dans la fenêtre de configuration, indiquez l'identifiant du nœud (par exemple `relay-1`), le rôle **Relay**, puis enregistrez.
2. **Rôle et nœuds → Exporter la configuration.** Toutes les adresses de l'ordinateur vont dans le fichier ; saisissez en plus un nom d'hôte si le Relay est atteint par un nom ou derrière un NAT. Exportez `relay.rcynq.json`. Les deux extrémités importent ce seul fichier, et chacune prend l'adresse qui lui répond.

## 5. Configurer l'Origin

1. Lancez RCynQ. Dans la fenêtre de configuration, indiquez :
   - l'identifiant du nœud, par exemple `origin-1` ;
   - le rôle **Origin** ;
   - le dossier du dépôt Git (celui qui contient `.git`) ;
   - **Comment les voisins se connectent à ce nœud** — *Par le serveur SSH de cet ordinateur* (variante A) ou *Sans serveur SSH : port propre avec TLS* (variante B).
2. Copiez le **jeton de liaison partagé** généré. La Workstation a besoin du même.
3. Enregistrez.
4. **Rôle et nœuds → Exporter la configuration.** La clé est créée d'elle-même si l'ordinateur n'en a pas. Exportez `origin.rcynq.json`.

Le dépôt porte le nom de son dossier. C'est ce nom que verra la Workstation.

## 6. Configurer la Workstation

1. Lancez RCynQ. Dans la fenêtre de configuration, indiquez :
   - l'identifiant du nœud, par exemple `workstation-1` ;
   - le rôle **Workstation** ;
   - un dossier pour la copie de travail. Un dossier vide convient : le premier Pull le remplit ;
   - le **jeton de liaison partagé** copié depuis l'Origin ;
   - **Comment les voisins se connectent à ce nœud** — variante A ou B comme à l'étape 3.
2. Enregistrez.
3. **Rôle et nœuds → Exporter la configuration.** Exportez `workstation.rcynq.json`.

## 7. Échanger les fichiers des nœuds

Sur chaque ordinateur : **Rôle et nœuds → Importer une configuration**, dans n'importe quel ordre. Après chaque import, RCynQ lance la vérification et dit si la liaison fonctionne.

| Sur | Importer |
|---|---|
| Workstation | `relay.rcynq.json` et `origin.rcynq.json` |
| Origin | `relay.rcynq.json` et `workstation.rcynq.json` |
| Relay | `workstation.rcynq.json` et `origin.rcynq.json` |

Sans Relay : la Workstation importe `origin.rcynq.json`, l'Origin importe `workstation.rcynq.json`.

Les fichiers ne contiennent aucune clé privée. Transmettez-les par un canal de confiance.

## 8. Autoriser les connexions

Après chaque import, RCynQ propose d'autoriser la clé du voisin.

- **Variante A :** confirmez. Sous Windows, confirmez aussi la demande d'administrateur. La clé est limitée au transfert d'un port et ne peut exécuter aucune commande.
- **Variante B :** rien à faire ; il n'y a pas de serveur SSH à configurer.

Si vous avez refusé, cliquez sur **Rôle et nœuds → Autoriser les connexions des nœuds**.

## 9. Vérifier et faire un Pull

1. Sur la Workstation et l'Origin : **Diagnostic → Lancer la vérification**. Corrigez ce qui est en rouge ; chaque vérification échouée dit comment.
2. Sur la Workstation : **Branches → Pull depuis Origin**. Le dossier contient maintenant le projet.
3. Modifiez un fichier, ouvrez **Synchronisation**, cliquez sur **Envoyer les modifications**. Le fichier apparaît sur l'Origin.

Désormais : **Pull** pour obtenir les nouveaux commits, **Envoyer les modifications** pour livrer vos modifications. Les modifications faites sur l'Origin s'envoient de là-bas de la même façon.

## 10. D'autres dépôts

1. Sur l'Origin : **Branches → Ajouter un dépôt**, choisissez le dossier d'un autre dépôt Git.
2. Sur l'un ou l'autre nœud, choisissez le dépôt dans la liste en haut de la fenêtre. Le nœud bascule sans redémarrer.
3. Sur la Workstation, un dépôt qui n'est pas encore sur l'ordinateur demande un dossier, puis se récupère tout seul.
4. L'autre nœud affiche un avis : **Basculer aussi** ou **Rester**. Les modifications ne s'échangent que tant que les deux nœuds travaillent avec le même dépôt.

Ligne de commande : `rcynq repo list`, `rcynq repo add <name> <path>`, `rcynq repo use <name>`, `rcynq repo remove <name>`.

Retirer un dépôt de la liste ne supprime jamais son dossier ni ses fichiers.

## 11. Routes de secours et plusieurs Relay

Quand un nœud de ce rôle est déjà dans la chaîne, un import ne change rien en silence : il demande ce qu'est le fichier.

| Ce qui est importé | Choix |
|---|---|
| Le même nœud à une autre adresse | **Une autre route vers le même nœud** — l'adresse s'ajoute aux routes du nœud. **Remplacement** — la nouvelle adresse prend la place de celle en usage |
| Un autre Relay | **Un nœud de plus** — le Relay rejoint la chaîne avant l'extrémité. **Remplacement** — il prend la place du Relay choisi |
| Une autre extrémité | **Remplacement** seulement : une chaîne se termine par un seul nœud |

Un nœud remplacé est oublié avec ses clés. Après un remplacement, cliquez sur **Autoriser les connexions des nœuds** pour que sa clé perde son accès.

**Une route de secours.** Exemple : deux ordinateurs sont reliés par un tunnel AmneziaWG sur UDP, et un tunnel sur TCP fonctionne à côté.

1. Sur le voisin, exportez de nouveau la configuration en choisissant l'adresse du second tunnel.
2. Importez le fichier et choisissez **Une autre route vers le même nœud**.
3. Sous le nœud, dans **Nœuds de la chaîne**, ses routes apparaissent avec leur état et leur latence.

Le **choix automatique** est activé par défaut : la route préférée qui répond est utilisée et, à égalité, la plus rapide. Quand une route cesse de répondre, le trafic passe à la suivante ; quand la meilleure répond de nouveau, le nœud y revient. Les flèches changent la priorité (la plus basse est préférée). La marque à côté d'une route la fixe à la main jusqu'à ce que vous reveniez au choix automatique.

Si le chemin passe par un Relay, importez-y aussi la seconde adresse : un Relay ne transfère que vers les adresses qu'il connaît.

**Une chaîne de deux Relay** (`Workstation → Relay 1 → Relay 2 → Origin`).

1. Sur la Workstation et l'Origin, importez les deux Relay ; pour le second, choisissez **Un nœud de plus**. L'ordre est celui de l'import, compté depuis ce nœud.
2. Sur chaque Relay, importez le Relay voisin et les deux extrémités.
3. Sur chaque nœud, cliquez sur **Autoriser les connexions des nœuds**.

Ligne de commande : `rcynq import <file> --as route|replace|hop [--link <link>]`. Sans `--as`, un tel import ne change rien et liste les choix.

Dans `syncbridge.json`, les adresses de secours d'une liaison sont listées dans `Routes` (`Host`, `Name` facultatif, `SshPort`, `SshUser`, `Priority`) ; la priorité de l'adresse de la liaison elle-même est `RoutePriority`.

## 12. Commits préparés sur la Workstation

Quand une branche est préparée sur la Workstation (cherry-picks, fusions pour une version), ses commits peuvent arriver sur l'Origin tels quels : mêmes hachages, mêmes auteurs, même ordre, dans la branche locale de l'Origin et prêts à être poussés depuis là. RCynQ lui-même ne pousse jamais.

1. Sur l'Origin : **Réglages → Commits de la Workstation → Accepter les commits de la Workstation**. C'est désactivé par défaut ; tant que c'est désactivé, l'Origin refuse les commits.
2. Sur la Workstation, la branche doit exister sur l'Origin et reposer directement sur son dernier commit : faites d'abord un **Pull**.
3. **Synchronisation → Commits à envoyer** liste les commits que l'Origin n'a pas, les plus récents en haut. Cochez le plus récent à envoyer ; tous les plus anciens sont cochés avec lui.
4. Cliquez sur **Envoyer les commits**. L'Origin fait avancer la même branche et la liste se vide.

Si l'arbre de travail de l'Origin a des modifications, les commits y attendent : occupez-vous des modifications (stash ou abandon) puis cliquez sur **Appliquer** sur l'Origin, ou sur **Refuser**. D'ici là, l'Origin refuse les patchs de fichiers. Les commits qui reposent sur un autre commit, ou destinés à une autre branche que celle extraite sur l'Origin, sont refusés aussitôt.

**Annuler des commits** annule les commits non envoyés les plus récents, comme `git reset` : Soft garde leurs modifications dans l'index, Mixed dans l'arbre de travail, Hard les abandonne. Cela ne fonctionne que si l'arbre de travail n'a pas de modifications.

Ligne de commande : `rcynq commits list`, `rcynq commits send [--up-to <hash>]`, `rcynq commits reset --to <hash> --mode Soft|Mixed|Hard`, sur l'Origin `rcynq commits apply <id>` et `rcynq commits decline <id>`. Dans `syncbridge.json`, l'interrupteur s'appelle `AcceptCommits`.

## Mise à jour depuis une version antérieure

Les versions antérieures appelaient les rôles Source, Mediator et Dest. Ils s'appellent désormais Workstation, Relay et Origin.

1. Mettez à jour **tous** les nœuds en même temps : l'ancienne et la nouvelle version ne se comprennent pas.
2. Lancez chaque nœud. Sa configuration est convertie toute seule ; le fichier précédent est conservé sous le nom `syncbridge.json.bak`.
3. Sur les nœuds dotés d'un serveur SSH, cliquez une fois sur **Rôle et nœuds → Autoriser les connexions des nœuds**. Cela réécrit les anciennes lignes de clés, qui autorisaient plus que nécessaire aujourd'hui.

## Si quelque chose ne marche pas

| Symptôme | Vérifier |
|---|---|
| Une liaison est hors ligne | Lancez le **Diagnostic** sur ce nœud. Depuis le voisin : `Test-NetConnection <adresse> -Port 22` (Windows) ou `nc -vz <adresse> 22`. Pour la variante B, utilisez le port `18444`. |
| « Le jeton de liaison est absent ou non valide » | Le jeton de liaison partagé diffère entre la Workstation et l'Origin. Copiez-le de nouveau dans l'onglet **Rôle et nœuds**. |
| La clé d'un nœud manque | Exportez de nouveau son fichier en cochant **Inclure la clé SSH publique du nœud** et importez-le. |
| « travaille avec le dépôt … et le patch est pour … » | Les nœuds travaillent avec des dépôts différents. Basculez l'un d'eux. |
| Le Pull refuse à cause de modifications locales | Les fichiers listés ont été modifiés et non envoyés. Envoyez-les d'abord ou annulez les modifications. |
| L'adresse d'un nœud a changé | Exportez son fichier avec la nouvelle adresse, importez-le chez les voisins et cliquez sur **Autoriser les connexions des nœuds**. |

Les journaux se trouvent dans l'onglet **Journaux** et dans `data/logs`.
