# Arquitectura de RCynQ

[English](../En/Architecture.md) · [Deutsch](../De/Architecture.md) · **Español** · [Français](../Fr/Architecture.md) · [Português](../Pt/Architecture.md) · [Русский](../Ru/Architecture.md)

## Nodos

Un mismo programa cumple tres roles; el rol se elige en la configuración.

| Rol | Qué es | Qué hace |
|---|---|---|
| **Workstation** | El equipo en el que trabajas | Guarda las copias de trabajo, obtiene ramas, envía y recibe parches |
| **Origin** | El equipo con los repositorios privados | Ofrece sus repositorios, recibe y envía parches |
| **Relay** | Un equipo al que llegan ambos extremos | Reenvía conexiones SSH. No tiene repositorio ni ve contenido |

Una cadena es `Workstation → Relay(s) → Origin`. Sin Relay, los dos extremos se conectan directamente.

## Regla: el repositorio del Origin solo se lee

- RCynQ nunca hace push ni crea en un repositorio Git de un Origin commits, remotes, ajustes o referencias propios, ni actualiza su índice (Git se ejecuta con `GIT_OPTIONAL_LOCKS=0`).
- El árbol de trabajo y la rama activa del Origin solo cambian por lo que envió el usuario: los archivos de un parche o, si el Origin acepta commits, los propios commits del usuario, hasta los que la rama avanza (fast-forward). El reflog de la rama guarda ese avance, así que Git lo deshace sin más.
- Todo lo que guarda el Origin (la lista de repositorios, la base de sincronización, el historial, las copias de seguridad, los commits que esperan a aplicarse) está en `syncbridge.json` y en la carpeta de datos del nodo.
- El mantenimiento de Git (crear el repositorio en una carpeta vacía, el remote `rcynq`, `refs/remotes/rcynq/*`, el avance rápido en Pull) ocurre solo en la copia de trabajo de la Workstation.

Unas pruebas de extremo a extremo comparan cada archivo bajo `.git` antes y después de registrar, cambiar, servir e inspeccionar un repositorio, y antes y después de una entrega de commits que se rechaza o tiene que esperar.

## Canales

Ambos canales usan la misma conexión y la misma autorización.

**Canal Git (Origin → Workstation).** La Workstation indica un repositorio y los commits que ya tiene. El Origin responde con un paquete Git incremental. La Workstation lo descarga en `refs/remotes/rcynq/*` y avanza rápidamente. Nunca se hace push de nada. Después, el Origin enumera todas sus ramas y `refs/remotes/rcynq/*` se ajusta a esa lista: un paquete solo lleva ramas con commits nuevos, así que una rama creada en un commit que la Workstation ya tiene, una rama movida hacia atrás y una rama eliminada llegan por esta vía. Un Pull hace que la rama actual siga a `rcynq/<rama>`, para que Git la compare con el Origin y no con un remote que este equipo no alcanza.

**Canal de parches (en ambos sentidos).** Un parche es la diferencia entre el árbol de trabajo y la *base de sincronización*, es decir, lo que se sabe que tiene el otro nodo. La base de sincronización se construye automáticamente a partir del commit de Git que comparten ambos nodos, más los archivos ya intercambiados. Entrega: un manifiesto firmado, después cada archivo por su SHA-256 y después un commit que lo aplica todo o nada. Un conflicto se resuelve conservando el archivo local, tomando el entrante (con copia de seguridad) o con una fusión de Git a tres bandas.

**Entrega de commits (Workstation → Origin).** Los commits preparados en la Workstation (cherry-picks, fusiones) llegan al Origin tal cual: los mismos hashes, autores y orden. La Workstation envía un bloque de sus commits sin enviar que empieza por el más antiguo, como un paquete Git con un sobre firmado; lo lleva la sesión del parche. El Origin lo acepta solo si el usuario activó **Aceptar commits de la Workstation**, solo para la rama activa y solo si su `HEAD` es el commit sobre el que se construyó el bloque; si no, lo rechaza en el acto. Los objetos se desempaquetan en una cuarentena junto al repositorio y se comparan con la lista firmada (la cadena de primeros padres, cada ruta que toca un commit); solo entonces se mueven los packs a `.git` y la rama avanza. Si el avance falla, los packs se vuelven a sacar. Mientras el árbol de trabajo del Origin tenga cambios, los commits esperan: el usuario los aplica o rechaza allí, y entretanto se rechazan los parches de archivos. Después de aplicarlos, un Fetch pone la Workstation al día con el Origin. Mientras una rama parte justo del commit del Origin, sus commits no son cambios por enviar: el canal de parches ofrece solo lo que no está confirmado, siempre frente a la versión que tiene el Origin. Una Workstation deshace commits sin enviar (Soft, Mixed, Hard) solo si su árbol de trabajo no tiene cambios.

