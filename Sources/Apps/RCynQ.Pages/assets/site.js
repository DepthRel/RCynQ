// The project page: the texts of every language, the language picker and the download buttons. English is written
// in the HTML itself, so the page reads fine without scripts; the other languages are tables here. The list of
// languages, the picker and the links to the documentation live in languages.js.
(function () {
  'use strict';

  var languages = window.RCynQLanguages;

  var texts = {
    de: {
      'page.title': 'RCynQ: Remote Code Synchronization, Quick',
      'nav.language': 'Sprache',
      'nav.why': 'Vorteile von RCynQ',
      'nav.download': 'Herunterladen',
      'nav.video': 'Videoanleitungen',
      'hero.title': 'Bringen Sie Ihren Code auf einen entfernten Rechner.<br><em>Ohne Commit.</em>',
      'hero.lead': 'RCynQ bringt Ihre nicht committeten Änderungen auf den Rechner mit dem Repository und holt dessen Branches zurück. Direkt, wenn sich die beiden sehen, sonst über ein Relay, das nur verschlüsselten Verkehr durchlässt.',
      'hero.download': 'RCynQ herunterladen',
      'hero.how': 'So funktioniert es',
      'hero.platforms': 'Frei und quelloffen · Windows · Linux · macOS · Anwendung und Befehlszeile',
      'chain.workstation': 'hier arbeiten Sie',
      'chain.relay': 'sieht nichts',
      'chain.origin': 'die Repositories',
      'chain.patch': 'Ihre Änderungen als signierter Patch',
      'chain.bundle': 'Branches als Git-Bundle',
      'how.eyebrow': 'So funktioniert es',
      'how.title': 'Zwei Schaltflächen statt Commit und Push',
      'how.sub': 'Ein Programm übernimmt auf jedem Rechner eine von drei Rollen. Sie arbeiten weiter, wo Sie sind; RCynQ bewegt den Code.',
      'how.1.title': 'Arbeiten, wo Sie sind',
      'how.1.text': 'Arbeiten Sie in Ihrem gewohnten Editor auf der Workstation. Keine WIP-Commits, keine Branches zum späteren Aufräumen.',
      'how.2.title': 'Änderungen senden',
      'how.2.text': 'Die Bearbeitungen gehen als Patch: ein signiertes Manifest, dann jede Datei nach ihrem SHA-256.',
      'how.3.title': 'Als Ganzes angewendet',
      'how.3.text': 'Origin wendet alles oder nichts an. Ein Konflikt wird Datei für Datei gezeigt: behalten, übernehmen, mit Git oder im eigenen Merge-Tool zusammenführen.',
      'how.4.title': 'Zurückholen',
      'how.4.text': 'Eine Schaltfläche holt den aktuellen Branch als inkrementelles Git-Bundle, auch in einen leeren Ordner.',
      'role.workstation': 'Der Rechner, auf dem Sie arbeiten. Hält die Arbeitskopien, sendet Änderungen, holt Branches.',
      'role.relay': 'Ein beliebiger Rechner, den beide Enden erreichen. Leitet SSH weiter, hat kein Repository und sieht keinen Inhalt.',
      'role.origin': 'Der Rechner mit den privaten Repositories. RCynQ liest sie nur; dort ändert sich nur, was Sie selbst gesendet haben.',
      'relay.eyebrow': 'Relay-Knoten',
      'relay.title': 'Ihr Code kommt dorthin, wo Sie nicht hinkommen',
      'relay.sub': 'Das Repository liegt in einem geschlossenen Netz, der Rechner des Entwicklers außerhalb davon. Stellen Sie ein Relay dazwischen: Jeder Rechner mit SSH-Server, den beide erreichen, genügt, etwa eine Cloud-VM, ein Bastion-Host, ein Bürogateway oder der Rechner eines Kollegen.',
      'relay.1': 'Eine Kette führt über so viele Relays, wie der Weg braucht:<br><code class="route">Workstation → Relay 1 → Relay 2 → Origin</code>',
      'relay.2': 'Ein Relay bleibt passiv. Der Schlüssel eines Knotens in <code>authorized_keys</code> darf nur zu den nächsten Knoten auf dem Weg weiterleiten und keinen Befehl ausführen',
      'relay.3': 'Der Verkehr ist durchgehend verschlüsselt: Das Relay sieht nie eine Datei, einen Branch oder einen Repository-Pfad',
      'relay.4': 'Ein Knoten kann Ersatzrouten haben, zum Beispiel zwei Tunnel. RCynQ wechselt von selbst auf eine andere Route, wenn die aktuelle nicht mehr antwortet',
      'hops.workstation': 'Ihr System',
      'hops.relay1': 'eine Cloud-VM',
      'hops.relay2': 'der Bastion-Host im Büro',
      'hops.origin': 'das geschlossene Netz',
      'why.eyebrow': 'Vorteile von RCynQ',
      'why.title': 'Schnelle Einrichtung, zuverlässige Zustellung',
      'why.sub': 'Gemacht für geschlossene Netze, in denen Code sicher und einfach den Rechner mit dem Repository erreichen muss.',
      'why.new': 'Neu!',
      'why.1.title': 'Flexible Zustellung',
      'why.1.text': 'Unfertige Arbeit kommt als Patch zu Origin, ohne Commits und Pushes. Auch Commits lassen sich senden, etwa wenn ein Release-Branch vorbereitet wird.',
      'why.2.title': 'Patches von Hand',
      'why.2.text': 'Speichern Sie die gewählten Änderungen in einer gewöhnlichen <code>.patch</code>-Datei und wenden Sie sie später ganz oder Datei für Datei an. Nichts kommt in den Index oder in einen Commit.',
      'why.3.title': 'Portabel',
      'why.3.text': 'Die Anwendung muss nicht installiert werden, die portable Version genügt. Alle Einstellungen liegen direkt im Ordner der Anwendung.',
      'why.4.title': 'Ein Relay sieht Ihren Code nie',
      'why.4.text': 'Der Verkehr ist durchgehend verschlüsselt: Ein Relay leitet ihn weiter, kann ihn aber nicht lesen. Patches sind mit ECDSA P-256 signiert: Ein gefälschter oder wiederholter wird abgelehnt, und keine Datei landet außerhalb des Arbeitsverzeichnisses.',
      'why.5.title': 'Origin bleibt in Ihrer Hand',
      'why.5.text': 'RCynQ schreibt keine Remotes, Referenzen oder Einstellungen in sein Repository und erstellt keine Commits. Dort ändern sich nur Dateien, die Sie gesendet oder angewendet haben, und der Branch rückt nur vor, wenn Sie Commits erlaubt haben.',
      'why.6.title': 'Konflikte im eigenen Werkzeug',
      'why.6.text': 'Git führt Änderungen an verschiedenen Stellen selbst zusammen, und Dateien mit kollidierenden Änderungen öffnen sich nacheinander in Ihrem gewohnten Merge-Tool: VS Code, Visual Studio, JetBrains, KDiff3, Meld, WinMerge, Beyond Compare und weitere. Geschrieben wird erst, wenn jede Datei zusammengeführt ist.',
      'why.7.title': 'Kein SSH-Server? Kein Problem',
      'why.7.text': 'Ein Endknoten kann Verbindungen auf seinem eigenen TLS-Port annehmen, geprüft durch ein festgelegtes Zertifikat.',
      'why.8.title': 'Viele Repositories',
      'why.8.text': 'Registrieren Sie auf Origin so viele wie nötig und wechseln Sie von beiden Seiten aus ohne Neustart.',
      'why.9.title': 'Fenster oder Terminal',
      'why.9.text': 'Eine Desktop-Anwendung für jeden Tag und die Befehlszeile <code>rcynq</code> für Skripte.',
      'start.eyebrow': 'Erste Schritte',
      'start.title': 'Von leeren Rechnern bis zum ersten Pull',
      'start.sub': 'Kein Server bei uns zu mieten, kein Konto, keine Cloud in der Mitte: nur Ihre Rechner.',
      'start.1.title': 'Installieren',
      'start.1.text': 'Installieren Sie RCynQ auf Workstation und Origin, bei Bedarf auch auf einem Relay, und wählen Sie die Rolle jedes Rechners.',
      'start.2.title': 'Knoten verbinden',
      'start.2.text': 'Erstellen Sie auf einem Knoten eine Einladung und fügen Sie sie beim Nachbarn ein: Eine Textzeile enthält alles, und RCynQ erlaubt die Schlüssel selbst. Wo eine Einladung nicht geht, leisten Knotendateien dasselbe.',
      'start.3.title': 'Pull und senden',
      'start.3.text': 'Starten Sie die Diagnose, klicken Sie auf Pull, bearbeiten Sie eine Datei, klicken Sie auf „Änderungen senden“. Fertig.',
      'start.note': '<b>Schritt für Schritt:</b> die Einrichtungsanleitung führt durch jeden Befehl, für Windows, Linux und macOS.',
      'dl.eyebrow': 'Herunterladen',
      'dl.title': 'RCynQ herunterladen',
      'dl.sub': 'Dieselbe Version auf jedem Knoten einer Kette.<br>Auf Workstation und Origin wird Git gebraucht.',
      'dl.windows': 'Ein Installer nur für Sie, ohne Administratorrechte (Einstellungen und Daten bleiben in der Installation), oder für alle Benutzer des Computers.',
      'dl.button': 'Herunterladen',
      'dl.windows.other': 'oder das portable .zip-Archiv',
      'dl.linux': 'Ein Paket für Ubuntu, Debian und verwandte Systeme: Git und der SSH-Client kommen mit.',
      'dl.linux.other': 'oder das portable .tar.gz-Archiv',
      'dl.macos': 'Ein portables Archiv für Macs mit Apple Silicon.',
      'dl.macos.other': 'oder für Macs mit Intel-Prozessor',
      'dl.version': 'Version ',
      'dl.for': 'Herunterladen für ',
      'foot.name': 'RCynQ ist freie Software unter der Lizenz GPL-3.0-or-later.',
      'foot.source': 'Quelltext',
      'foot.guide': 'Einrichtungsanleitung',
      'foot.architecture': 'Architektur',
      'foot.license': 'Lizenz',
      'foot.releases': 'Alle Releases',
      'foot.mail': 'Schreiben Sie uns'
    },
    es: {
      'page.title': 'RCynQ: Remote Code Synchronization, Quick',
      'nav.language': 'Idioma',
      'nav.why': 'Ventajas de RCynQ',
      'nav.download': 'Descargar',
      'nav.video': 'Videotutoriales',
      'hero.title': 'Lleva tu código a una máquina remota.<br><em>Sin un commit.</em>',
      'hero.lead': 'RCynQ lleva tus cambios sin commit al equipo que tiene el repositorio y trae de vuelta sus ramas. Directamente si los dos se ven y, si no, a través de un Relay que solo deja pasar tráfico cifrado.',
      'hero.download': 'Descargar RCynQ',
      'hero.how': 'Cómo funciona',
      'hero.platforms': 'Libre y de código abierto · Windows · Linux · macOS · aplicación y línea de comandos',
      'chain.workstation': 'aquí editas',
      'chain.relay': 'no ve nada',
      'chain.origin': 'los repositorios',
      'chain.patch': 'tus cambios, como parche firmado',
      'chain.bundle': 'ramas, como paquete Git',
      'how.eyebrow': 'Cómo funciona',
      'how.title': 'Dos botones en lugar de un commit y un push',
      'how.sub': 'Un mismo programa toma uno de tres roles en cada equipo. Tú sigues trabajando donde estás; RCynQ mueve el código.',
      'how.1.title': 'Edita donde estás',
      'how.1.text': 'Trabaja en tu editor habitual en la Workstation. Sin commits de trabajo en curso ni ramas que limpiar después.',
      'how.2.title': 'Envía los cambios',
      'how.2.text': 'Las ediciones salen como un parche: un manifiesto firmado y después cada archivo por su SHA-256.',
      'how.3.title': 'Se aplica entero',
      'how.3.text': 'El Origin lo aplica todo o nada. Un conflicto se muestra archivo por archivo: conservar, tomar, fusionar con Git o en tu propia herramienta de fusión.',
      'how.4.title': 'Tráelo de vuelta',
      'how.4.text': 'Un botón trae la rama actual como paquete Git incremental, incluso a una carpeta vacía.',
      'role.workstation': 'El equipo en el que trabajas. Guarda las copias de trabajo, envía cambios y obtiene ramas.',
      'role.relay': 'Cualquier equipo al que lleguen ambos extremos. Reenvía SSH, no tiene repositorio y no ve contenido.',
      'role.origin': 'El equipo con los repositorios privados. RCynQ solo los lee; allí solo cambia lo que tú mismo enviaste.',
      'relay.eyebrow': 'Nodos Relay',
      'relay.title': 'Tu código llega a donde tú no llegas',
      'relay.sub': 'El repositorio está en una red cerrada y la máquina del desarrollador, fuera de ella. Pon un Relay entre ellos: sirve cualquier máquina con servidor SSH a la que lleguen ambos, como una VM en la nube, un bastión, una pasarela de oficina o el equipo de un compañero.',
      'relay.1': 'Una cadena pasa por tantos Relay como necesite el camino:<br><code class="route">Workstation → Relay 1 → Relay 2 → Origin</code>',
      'relay.2': 'Un Relay permanece pasivo. La clave de un nodo en <code>authorized_keys</code> solo puede reenviar hacia los siguientes nodos del camino y no ejecuta ningún comando',
      'relay.3': 'El tráfico va cifrado de extremo a extremo: el Relay nunca ve un archivo, una rama ni la ruta de un repositorio',
      'relay.4': 'Un nodo puede tener rutas de reserva, por ejemplo dos túneles. RCynQ cambia por sí solo a otra ruta cuando la actual deja de responder',
      'hops.workstation': 'tu sistema',
      'hops.relay1': 'una VM en la nube',
      'hops.relay2': 'el bastión de la oficina',
      'hops.origin': 'la red cerrada',
      'why.eyebrow': 'Ventajas de RCynQ',
      'why.title': 'Configuración rápida, entrega fiable',
      'why.sub': 'Hecho para redes cerradas, donde el código tiene que llegar de forma segura y sencilla a la máquina con el repositorio.',
      'why.new': '¡Nuevo!',
      'why.1.title': 'Entrega flexible',
      'why.1.text': 'El trabajo en curso llega al Origin como parche, sin commits ni pushes. También puedes enviar commits, por ejemplo cuando preparas una rama de versión.',
      'why.2.title': 'Parches a mano',
      'why.2.text': 'Guarda los cambios que elijas en un archivo <code>.patch</code> corriente y aplícalo después entero o archivo por archivo. Nada pasa al índice ni a un commit.',
      'why.3.title': 'Portátil',
      'why.3.text': 'No hace falta instalar la aplicación: basta con la versión portátil. Todos los ajustes se guardan en la propia carpeta de la aplicación.',
      'why.4.title': 'Un Relay nunca ve tu código',
      'why.4.text': 'El tráfico va cifrado de extremo a extremo: un Relay lo reenvía, pero no puede leerlo. Los parches van firmados con ECDSA P-256: uno falso o repetido se rechaza, y ningún archivo acaba fuera del árbol de trabajo.',
      'why.5.title': 'El Origin sigue en tus manos',
      'why.5.text': 'RCynQ no escribe remotes, referencias ni ajustes en su repositorio ni crea commits. Allí solo cambian los archivos que enviaste o aplicaste, y la rama solo avanza si permitiste los commits.',
      'why.6.title': 'Conflictos en tu propia herramienta',
      'why.6.text': 'Git fusiona por sí solo los cambios en lugares distintos, y los archivos cuyos cambios chocan se abren uno a uno en tu herramienta de fusión habitual: VS Code, Visual Studio, JetBrains, KDiff3, Meld, WinMerge, Beyond Compare y otras. No se escribe nada hasta que cada archivo esté fusionado.',
      'why.7.title': '¿Sin servidor SSH? No pasa nada',
      'why.7.text': 'Un nodo final puede aceptar conexiones en su propio puerto TLS, verificado con un certificado fijado.',
      'why.8.title': 'Muchos repositorios',
      'why.8.text': 'Registra en el Origin todos los que necesites y cambia entre ellos desde cualquier lado, sin reiniciar.',
      'why.9.title': 'Ventana o terminal',
      'why.9.text': 'Una aplicación de escritorio para el día a día y la línea de comandos <code>rcynq</code> para los scripts.',
      'start.eyebrow': 'Primeros pasos',
      'start.title': 'De equipos vacíos al primer Pull',
      'start.sub': 'Ningún servidor que alquilarnos, ninguna cuenta, ninguna nube en medio: solo tus máquinas.',
      'start.1.title': 'Instala',
      'start.1.text': 'Pon RCynQ en la Workstation y el Origin, y en un Relay si lo necesitas, y elige el rol de cada uno.',
      'start.2.title': 'Conecta los nodos',
      'start.2.text': 'Crea una invitación en un nodo y pégala en su vecino: una línea de texto lo lleva todo, y RCynQ permite las claves por sí mismo. Donde no se pueda usar una invitación, los archivos de nodo hacen lo mismo.',
      'start.3.title': 'Pull y envío',
      'start.3.text': 'Ejecuta el diagnóstico, pulsa Pull, edita un archivo y pulsa «Enviar cambios». Listo.',
      'start.note': '<b>Paso a paso:</b> la guía de configuración recorre cada comando para Windows, Linux y macOS.',
      'dl.eyebrow': 'Descargar',
      'dl.title': 'Descarga RCynQ',
      'dl.sub': 'La misma versión en todos los nodos de una cadena.<br>En la Workstation y el Origin hace falta Git.',
      'dl.windows': 'Un instalador solo para ti, sin permisos de administrador (los ajustes y los datos se quedan dentro de la instalación), o para todos los usuarios del equipo.',
      'dl.button': 'Descargar',
      'dl.windows.other': 'o el archivo .zip portátil',
      'dl.linux': 'Un paquete para Ubuntu, Debian y sus parientes: Git y el cliente SSH se instalan con él.',
      'dl.linux.other': 'o el archivo .tar.gz portátil',
      'dl.macos': 'Un archivo portátil para Mac con Apple silicon.',
      'dl.macos.other': 'o para Mac con procesador Intel',
      'dl.version': 'versión ',
      'dl.for': 'Descargar para ',
      'foot.name': 'RCynQ es software libre bajo la licencia GPL-3.0-or-later.',
      'foot.source': 'Código',
      'foot.guide': 'Guía de configuración',
      'foot.architecture': 'Arquitectura',
      'foot.license': 'Licencia',
      'foot.releases': 'Todas las versiones',
      'foot.mail': 'Escríbenos'
    },
    fr: {
      'page.title': 'RCynQ : Remote Code Synchronization, Quick',
      'nav.language': 'Langue',
      'nav.why': 'Atouts de RCynQ',
      'nav.download': 'Télécharger',
      'nav.video': 'Tutoriels vidéo',
      'hero.title': 'Envoyez votre code sur une machine distante.<br><em>Sans commit.</em>',
      'hero.lead': 'RCynQ livre vos modifications non commitées à l\'ordinateur qui héberge le dépôt et en rapporte les branches. Directement quand les deux se voient, sinon par un Relay qui ne laisse passer que du trafic chiffré.',
      'hero.download': 'Télécharger RCynQ',
      'hero.how': 'Fonctionnement',
      'hero.platforms': 'Libre et open source · Windows · Linux · macOS · application et ligne de commande',
      'chain.workstation': 'vous travaillez ici',
      'chain.relay': 'ne voit rien',
      'chain.origin': 'les dépôts',
      'chain.patch': 'vos modifications, en patch signé',
      'chain.bundle': 'les branches, en paquet Git',
      'how.eyebrow': 'Fonctionnement',
      'how.title': 'Deux boutons au lieu d\'un commit et d\'un push',
      'how.sub': 'Un même programme prend l\'un de trois rôles sur chaque ordinateur. Vous continuez à travailler là où vous êtes ; RCynQ déplace le code.',
      'how.1.title': 'Modifiez sur place',
      'how.1.text': 'Travaillez dans votre éditeur habituel sur la Workstation. Pas de commits « WIP », pas de branches à nettoyer ensuite.',
      'how.2.title': 'Envoyez les modifications',
      'how.2.text': 'Les modifications partent en patch : un manifeste signé, puis chaque fichier selon son SHA-256.',
      'how.3.title': 'Appliqué d\'un bloc',
      'how.3.text': 'L\'Origin applique tout ou rien. Un conflit est montré fichier par fichier : garder, prendre, fusionner avec Git ou dans votre propre outil de fusion.',
      'how.4.title': 'Rapatriez',
      'how.4.text': 'Un bouton rapporte la branche actuelle en paquet Git incrémental, même dans un dossier vide.',
      'role.workstation': 'L\'ordinateur sur lequel vous travaillez. Garde les copies de travail, envoie les modifications, récupère les branches.',
      'role.relay': 'N\'importe quel ordinateur que les deux extrémités atteignent. Transfère SSH, n\'a aucun dépôt et ne voit aucun contenu.',
      'role.origin': 'L\'ordinateur qui a les dépôts privés. RCynQ ne fait que les lire ; seul ce que vous avez envoyé vous-même y change quelque chose.',
      'relay.eyebrow': 'Nœuds Relay',
      'relay.title': 'Votre code va là où vous n\'allez pas',
      'relay.sub': 'Le dépôt est dans un réseau fermé, la machine du développeur en dehors. Placez un Relay entre les deux : n\'importe quelle machine dotée d\'un serveur SSH que les deux atteignent suffit, par exemple une VM dans le cloud, un bastion, une passerelle de bureau ou l\'ordinateur d\'un collègue.',
      'relay.1': 'Une chaîne passe par autant de Relay que le chemin l\'exige :<br><code class="route">Workstation → Relay 1 → Relay 2 → Origin</code>',
      'relay.2': 'Un Relay reste passif. La clé d\'un nœud dans <code>authorized_keys</code> ne peut transférer que vers les nœuds suivants du chemin et n\'exécute aucune commande',
      'relay.3': 'Le trafic est chiffré de bout en bout : le Relay ne voit jamais un fichier, une branche ni le chemin d\'un dépôt',
      'relay.4': 'Un nœud peut avoir des routes de secours, par exemple deux tunnels. RCynQ passe de lui-même à une autre route quand la route actuelle ne répond plus',
      'hops.workstation': 'votre système',
      'hops.relay1': 'une VM dans le cloud',
      'hops.relay2': 'le bastion du bureau',
      'hops.origin': 'le réseau fermé',
      'why.eyebrow': 'Atouts de RCynQ',
      'why.title': 'Installation rapide, livraison fiable',
      'why.sub': 'Conçu pour les réseaux fermés, où le code doit atteindre la machine du dépôt simplement et en toute sécurité.',
      'why.new': 'Nouveau !',
      'why.1.title': 'Une livraison souple',
      'why.1.text': 'Le travail en cours part vers l\'Origin en patch, sans commit ni push. Les commits peuvent aussi partir, par exemple quand vous préparez une branche de version.',
      'why.2.title': 'Des patchs à la main',
      'why.2.text': 'Enregistrez les modifications choisies dans un fichier <code>.patch</code> ordinaire, puis appliquez-le en entier ou fichier par fichier. Rien ne va dans l\'index ni dans un commit.',
      'why.3.title': 'Portable',
      'why.3.text': 'Inutile d\'installer l\'application : la version portable suffit. Tous les réglages restent dans le dossier même de l\'application.',
      'why.4.title': 'Un Relay ne voit jamais votre code',
      'why.4.text': 'Le trafic est chiffré de bout en bout : un Relay le transmet sans pouvoir le lire. Les patchs sont signés en ECDSA P-256 : un patch falsifié ou rejoué est refusé, et aucun fichier ne sort de l\'arbre de travail.',
      'why.5.title': 'L\'Origin reste entre vos mains',
      'why.5.text': 'RCynQ n\'écrit ni remote, ni référence, ni réglage dans son dépôt et ne crée aucun commit. Seuls les fichiers que vous avez envoyés ou appliqués y changent, et la branche n\'avance que si vous avez autorisé les commits.',
      'why.6.title': 'Les conflits dans votre outil',
      'why.6.text': 'Git fusionne seul les modifications à des endroits différents, et les fichiers dont les modifications se heurtent s\'ouvrent un par un dans votre outil de fusion habituel : VS Code, Visual Studio, JetBrains, KDiff3, Meld, WinMerge, Beyond Compare et d\'autres. Rien n\'est écrit tant que chaque fichier n\'est pas fusionné.',
      'why.7.title': 'Pas de serveur SSH ? Aucun souci',
      'why.7.text': 'Un nœud final peut accepter les connexions sur son propre port TLS, vérifié par un certificat fixé.',
      'why.8.title': 'De nombreux dépôts',
      'why.8.text': 'Enregistrez sur l\'Origin autant de dépôts qu\'il faut et basculez entre eux depuis l\'un ou l\'autre côté, sans redémarrer.',
      'why.9.title': 'Fenêtre ou terminal',
      'why.9.text': 'Une application de bureau pour tous les jours et la ligne de commande <code>rcynq</code> pour les scripts.',
      'start.eyebrow': 'Prise en main',
      'start.title': 'Des ordinateurs vierges au premier Pull',
      'start.sub': 'Aucun serveur à nous louer, aucun compte, aucun cloud au milieu : seulement vos machines.',
      'start.1.title': 'Installez',
      'start.1.text': 'Installez RCynQ sur la Workstation et l\'Origin, et sur un Relay si besoin, puis choisissez le rôle de chacun.',
      'start.2.title': 'Reliez les nœuds',
      'start.2.text': 'Créez une invitation sur un nœud et collez-la chez son voisin : une ligne de texte transporte tout, et RCynQ autorise les clés lui-même. Là où une invitation n\'est pas possible, les fichiers de nœud font la même chose.',
      'start.3.title': 'Pull et envoi',
      'start.3.text': 'Lancez le diagnostic, cliquez sur Pull, modifiez un fichier, cliquez sur « Envoyer les modifications ». C\'est fait.',
      'start.note': '<b>Pas à pas :</b> le guide d\'installation parcourt chaque commande, pour Windows, Linux et macOS.',
      'dl.eyebrow': 'Télécharger',
      'dl.title': 'Obtenir RCynQ',
      'dl.sub': 'La même version sur chaque nœud d\'une chaîne.<br>Git est nécessaire sur la Workstation et l\'Origin.',
      'dl.windows': 'Un installateur pour vous seul, sans droits d\'administrateur (les réglages et les données restent dans l\'installation), ou pour tous les utilisateurs de l\'ordinateur.',
      'dl.button': 'Télécharger',
      'dl.windows.other': 'ou l\'archive .zip portable',
      'dl.linux': 'Un paquet pour Ubuntu, Debian et leurs cousins : Git et le client SSH viennent avec.',
      'dl.linux.other': 'ou l\'archive .tar.gz portable',
      'dl.macos': 'Une archive portable pour les Mac à puce Apple.',
      'dl.macos.other': 'ou pour les Mac à processeur Intel',
      'dl.version': 'version ',
      'dl.for': 'Télécharger pour ',
      'foot.name': 'RCynQ est un logiciel libre sous licence GPL-3.0-or-later.',
      'foot.source': 'Code',
      'foot.guide': 'Guide d\'installation',
      'foot.architecture': 'Architecture',
      'foot.license': 'Licence',
      'foot.releases': 'Toutes les versions',
      'foot.mail': 'Nous écrire'
    },
    pt: {
      'page.title': 'RCynQ: Remote Code Synchronization, Quick',
      'nav.language': 'Idioma',
      'nav.why': 'Vantagens do RCynQ',
      'nav.download': 'Transferir',
      'nav.video': 'Tutoriais em vídeo',
      'hero.title': 'Leve o seu código para uma máquina remota.<br><em>Sem commit.</em>',
      'hero.lead': 'O RCynQ entrega as suas alterações sem commit ao computador que tem o repositório e traz de volta os ramos dele. Diretamente quando os dois se veem; caso contrário, através de um Relay que só deixa passar tráfego cifrado.',
      'hero.download': 'Transferir o RCynQ',
      'hero.how': 'Como funciona',
      'hero.platforms': 'Livre e de código aberto · Windows · Linux · macOS · aplicação e linha de comandos',
      'chain.workstation': 'aqui edita',
      'chain.relay': 'não vê nada',
      'chain.origin': 'os repositórios',
      'chain.patch': 'as suas alterações, como patch assinado',
      'chain.bundle': 'ramos, como pacote Git',
      'how.eyebrow': 'Como funciona',
      'how.title': 'Dois botões em vez de um commit e um push',
      'how.sub': 'Um só programa assume um de três papéis em cada computador. Continua a trabalhar onde está; o RCynQ move o código.',
      'how.1.title': 'Edite onde está',
      'how.1.text': 'Trabalhe no seu editor habitual na Workstation. Sem commits provisórios nem ramos para limpar depois.',
      'how.2.title': 'Envie as alterações',
      'how.2.text': 'As edições seguem como patch: um manifesto assinado e depois cada ficheiro pelo seu SHA-256.',
      'how.3.title': 'Aplicado por inteiro',
      'how.3.text': 'O Origin aplica tudo ou nada. Um conflito é mostrado ficheiro a ficheiro: manter, aceitar, fundir com o Git ou na sua própria ferramenta de fusão.',
      'how.4.title': 'Traga de volta',
      'how.4.text': 'Um botão traz o ramo atual como pacote Git incremental, até para uma pasta vazia.',
      'role.workstation': 'O computador onde trabalha. Guarda as cópias de trabalho, envia alterações e obtém ramos.',
      'role.relay': 'Qualquer computador que ambas as extremidades alcancem. Reencaminha SSH, não tem repositório e não vê conteúdo.',
      'role.origin': 'O computador com os repositórios privados. O RCynQ só os lê; lá, só muda aquilo que enviou.',
      'relay.eyebrow': 'Nós Relay',
      'relay.title': 'O seu código chega aonde não consegue ir',
      'relay.sub': 'O repositório está numa rede fechada e a máquina do programador fora dela. Ponha um Relay entre eles: serve qualquer máquina com servidor SSH que ambos alcancem, como uma VM na nuvem, um bastião, um gateway do escritório ou o computador de um colega.',
      'relay.1': 'Uma cadeia passa por tantos Relay quantos o caminho precisar:<br><code class="route">Workstation → Relay 1 → Relay 2 → Origin</code>',
      'relay.2': 'Um Relay fica passivo. A chave de um nó em <code>authorized_keys</code> só pode reencaminhar para os nós seguintes do caminho e não executa nenhum comando',
      'relay.3': 'O tráfego é cifrado de ponta a ponta: o Relay nunca vê um ficheiro, um ramo nem o caminho de um repositório',
      'relay.4': 'Um nó pode ter rotas de reserva, por exemplo dois túneis. O RCynQ muda sozinho para outra rota quando a atual deixa de responder',
      'hops.workstation': 'o seu sistema',
      'hops.relay1': 'uma VM na nuvem',
      'hops.relay2': 'o bastião do escritório',
      'hops.origin': 'a rede fechada',
      'why.eyebrow': 'Vantagens do RCynQ',
      'why.title': 'Configuração rápida, entrega fiável',
      'why.sub': 'Feito para redes fechadas, onde o código tem de chegar de forma segura e simples à máquina com o repositório.',
      'why.new': 'Novidade!',
      'why.1.title': 'Entrega flexível',
      'why.1.text': 'O trabalho em curso chega ao Origin como patch, sem commits nem pushes. Também pode enviar commits, por exemplo quando prepara um ramo de versão.',
      'why.2.title': 'Patches à mão',
      'why.2.text': 'Guarde as alterações que escolher num ficheiro <code>.patch</code> comum e aplique-o depois por inteiro ou ficheiro a ficheiro. Nada vai para o índice nem para um commit.',
      'why.3.title': 'Portátil',
      'why.3.text': 'Não é preciso instalar a aplicação: basta a versão portátil. Todas as definições ficam na própria pasta da aplicação.',
      'why.4.title': 'Um Relay nunca vê o seu código',
      'why.4.text': 'O tráfego é cifrado de ponta a ponta: um Relay reencaminha-o, mas não o consegue ler. Os patches são assinados com ECDSA P-256: um falso ou repetido é recusado, e nenhum ficheiro fica fora da árvore de trabalho.',
      'why.5.title': 'O Origin fica nas suas mãos',
      'why.5.text': 'O RCynQ não escreve remotes, referências nem definições no repositório dele e não cria commits. Lá só mudam os ficheiros que enviou ou aplicou, e o ramo só avança se tiver permitido commits.',
      'why.6.title': 'Conflitos na sua ferramenta',
      'why.6.text': 'O Git funde sozinho as alterações em sítios diferentes, e os ficheiros cujas alterações colidem abrem-se um a um na sua ferramenta de fusão habitual: VS Code, Visual Studio, JetBrains, KDiff3, Meld, WinMerge, Beyond Compare e outras. Nada é escrito até cada ficheiro estar fundido.',
      'why.7.title': 'Sem servidor SSH? Não faz mal',
      'why.7.text': 'Um nó final pode aceitar ligações na sua própria porta TLS, verificada por um certificado fixado.',
      'why.8.title': 'Muitos repositórios',
      'why.8.text': 'Registe no Origin quantos precisar e mude entre eles a partir de qualquer lado, sem reiniciar.',
      'why.9.title': 'Janela ou terminal',
      'why.9.text': 'Uma aplicação de ambiente de trabalho para o dia a dia e a linha de comandos <code>rcynq</code> para scripts.',
      'start.eyebrow': 'Primeiros passos',
      'start.title': 'De computadores vazios ao primeiro Pull',
      'start.sub': 'Nenhum servidor para nos alugar, nenhuma conta, nenhuma nuvem no meio: só as suas máquinas.',
      'start.1.title': 'Instale',
      'start.1.text': 'Instale o RCynQ na Workstation e no Origin, e num Relay se precisar, e escolha o papel de cada um.',
      'start.2.title': 'Ligue os nós',
      'start.2.text': 'Crie um convite num nó e cole-o no vizinho: uma linha de texto leva tudo, e o RCynQ permite as chaves sozinho. Onde um convite não for possível, os ficheiros de nó fazem o mesmo.',
      'start.3.title': 'Pull e envio',
      'start.3.text': 'Execute o diagnóstico, prima Pull, edite um ficheiro e prima «Enviar alterações». Pronto.',
      'start.note': '<b>Passo a passo:</b> o guia de configuração percorre cada comando para Windows, Linux e macOS.',
      'dl.eyebrow': 'Transferir',
      'dl.title': 'Obtenha o RCynQ',
      'dl.sub': 'A mesma versão em todos os nós de uma cadeia.<br>Na Workstation e no Origin é preciso o Git.',
      'dl.windows': 'Um instalador só para si, sem direitos de administrador (as definições e os dados ficam dentro da instalação), ou para todos os utilizadores do computador.',
      'dl.button': 'Transferir',
      'dl.windows.other': 'ou o arquivo .zip portátil',
      'dl.linux': 'Um pacote para Ubuntu, Debian e afins: o Git e o cliente SSH vêm com ele.',
      'dl.linux.other': 'ou o arquivo .tar.gz portátil',
      'dl.macos': 'Um arquivo portátil para Mac com Apple silicon.',
      'dl.macos.other': 'ou para Mac com processador Intel',
      'dl.version': 'versão ',
      'dl.for': 'Transferir para ',
      'foot.name': 'O RCynQ é software livre sob a licença GPL-3.0-or-later.',
      'foot.source': 'Código',
      'foot.guide': 'Guia de configuração',
      'foot.architecture': 'Arquitetura',
      'foot.license': 'Licença',
      'foot.releases': 'Todas as versões',
      'foot.mail': 'Escreva-nos'
    },
    ru: {
      'page.title': 'RCynQ: Remote Code Synchronization, Quick',
      'nav.language': 'Язык',
      'nav.why': 'Преимущества RCynQ',
      'nav.download': 'Скачать',
      'nav.video': 'Видеоинструкции',
      'hero.title': 'Доставьте код на удалённую машину.<br><em>Без коммита.</em>',
      'hero.lead': 'RCynQ переносит незакоммиченные изменения на компьютер с репозиторием и приносит обратно его ветки. Если машины видят друг друга, то напрямую, а если нет — через узел Relay, который пропускает только зашифрованный трафик.',
      'hero.download': 'Скачать RCynQ',
      'hero.how': 'Как это работает',
      'hero.platforms': 'Бесплатно и с открытым кодом · Windows · Linux · macOS · приложение и командная строка',
      'chain.workstation': 'здесь вы пишете код',
      'chain.relay': 'ничего не видит',
      'chain.origin': 'репозитории',
      'chain.patch': 'ваши правки — подписанным патчем',
      'chain.bundle': 'ветки — Git-бандлом',
      'how.eyebrow': 'Как это работает',
      'how.title': 'Две кнопки вместо коммита и пуша',
      'how.sub': 'Одна программа на каждом компьютере берёт одну из трёх ролей. Вы работаете там же, где и раньше, а код переносит RCynQ.',
      'how.1.title': 'Правьте у себя',
      'how.1.text': 'Работайте в привычном редакторе на Workstation. Никаких WIP-коммитов и веток, которые потом придётся чистить.',
      'how.2.title': 'Отправьте изменения',
      'how.2.text': 'Правки уходят патчем: подписанный манифест, затем каждый файл по его SHA-256.',
      'how.3.title': 'Применяется целиком',
      'how.3.text': 'Origin применяет всё или ничего. Конфликт показан по файлам: оставить, взять, слить средствами Git или в своём инструменте слияния.',
      'how.4.title': 'Заберите обратно',
      'how.4.text': 'Одна кнопка приносит текущую ветку инкрементальным Git-бандлом, даже в пустую папку.',
      'role.workstation': 'Компьютер, на котором вы работаете. Хранит рабочие копии, отправляет изменения, забирает ветки.',
      'role.relay': 'Любой компьютер, до которого дотягиваются оба конца. Пересылает SSH, не хранит репозиториев и не видит содержимого.',
      'role.origin': 'Компьютер с закрытыми репозиториями. RCynQ их только читает; меняется там лишь то, что вы отправили сами.',
      'relay.eyebrow': 'Узлы Relay',
      'relay.title': 'Код попадает туда, куда вам не дотянуться',
      'relay.sub': 'Репозиторий — в закрытой сети, машина разработчика — за её пределами. Поставьте между ними Relay: подойдёт любая машина с SSH-сервером, до которой дотягиваются обе стороны, например облачная виртуалка, bastion-хост, офисный шлюз или компьютер коллеги.',
      'relay.1': 'В цепочке может быть сколько угодно узлов Relay:<br><code class="route">Workstation → Relay 1 → Relay 2 → Origin</code>',
      'relay.2': 'Relay остаётся пассивным. Ключу узла в <code>authorized_keys</code> разрешён проброс только к следующим узлам на пути и ни одной команды',
      'relay.3': 'Трафик зашифрован из конца в конец: Relay не видит ни файлов, ни веток, ни путей к репозиториям',
      'relay.4': 'У узла могут быть запасные маршруты, например два туннеля. RCynQ переключается автоматически на альтернативный маршрут, если текущий перестал отвечать',
      'hops.workstation': 'ваша система',
      'hops.relay1': 'облачная VM',
      'hops.relay2': 'офисный bastion',
      'hops.origin': 'закрытая сеть',
      'why.eyebrow': 'Преимущества RCynQ',
      'why.title': 'Быстрая настройка, надёжная доставка',
      'why.sub': 'Сделан для закрытых сетей, где код должен безопасно и легко дойти до машины с репозиторием.',
      'why.new': 'Новинка!',
      'why.1.title': 'Гибкая доставка',
      'why.1.text': 'Незаконченная работа уходит в Origin патчем, без коммитов и пушей. Доступна и отправка коммитов, например когда готовится релизная ветка.',
      'why.2.title': 'Патчи вручную',
      'why.2.text': 'Сохраните выбранные правки в обычный файл <code>.patch</code>, а потом примените его целиком или по отдельным файлам. Ничего не попадает ни в индекс, ни в коммит.',
      'why.3.title': 'Портативность',
      'why.3.text': 'Необязательно устанавливать приложение, достаточно использовать портативную версию. Все настройки хранятся прямо в папке с приложением.',
      'why.4.title': 'Relay не видит ваш код',
      'why.4.text': 'Трафик зашифрован из конца в конец: Relay передаёт его дальше, но прочитать не может. Патчи подписаны ECDSA P-256: поддельный или повторный будет отклонён, а файл не выйдет за пределы рабочего дерева.',
      'why.5.title': 'Origin в ваших руках',
      'why.5.text': 'RCynQ не пишет в его репозиторий ни remote, ни ссылки, ни настройки и сам не создаёт коммитов. Там меняются только файлы, которые вы отправили или применили, а ветка сдвигается вперёд, только если вы разрешили коммиты.',
      'why.6.title': 'Конфликты в вашем инструменте',
      'why.6.text': 'Git сам сливает правки в разных местах, а файлы со спорными правками по одному открываются в привычном инструменте слияния: VS Code, Visual Studio, JetBrains, KDiff3, Meld, WinMerge, Beyond Compare и других. Пока не слит каждый файл, ничего не записывается.',
      'why.7.title': 'Нет SSH-сервера? Не беда',
      'why.7.text': 'Конечный узел может принимать подключения на собственном TLS-порту с закреплённым сертификатом.',
      'why.8.title': 'Много репозиториев',
      'why.8.text': 'Зарегистрируйте на Origin сколько нужно и переключайтесь между ними с любой стороны без перезапуска.',
      'why.9.title': 'Окно или терминал',
      'why.9.text': 'Приложение для каждого дня и командная строка <code>rcynq</code> для скриптов.',
      'start.eyebrow': 'Начало работы',
      'start.title': 'От пустых компьютеров до первого Pull',
      'start.sub': 'Не нужно арендовать у нас сервер, заводить аккаунт или пускать облако в середину: только ваши машины.',
      'start.1.title': 'Установите',
      'start.1.text': 'Поставьте RCynQ на Workstation и Origin, а если нужен Relay, то и на него, и выберите роль каждого.',
      'start.2.title': 'Соедините узлы',
      'start.2.text': 'Создайте приглашение на одном узле и вставьте его на соседнем: в одной строке текста есть всё нужное, а ключи RCynQ разрешит сам. Где приглашение не подходит, то же сделают файлы узлов.',
      'start.3.title': 'Pull и отправка',
      'start.3.text': 'Запустите диагностику, нажмите Pull, поправьте файл, нажмите «Отправить изменения». Готово.',
      'start.note': '<b>Шаг за шагом:</b> руководство по настройке проводит через каждую команду для Windows, Linux и macOS.',
      'dl.eyebrow': 'Скачать',
      'dl.title': 'Скачайте RCynQ',
      'dl.sub': 'На всех узлах цепочки одна и та же версия.<br>На Workstation и Origin нужен Git.',
      'dl.windows': 'Установщик только для вас, без прав администратора (настройки и данные остаются внутри папки установки), или для всех пользователей компьютера.',
      'dl.button': 'Скачать',
      'dl.windows.other': 'или портативный архив .zip',
      'dl.linux': 'Пакет для Ubuntu, Debian и родственных систем: Git и SSH-клиент ставятся вместе с ним.',
      'dl.linux.other': 'или портативный архив .tar.gz',
      'dl.macos': 'Портативный архив для Mac на Apple silicon.',
      'dl.macos.other': 'или для Mac с процессором Intel',
      'dl.version': 'версия ',
      'dl.for': 'Скачать для ',
      'foot.name': 'RCynQ — свободная программа под лицензией GPL-3.0-or-later.',
      'foot.source': 'Исходный код',
      'foot.guide': 'Руководство по настройке',
      'foot.architecture': 'Архитектура',
      'foot.license': 'Лицензия',
      'foot.releases': 'Все выпуски',
      'foot.mail': 'Написать нам'
    }
  };

  // What the HTML says is the English table.
  var english = { 'page.title': document.title, 'nav.language': 'Language', 'dl.version': 'version ', 'dl.for': 'Download for ' };
  var nodes = document.querySelectorAll('[data-t]');
  nodes.forEach(function (node) { english[node.getAttribute('data-t')] = node.innerHTML; });
  texts.en = english;

  var language = 'en';
  var release = null;
  var names = { windows: 'Windows', linux: 'Linux', macos: 'macOS' };

  function text(key) {
    return (texts[language] && texts[language][key]) || english[key] || '';
  }

  function detectSystem() {
    var agent = navigator.userAgent || '';
    if (/Android|iPhone|iPad|iPod/i.test(agent)) { return null; }
    if (/Windows/i.test(agent)) { return 'windows'; }
    if (/Macintosh|Mac OS X/i.test(agent)) { return 'macos'; }
    if (/Linux|X11/i.test(agent)) { return 'linux'; }
    return null;
  }

  // Only https addresses of a release are put on a button; anything else in the file is ignored.
  function safeUrl(value) {
    return typeof value === 'string' && /^https:\/\/[^\s"'<>]+$/.test(value) ? value : null;
  }

  function showDownloads() {
    var mine = detectSystem();
    document.querySelectorAll('.download').forEach(function (card) {
      var system = card.getAttribute('data-os');
      var entry = release && release[system];
      var version = entry && typeof entry.version === 'string' && /^[0-9][0-9A-Za-z.\-]{0,30}$/.test(entry.version) ? entry.version : null;
      card.querySelector('.meta').textContent = version ? text('dl.version') + version : '';
      // Each link names the field of updates.json it takes: url (the installer or the archive), zip, deb or intel.
      card.querySelectorAll('a[data-field]').forEach(function (link) {
        var url = entry && safeUrl(entry[link.getAttribute('data-field')]);
        if (url) { link.setAttribute('href', url); }
      });
      card.classList.toggle('mine', system === mine);
    });

    var hero = document.getElementById('hero-download');
    var entry = mine && release && release[mine];
    if (mine) {
      hero.querySelector('span').textContent = text('dl.for') + names[mine];
      // On Linux the package of the system comes first, as on the card.
      var deb = entry && safeUrl(entry.deb);
      var url = deb || (entry && safeUrl(entry.url));
      hero.setAttribute('href', url || '#download');
      // A click is counted as a download (Umami) only when the button leads to a package, not to the list below.
      if (url) {
        hero.setAttribute('data-umami-event', 'download');
        hero.setAttribute('data-umami-event-package', mine === 'linux' ? (deb ? 'linux-deb' : 'linux-archive') : mine);
        hero.setAttribute('data-umami-event-place', 'hero');
      }
    }
  }

  var picker = document.querySelector('.lang');
  var showPicker = picker && languages ? languages.mountPicker(picker, switchTo) : function () { };

  function apply(next) {
    language = texts[next] ? next : 'en';
    document.documentElement.lang = language;
    document.title = text('page.title');
    nodes.forEach(function (node) { node.innerHTML = text(node.getAttribute('data-t')); });
    if (picker) { picker.setAttribute('aria-label', text('nav.language')); }
    if (languages) {
      document.getElementById('guide-link').setAttribute('href', languages.docUrl(language, 'SetupGuide.md'));
      document.getElementById('architecture-link').setAttribute('href', languages.docUrl(language, 'Architecture.md'));
    }
    showPicker(language);
    showDownloads();
  }

  // A chosen language fades the page out and back in, so the switch reads as one change rather than a flicker.
  function switchTo(next) {
    var body = document.body;
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      apply(next);
      return;
    }
    body.classList.add('switching');
    window.setTimeout(function () {
      apply(next);
      body.classList.remove('switching');
    }, 160);
  }

  apply(languages ? languages.detect() : 'en');

  // The latest release: the download buttons lead straight to its files.
  fetch('updates.json', { cache: 'no-cache' })
    .then(function (response) { return response.ok ? response.json() : null; })
    .then(function (json) { if (json && typeof json === 'object') { release = json; showDownloads(); } })
    .catch(function () { /* the buttons keep leading to the releases page */ });

  // The steps appear as the reader gets to them.
  var steps = document.querySelectorAll('.steps li');
  if ('IntersectionObserver' in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { entry.target.classList.add('seen'); observer.unobserve(entry.target); }
      });
    }, { threshold: 0.2 });
    steps.forEach(function (step, index) {
      step.style.transitionDelay = (index % 4) * 90 + 'ms';
      observer.observe(step);
    });
  } else {
    steps.forEach(function (step) { step.classList.add('seen'); });
  }
})();
