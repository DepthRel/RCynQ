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
      'nav.how': 'So funktioniert es',
      'nav.relay': 'Relays',
      'nav.why': 'Warum RCynQ',
      'nav.download': 'Herunterladen',
      'hero.title': 'Bringen Sie Ihren Code auf einen entfernten Rechner.<br><em>Ohne Commit.</em>',
      'hero.lead': 'RCynQ bringt Ihre nicht committeten Änderungen auf den Rechner mit dem Repository und holt dessen Branches zurück, auch wenn sich die beiden nicht direkt sehen. Direkt oder über ein Relay, das nur verschlüsselten Verkehr durchlässt.',
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
      'how.3.text': 'Origin wendet alles oder nichts an. Ein Konflikt wird Datei für Datei gezeigt: behalten, übernehmen, mit Git zusammenführen oder im eigenen Merge-Tool zusammenführen (VS Code, Visual Studio, JetBrains, KDiff3, Meld…).',
      'how.4.title': 'Zurückholen',
      'how.4.text': 'Eine Schaltfläche holt den aktuellen Branch als inkrementelles Git-Bundle, auch in einen leeren Ordner.',
      'role.workstation': 'Der Rechner, auf dem Sie arbeiten. Hält die Arbeitskopien, sendet Änderungen, holt Branches.',
      'role.relay': 'Ein beliebiger Rechner, den beide Enden erreichen. Leitet SSH weiter, hat kein Repository und sieht keinen Inhalt.',
      'role.origin': 'Der Rechner mit den privaten Repositories. RCynQ liest sie nur; dort ändern nur Ihre Patches Dateien.',
      'relay.eyebrow': 'Relay-Dienste',
      'relay.title': 'Ihr Code kommt dorthin, wo Sie nicht hinkommen',
      'relay.sub': 'Das Repository liegt in einem geschlossenen Netz, Ihr Laptop woanders. Stellen Sie ein Relay dazwischen: eine Cloud-VM, einen Bastion-Host, ein Bürogateway, den Rechner eines Kollegen.',
      'relay.1': 'Eine Kette führt über so viele Relays, wie der Weg braucht: <code>Workstation → Relay 1 → Relay 2 → Origin</code>.',
      'relay.2': 'Ein Relay bleibt passiv. Sein Schlüssel in <code>authorized_keys</code> darf einen Port weiterleiten und keinen Befehl ausführen.',
      'relay.3': 'Der Verkehr ist durchgehend verschlüsselt; das Relay sieht nie eine Datei, einen Branch oder einen Repository-Pfad.',
      'relay.4': 'Ein Knoten kann Ersatzrouten haben, zum Beispiel zwei Tunnel. RCynQ prüft sie alle 15 Sekunden und wechselt zu der, die antwortet.',
      'hops.workstation': 'Ihr Laptop',
      'hops.relay1': 'eine Cloud-VM',
      'hops.relay2': 'der Bastion-Host im Büro',
      'hops.origin': 'das geschlossene Netz',
      'why.eyebrow': 'Warum RCynQ',
      'why.title': 'Schnell eingerichtet, sorgsam mit Ihrem Code',
      'why.sub': 'Gemacht für geschlossene Netze, in denen der Code ankommen muss und sonst nichts nach außen dringen darf.',
      'why.1.title': 'Keine Commits, keine Pushes',
      'why.1.text': 'Unfertige Arbeit reist als Patch. Die Geschichte bleibt Ihre: nichts zu squashen, nichts mit --force zu pushen.',
      'why.2.title': 'Über jedes Relay',
      'why.2.text': 'Ein Sprung oder mehrere. Jeder Rechner mit SSH-Server, den beide Seiten erreichen, genügt.',
      'why.3.title': 'Nichts in der Mitte zu vertrauen',
      'why.3.text': 'Patches sind mit ECDSA P-256 signiert, werden einmal angenommen und bleiben nachweislich im Arbeitsverzeichnis.',
      'why.4.title': 'Origin bleibt sauber',
      'why.4.text': 'In sein Repository werden keine Remotes, Referenzen, Einstellungen oder Commits geschrieben. Nur Ihre Dateien ändern sich.',
      'why.5.title': 'Routen, die sich selbst heilen',
      'why.5.text': 'Mehrere Adressen für einen Knoten: die schnellste, die antwortet, wird verwendet, und die bessere kommt zurück, sobald sie wieder antwortet.',
      'why.6.title': 'Kein SSH-Server? Kein Problem',
      'why.6.text': 'Ein Endknoten kann Verbindungen auf seinem eigenen TLS-Port annehmen, geprüft durch ein festgelegtes Zertifikat.',
      'why.7.title': 'Viele Repositories',
      'why.7.text': 'Registrieren Sie auf Origin so viele wie nötig und wechseln Sie von beiden Seiten aus ohne Neustart.',
      'why.8.title': 'Portabel',
      'why.8.text': 'Einstellungen, Schlüssel und Daten bleiben im Programmordner. Nichts wird im System verstreut.',
      'why.9.title': 'Fenster oder Terminal',
      'why.9.text': 'Eine Desktop-Anwendung für jeden Tag und die Befehlszeile <code>rcynq</code> für Skripte.',
      'start.eyebrow': 'Erste Schritte',
      'start.title': 'Von drei leeren Rechnern bis zum ersten Pull',
      'start.sub': 'Kein Server bei uns zu mieten, kein Konto, keine Cloud in der Mitte: nur Ihre Rechner.',
      'start.1.title': 'Installieren',
      'start.1.text': 'Installieren Sie RCynQ auf Workstation, Origin und Relay und wählen Sie die Rolle jedes Rechners.',
      'start.2.title': 'Knoten bekannt machen',
      'start.2.text': 'Exportieren Sie auf jedem Rechner eine Knotendatei und importieren Sie sie bei den Nachbarn. RCynQ erlaubt die Schlüssel selbst.',
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
      'nav.how': 'Cómo funciona',
      'nav.relay': 'Relay',
      'nav.why': 'Por qué RCynQ',
      'nav.download': 'Descargar',
      'hero.title': 'Lleva tu código a una máquina remota.<br><em>Sin un commit.</em>',
      'hero.lead': 'RCynQ lleva tus cambios sin commit al equipo que tiene el repositorio y trae de vuelta sus ramas, aunque los dos no se vean directamente. Directamente o a través de un Relay que solo deja pasar tráfico cifrado.',
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
      'how.3.text': 'El Origin lo aplica todo o nada. Un conflicto se muestra archivo por archivo: conservar, tomar, fusionar con Git o fusionar en tu propia herramienta de fusión (VS Code, Visual Studio, JetBrains, KDiff3, Meld…).',
      'how.4.title': 'Tráelo de vuelta',
      'how.4.text': 'Un botón trae la rama actual como paquete Git incremental, incluso a una carpeta vacía.',
      'role.workstation': 'El equipo en el que trabajas. Guarda las copias de trabajo, envía cambios y obtiene ramas.',
      'role.relay': 'Cualquier equipo al que lleguen ambos extremos. Reenvía SSH, no tiene repositorio y no ve contenido.',
      'role.origin': 'El equipo con los repositorios privados. RCynQ solo los lee; allí solo tus parches cambian archivos.',
      'relay.eyebrow': 'Servicios Relay',
      'relay.title': 'Tu código llega a donde tú no llegas',
      'relay.sub': 'El repositorio está en una red cerrada y tu portátil en otro sitio. Pon un Relay entre ellos: una VM en la nube, un bastión, una pasarela de oficina, el equipo de un compañero.',
      'relay.1': 'Una cadena pasa por tantos Relay como necesite el camino: <code>Workstation → Relay 1 → Relay 2 → Origin</code>.',
      'relay.2': 'Un Relay permanece pasivo. Su clave en <code>authorized_keys</code> puede reenviar un puerto y no ejecutar ningún comando.',
      'relay.3': 'El tráfico va cifrado de extremo a extremo; el Relay nunca ve un archivo, una rama ni la ruta de un repositorio.',
      'relay.4': 'Un nodo puede tener rutas de reserva, por ejemplo dos túneles. RCynQ las comprueba cada 15 segundos y pasa a la que responde.',
      'hops.workstation': 'tu portátil',
      'hops.relay1': 'una VM en la nube',
      'hops.relay2': 'el bastión de la oficina',
      'hops.origin': 'la red cerrada',
      'why.eyebrow': 'Por qué RCynQ',
      'why.title': 'Rápido de configurar, cuidadoso con tu código',
      'why.sub': 'Hecho para redes cerradas, donde el código tiene que llegar y nada más puede filtrarse.',
      'why.1.title': 'Sin commits ni pushes',
      'why.1.text': 'El trabajo en curso viaja como parche. El historial sigue siendo tuyo: nada que aplastar, nada que forzar con push.',
      'why.2.title': 'A través de cualquier Relay',
      'why.2.text': 'Un salto o varios. Basta cualquier máquina con servidor SSH a la que lleguen ambos lados.',
      'why.3.title': 'Nada en lo que confiar en medio',
      'why.3.text': 'Los parches van firmados con ECDSA P-256, se aceptan una vez y se comprueba que no salgan del árbol de trabajo.',
      'why.4.title': 'El Origin queda limpio',
      'why.4.text': 'En su repositorio no se escriben remotes, referencias, ajustes ni commits. Solo cambian tus archivos.',
      'why.5.title': 'Rutas que se reparan solas',
      'why.5.text': 'Varias direcciones para un nodo: se usa la más rápida que responde, y la mejor vuelve en cuanto responde de nuevo.',
      'why.6.title': '¿Sin servidor SSH? No pasa nada',
      'why.6.text': 'Un nodo final puede aceptar conexiones en su propio puerto TLS, verificado con un certificado fijado.',
      'why.7.title': 'Muchos repositorios',
      'why.7.text': 'Registra en el Origin todos los que necesites y cambia entre ellos desde cualquier lado, sin reiniciar.',
      'why.8.title': 'Portátil',
      'why.8.text': 'Los ajustes, las claves y los datos se quedan en la carpeta del programa. Nada queda disperso por el sistema.',
      'why.9.title': 'Ventana o terminal',
      'why.9.text': 'Una aplicación de escritorio para el día a día y la línea de comandos <code>rcynq</code> para los scripts.',
      'start.eyebrow': 'Primeros pasos',
      'start.title': 'De tres equipos vacíos al primer Pull',
      'start.sub': 'Ningún servidor que alquilarnos, ninguna cuenta, ninguna nube en medio: solo tus máquinas.',
      'start.1.title': 'Instala',
      'start.1.text': 'Pon RCynQ en la Workstation, el Origin y el Relay, y elige el rol de cada uno.',
      'start.2.title': 'Presenta los nodos',
      'start.2.text': 'Exporta un archivo de nodo en cada equipo e impórtalo en sus vecinos. RCynQ permite las claves por sí mismo.',
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
      'nav.how': 'Fonctionnement',
      'nav.relay': 'Relay',
      'nav.why': 'Pourquoi RCynQ',
      'nav.download': 'Télécharger',
      'hero.title': 'Envoyez votre code sur une machine distante.<br><em>Sans commit.</em>',
      'hero.lead': 'RCynQ livre vos modifications non commitées à l\'ordinateur qui héberge le dépôt et en rapporte les branches, même quand les deux ne se voient pas. Directement, ou par un Relay qui ne laisse passer que du trafic chiffré.',
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
      'how.3.text': 'L\'Origin applique tout ou rien. Un conflit est montré fichier par fichier : garder, prendre, fusionner avec Git ou fusionner dans votre propre outil de fusion (VS Code, Visual Studio, JetBrains, KDiff3, Meld…).',
      'how.4.title': 'Rapatriez',
      'how.4.text': 'Un bouton rapporte la branche actuelle en paquet Git incrémental, même dans un dossier vide.',
      'role.workstation': 'L\'ordinateur sur lequel vous travaillez. Garde les copies de travail, envoie les modifications, récupère les branches.',
      'role.relay': 'N\'importe quel ordinateur que les deux extrémités atteignent. Transfère SSH, n\'a aucun dépôt et ne voit aucun contenu.',
      'role.origin': 'L\'ordinateur qui a les dépôts privés. RCynQ ne fait que les lire ; seuls vos patchs y modifient des fichiers.',
      'relay.eyebrow': 'Services Relay',
      'relay.title': 'Votre code va là où vous n\'allez pas',
      'relay.sub': 'Le dépôt est dans un réseau fermé, votre portable ailleurs. Placez un Relay entre les deux : une VM dans le cloud, un bastion, une passerelle de bureau, la machine d\'un collègue.',
      'relay.1': 'Une chaîne passe par autant de Relay que le chemin l\'exige : <code>Workstation → Relay 1 → Relay 2 → Origin</code>.',
      'relay.2': 'Un Relay reste passif. Sa clé dans <code>authorized_keys</code> peut transférer un port et n\'exécuter aucune commande.',
      'relay.3': 'Le trafic est chiffré de bout en bout ; le Relay ne voit jamais un fichier, une branche ni le chemin d\'un dépôt.',
      'relay.4': 'Un nœud peut avoir des routes de secours, par exemple deux tunnels. RCynQ les vérifie toutes les 15 secondes et passe à celle qui répond.',
      'hops.workstation': 'votre portable',
      'hops.relay1': 'une VM dans le cloud',
      'hops.relay2': 'le bastion du bureau',
      'hops.origin': 'le réseau fermé',
      'why.eyebrow': 'Pourquoi RCynQ',
      'why.title': 'Rapide à installer, soigneux avec votre code',
      'why.sub': 'Conçu pour les réseaux fermés, où le code doit arriver et rien d\'autre ne doit fuir.',
      'why.1.title': 'Ni commits ni pushs',
      'why.1.text': 'Le travail en cours voyage en patch. L\'historique reste le vôtre : rien à écraser, rien à pousser en force.',
      'why.2.title': 'Par n\'importe quel Relay',
      'why.2.text': 'Un saut ou plusieurs. N\'importe quelle machine dotée d\'un serveur SSH que les deux côtés atteignent suffit.',
      'why.3.title': 'Rien à croire au milieu',
      'why.3.text': 'Les patchs sont signés en ECDSA P-256, acceptés une seule fois et vérifiés pour rester dans l\'arbre de travail.',
      'why.4.title': 'L\'Origin reste propre',
      'why.4.text': 'Aucun remote, référence, réglage ni commit n\'est écrit dans son dépôt. Seuls vos fichiers changent.',
      'why.5.title': 'Des routes qui se réparent',
      'why.5.text': 'Plusieurs adresses pour un nœud : la plus rapide qui répond est utilisée, et la meilleure revient dès qu\'elle répond de nouveau.',
      'why.6.title': 'Pas de serveur SSH ? Aucun souci',
      'why.6.text': 'Un nœud final peut accepter les connexions sur son propre port TLS, vérifié par un certificat fixé.',
      'why.7.title': 'De nombreux dépôts',
      'why.7.text': 'Enregistrez sur l\'Origin autant de dépôts qu\'il faut et basculez entre eux depuis l\'un ou l\'autre côté, sans redémarrer.',
      'why.8.title': 'Portable',
      'why.8.text': 'Les réglages, les clés et les données restent dans le dossier du programme. Rien n\'est dispersé dans le système.',
      'why.9.title': 'Fenêtre ou terminal',
      'why.9.text': 'Une application de bureau pour tous les jours et la ligne de commande <code>rcynq</code> pour les scripts.',
      'start.eyebrow': 'Prise en main',
      'start.title': 'De trois ordinateurs vierges au premier Pull',
      'start.sub': 'Aucun serveur à nous louer, aucun compte, aucun cloud au milieu : seulement vos machines.',
      'start.1.title': 'Installez',
      'start.1.text': 'Installez RCynQ sur la Workstation, l\'Origin et le Relay, et choisissez le rôle de chacun.',
      'start.2.title': 'Présentez les nœuds',
      'start.2.text': 'Exportez un fichier de nœud sur chaque ordinateur et importez-le chez ses voisins. RCynQ autorise les clés lui-même.',
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
      'nav.how': 'Como funciona',
      'nav.relay': 'Relay',
      'nav.why': 'Porquê o RCynQ',
      'nav.download': 'Transferir',
      'hero.title': 'Leve o seu código para uma máquina remota.<br><em>Sem commit.</em>',
      'hero.lead': 'O RCynQ entrega as suas alterações sem commit ao computador que tem o repositório e traz de volta os ramos dele, mesmo quando os dois não se veem. Diretamente ou através de um Relay que só deixa passar tráfego cifrado.',
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
      'how.3.text': 'O Origin aplica tudo ou nada. Um conflito é mostrado ficheiro a ficheiro: manter, aceitar, fundir com o Git ou fundir na sua própria ferramenta de fusão (VS Code, Visual Studio, JetBrains, KDiff3, Meld…).',
      'how.4.title': 'Traga de volta',
      'how.4.text': 'Um botão traz o ramo atual como pacote Git incremental, até para uma pasta vazia.',
      'role.workstation': 'O computador onde trabalha. Guarda as cópias de trabalho, envia alterações e obtém ramos.',
      'role.relay': 'Qualquer computador que ambas as extremidades alcancem. Reencaminha SSH, não tem repositório e não vê conteúdo.',
      'role.origin': 'O computador com os repositórios privados. O RCynQ só os lê; lá, só os seus patches alteram ficheiros.',
      'relay.eyebrow': 'Serviços Relay',
      'relay.title': 'O seu código chega aonde não consegue ir',
      'relay.sub': 'O repositório está numa rede fechada e o seu portátil noutro lado. Ponha um Relay entre eles: uma VM na nuvem, um bastião, um gateway do escritório, a máquina de um colega.',
      'relay.1': 'Uma cadeia passa por tantos Relay quantos o caminho precisar: <code>Workstation → Relay 1 → Relay 2 → Origin</code>.',
      'relay.2': 'Um Relay fica passivo. A sua chave em <code>authorized_keys</code> pode reencaminhar uma porta e não executar nenhum comando.',
      'relay.3': 'O tráfego é cifrado de ponta a ponta; o Relay nunca vê um ficheiro, um ramo nem o caminho de um repositório.',
      'relay.4': 'Um nó pode ter rotas de reserva, por exemplo dois túneis. O RCynQ verifica-as a cada 15 segundos e passa para a que responde.',
      'hops.workstation': 'o seu portátil',
      'hops.relay1': 'uma VM na nuvem',
      'hops.relay2': 'o bastião do escritório',
      'hops.origin': 'a rede fechada',
      'why.eyebrow': 'Porquê o RCynQ',
      'why.title': 'Rápido de configurar, cuidadoso com o seu código',
      'why.sub': 'Feito para redes fechadas, onde o código tem de chegar e mais nada pode sair.',
      'why.1.title': 'Sem commits nem pushes',
      'why.1.text': 'O trabalho em curso viaja como patch. O histórico continua a ser seu: nada para esmagar, nada para forçar com push.',
      'why.2.title': 'Através de qualquer Relay',
      'why.2.text': 'Um salto ou vários. Basta qualquer máquina com servidor SSH que ambos os lados alcancem.',
      'why.3.title': 'Nada em que confiar no meio',
      'why.3.text': 'Os patches são assinados com ECDSA P-256, aceites uma vez e verificados para não sair da árvore de trabalho.',
      'why.4.title': 'O Origin fica limpo',
      'why.4.text': 'No seu repositório não se escrevem remotes, referências, definições nem commits. Só mudam os seus ficheiros.',
      'why.5.title': 'Rotas que se recuperam sozinhas',
      'why.5.text': 'Vários endereços para um nó: usa-se o mais rápido que responde, e o melhor volta assim que responde de novo.',
      'why.6.title': 'Sem servidor SSH? Não faz mal',
      'why.6.text': 'Um nó final pode aceitar ligações na sua própria porta TLS, verificada por um certificado fixado.',
      'why.7.title': 'Muitos repositórios',
      'why.7.text': 'Registe no Origin quantos precisar e mude entre eles a partir de qualquer lado, sem reiniciar.',
      'why.8.title': 'Portátil',
      'why.8.text': 'As definições, as chaves e os dados ficam na pasta do programa. Nada fica espalhado pelo sistema.',
      'why.9.title': 'Janela ou terminal',
      'why.9.text': 'Uma aplicação de ambiente de trabalho para o dia a dia e a linha de comandos <code>rcynq</code> para scripts.',
      'start.eyebrow': 'Primeiros passos',
      'start.title': 'De três computadores vazios ao primeiro Pull',
      'start.sub': 'Nenhum servidor para nos alugar, nenhuma conta, nenhuma nuvem no meio: só as suas máquinas.',
      'start.1.title': 'Instale',
      'start.1.text': 'Instale o RCynQ na Workstation, no Origin e no Relay, e escolha o papel de cada um.',
      'start.2.title': 'Apresente os nós',
      'start.2.text': 'Exporte um ficheiro de nó em cada computador e importe-o nos vizinhos. O RCynQ permite as chaves sozinho.',
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
      'nav.how': 'Как это работает',
      'nav.relay': 'Relay',
      'nav.why': 'Почему RCynQ',
      'nav.download': 'Скачать',
      'hero.title': 'Доставьте код на удалённую машину.<br><em>Без коммита.</em>',
      'hero.lead': 'RCynQ переносит незакоммиченные изменения на компьютер с репозиторием и приносит обратно его ветки, даже если напрямую машины друг друга не видят. Напрямую или через узел Relay, который пропускает только зашифрованный трафик.',
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
      'how.sub': 'Одна программа на каждом компьютере берёт одну из трёх ролей. Вы работаете там, где вы есть, а код переносит RCynQ.',
      'how.1.title': 'Правьте у себя',
      'how.1.text': 'Работайте в привычном редакторе на Workstation. Никаких WIP-коммитов и веток, которые потом придётся чистить.',
      'how.2.title': 'Отправьте изменения',
      'how.2.text': 'Правки уходят патчем: подписанный манифест, затем каждый файл по его SHA-256.',
      'how.3.title': 'Применяется целиком',
      'how.3.text': 'Origin применяет всё или ничего. Конфликт показан по файлам: оставить, взять, слить средствами Git или в своём инструменте слияния (VS Code, Visual Studio, JetBrains, KDiff3, Meld…).',
      'how.4.title': 'Заберите обратно',
      'how.4.text': 'Одна кнопка приносит текущую ветку инкрементальным Git-бандлом, даже в пустую папку.',
      'role.workstation': 'Компьютер, на котором вы работаете. Хранит рабочие копии, отправляет изменения, забирает ветки.',
      'role.relay': 'Любой компьютер, до которого дотягиваются оба конца. Пересылает SSH, не хранит репозиториев и не видит содержимого.',
      'role.origin': 'Компьютер с закрытыми репозиториями. RCynQ их только читает; файлы там меняют лишь ваши патчи.',
      'relay.eyebrow': 'Relay-сервисы',
      'relay.title': 'Код попадает туда, куда вам не дотянуться',
      'relay.sub': 'Репозиторий в закрытой сети, ноутбук — где-то ещё. Поставьте между ними Relay: облачную виртуалку, bastion-хост, офисный шлюз, машину коллеги.',
      'relay.1': 'В цепочке может быть сколько угодно узлов Relay: <code>Workstation → Relay 1 → Relay 2 → Origin</code>.',
      'relay.2': 'Relay остаётся пассивным. Его ключу в <code>authorized_keys</code> разрешён проброс одного порта и ни одной команды.',
      'relay.3': 'Трафик зашифрован из конца в конец; Relay не видит ни файлов, ни веток, ни путей к репозиториям.',
      'relay.4': 'У узла могут быть запасные маршруты, например два туннеля. RCynQ проверяет их каждые 15 секунд и уходит на тот, что отвечает.',
      'hops.workstation': 'ваш ноутбук',
      'hops.relay1': 'облачная VM',
      'hops.relay2': 'офисный bastion',
      'hops.origin': 'закрытая сеть',
      'why.eyebrow': 'Почему RCynQ',
      'why.title': 'Быстро настроить, бережно с кодом',
      'why.sub': 'Сделан для закрытых сетей, где код должен дойти, а больше ничего утечь не должно.',
      'why.1.title': 'Без коммитов и пушей',
      'why.1.text': 'Незаконченная работа едет патчем. История остаётся вашей: ничего не нужно сквашивать и пушить с --force.',
      'why.2.title': 'Через любой Relay',
      'why.2.text': 'Один переход или несколько. Достаточно любой машины с SSH-сервером, до которой дотягиваются обе стороны.',
      'why.3.title': 'Посреднику не нужно доверять',
      'why.3.text': 'Патчи подписаны ECDSA P-256, принимаются один раз и проверяются на выход за пределы рабочего дерева.',
      'why.4.title': 'Origin остаётся чистым',
      'why.4.text': 'В его репозиторий не пишутся ни remote, ни ссылки, ни настройки, ни коммиты. Меняются только ваши файлы.',
      'why.5.title': 'Маршруты, которые чинятся сами',
      'why.5.text': 'Несколько адресов у одного узла: используется самый быстрый из отвечающих, и узел возвращается на лучший, когда тот оживает.',
      'why.6.title': 'Нет SSH-сервера? Не беда',
      'why.6.text': 'Конечный узел может принимать подключения на собственном TLS-порту с закреплённым сертификатом.',
      'why.7.title': 'Много репозиториев',
      'why.7.text': 'Зарегистрируйте на Origin сколько нужно и переключайтесь между ними с любой стороны без перезапуска.',
      'why.8.title': 'Портативность',
      'why.8.text': 'Настройки, ключи и данные лежат в папке программы. Ничего не разбросано по системе.',
      'why.9.title': 'Окно или терминал',
      'why.9.text': 'Приложение с окном на каждый день и командная строка <code>rcynq</code> для скриптов.',
      'start.eyebrow': 'Начало работы',
      'start.title': 'От трёх пустых компьютеров до первого Pull',
      'start.sub': 'Не нужно арендовать у нас сервер, заводить аккаунт или пускать облако в середину: только ваши машины.',
      'start.1.title': 'Установите',
      'start.1.text': 'Поставьте RCynQ на Workstation, Origin и Relay и выберите роль каждого.',
      'start.2.title': 'Познакомьте узлы',
      'start.2.text': 'Экспортируйте файл узла на каждом компьютере и импортируйте его у соседей. Ключи RCynQ разрешит сам.',
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