## Cómo acepta conexiones un nodo

| Modo | Camino | El par se verifica por |
|---|---|---|
| Servidor SSH | `ssh -L` hacia el puerto loopback del nodo, a través de los Relay como hosts de salto | Clave de host SSH fijada |
| Puerto TLS propio (por defecto `18444`) | Directamente al puerto, o por un reenvío SSH que termina en el último Relay | Huella del certificado fijada |

Un Relay usa siempre su servidor SSH. Las claves de `authorized_keys` están limitadas a un reenvío de puerto y no pueden ejecutar comandos.

La API local de un nodo escucha solo en loopback (por defecto `18443`) y exige un token guardado en la carpeta de datos.

## Rutas hacia un nodo

Un nodo puede ser accesible en varias direcciones: por ejemplo, a través de un túnel sobre UDP y de un segundo túnel sobre TCP. Cada una de esas direcciones es una *ruta* del enlace. Las claves y las huellas del nodo son las mismas para todas las rutas; solo cambia la dirección.

- **Almacenamiento.** Las rutas se guardan en el enlace. La dirección del nodo en el enlace es siempre la de la ruta en uso, así que los parches, el canal Git, el ping y el diagnóstico toman la ruta elegida sin saber nada de rutas.
- **Comprobación.** Cada 15 segundos, y de inmediato tras un ping fallido, el nodo comprueba cada ruta. Un vecino se comprueba con una conexión TCP. Un nodo tras varios Relay se comprueba con un reenvío SSH que termina en la dirección de la ruta: el banner de un servidor SSH o un saludo TLS cuentan como respuesta.
- **Elección.** Gana la ruta que responde con la prioridad más baja; entre iguales, la más rápida, si es claramente más rápida que la ruta en uso. Cuando una ruta mejor vuelve a responder, el nodo regresa a ella. Mientras ninguna responde, el nodo se queda donde está.
- **Elección manual.** Una ruta fijada se usa responda o no, hasta que la elección se devuelva al nodo.

Un enlace con una sola dirección no tiene rutas: ni se comprueba ni se cambia, y el nodo funciona como antes.

El camino a través de varios Relay lo define el extremo: los enumera en orden y se los pasa él mismo a SSH. Un Relay permanece pasivo; solo deja que las claves de los extremos reenvíen hacia los nodos del rol opuesto y hacia los servidores SSH de los Relay vecinos, en cada dirección por la que los conoce.

## Invitaciones

Un nodo invita a un vecino con un ticket de un solo uso: un secreto aleatorio, del que el nodo solo guarda el SHA-256, un rol y una caducidad. La invitación lleva la tarjeta del nodo, las tarjetas de los Relays del camino, el ticket, el token de enlace para la contraparte y el ticket del Relay cuando el nodo tiene uno.

- **Por el servidor SSH.** El ticket viene con una clave Ed25519 de un solo uso. Su línea en `authorized_keys` es `restrict,expiry-time=…,command="rcynq enroll --config …"`: no ejecuta nada más que la unión y el servidor la rechaza tras la caducidad. El nodo que se une entra con ella, a través de los Relays como saltos, y envía su tarjeta como comando; `rcynq enroll` la pasa al servicio por loopback. La línea se quita en cuanto el ticket se usa, se revoca o caduca.
- **Por el puerto TLS propio.** La unión se envía a `/peer/enroll` de ese puerto; se fija la huella del certificado de la invitación.

El nodo que invita comprueba el ticket, lo consume en un solo paso, importa la tarjeta y permite la clave. Un Relay responde a un extremo con un ticket para el extremo opuesto, que el extremo pasa en su propia invitación. La clave de host o el certificado de cada nodo se fija desde la invitación antes de enviar nada. La unión automática pasa como mucho por un Relay; un camino más largo necesita archivos.

## Seguridad del intercambio

| Protección | Mecanismo |
|---|---|
| Quién puede enviar | Token de enlace compartido y un identificador de par conocido en cada solicitud |
| Integridad y procedencia | Firma ECDSA P-256 sobre el manifiesto, incluido el nombre del repositorio |
| Repetición | Se comprueban el identificador y la antigüedad del parche; un parche se acepta una sola vez |
| Salida de rutas | Las rutas de un parche son canónicas y deben quedar dentro del árbol de trabajo |
| Repositorio equivocado | Se rechaza un parche para un repositorio con el que el receptor no trabaja |
| Exposición del Origin | Un vecino solo conoce nombres de repositorios, nunca rutas; un paquete solo se sirve para un nombre registrado |
| Commits | Desactivado hasta que el usuario los permita en el Origin; la rama, la base, cada commit y el hash del paquete van firmados; los objetos quedan en cuarentena hasta comprobarse; solo un avance (fast-forward) de la rama activa |
| Unión | Ticket de un solo uso con caducidad (se guarda el hash); clave SSH de un solo uso limitada al comando de unión, con `expiry-time`; clave de host y certificado fijados desde la invitación |

## Repositorios

- El Origin mantiene un catálogo: nombre → carpeta. Un nombre tiene de 1 a 64 caracteres de `A–Z a–z 0–9 . _ -`.
- La Workstation mantiene nombre → carpeta local y solo puede añadir nombres que ofrezca el Origin.
- Cada nodo trabaja con un repositorio a la vez. Cambiar no necesita reiniciar: espera a que termine el envío, la recepción o el Pull en curso y después cambia el árbol de trabajo para todo a la vez.
- Cada ping lleva, en ambos sentidos, el nombre del repositorio con el que trabaja el nodo. Un nodo que se entera de que el otro cambió se lo dice al usuario y no lo sigue por su cuenta.
- El nodo mantiene una base de sincronización por repositorio. El historial de parches se muestra para el repositorio actual.

## Almacenamiento

```
<carpeta del programa>/
  config/syncbridge.json     ajustes del nodo, enlaces, repositorios
  data/
    rcynq.db                 SQLite: pares, enlaces, parches, historial, secretos cifrados (clave de firma)
    keys/                    clave SSH, certificado TLS, token de la API, known_hosts
    patches/  backups/       contenido de los parches y archivos sustituidos por un parche
    snapshots/               base de sincronización (una carpeta por cada repositorio adicional)
    logs/
```

Un archivo de configuración escrito por una versión anterior se convierte al iniciar; el original se conserva como `syncbridge.json.bak`.

## Proyectos

| Proyecto | Propósito |
|---|---|
| `RCynQ.Core` | Contratos, configuración, registros de la API |
| `RCynQ.Localization` | Textos, un archivo JSON por idioma |
| `RCynQ.Core.Data`, `…Migrations`, `…Migrations.SQLite` | Almacenamiento SQLite y migraciones del esquema |
| `RCynQ.Git` | Comandos de Git, manifiestos, construcción de parches |
| `RCynQ.Ssh` | Comandos SSH, reenvíos de puertos, gestión de claves y claves de host |
| `RCynQ.Transports.*` | Enlaces con los vecinos y su estado |
| `RCynQ.Sync` | Parches, base de sincronización, canal Git, repositorios, diagnóstico |
| `RCynQ.Service` | API HTTP local y eventos SignalR |
| `RCynQ.UI` | Aplicación de escritorio (Avalonia); inicia el servicio |
| `RCynQ.Cli` | Línea de comandos `rcynq` |
| `RCynQ.Dbs.SQLite.DbMigrator` | Migrador de la base de datos de consola |
| `RCynQ.Pages` | Página estática del proyecto para GitHub Pages (sin compilación) |

Las pruebas están en `Tests/<Proyecto>.Tests`; `RCynQ.Conventions.Tests` comprueba las reglas de código de `AGENTS.md`.
Cómo compilar, probar y empaquetar: [Desarrollo](Development.md).
