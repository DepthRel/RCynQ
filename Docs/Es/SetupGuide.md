# Guía de configuración de RCynQ

[English](../En/SetupGuide.md) · [Deutsch](../De/SetupGuide.md) · **Español** · [Français](../Fr/SetupGuide.md) · [Português](../Pt/SetupGuide.md) · [Русский](../Ru/SetupGuide.md)

Participan tres equipos:

| Nodo | Equipo |
|---|---|
| **Workstation** | donde editas el proyecto |
| **Origin** | donde están los repositorios privados |
| **Relay** | uno al que llegan los otros dos |

Sigue los pasos en orden. Donde un paso tiene variantes, elige la que corresponda al equipo.

## 1. Comprueba quién llega a quién

| Desde | Hacia | Puerto |
|---|---|---|
| Workstation | Relay | 22 |
| Origin | Relay | 22 |
| Relay | Origin | 22 (variante A del paso 3) o 18444 (variante B) |
| Relay | Workstation | 22 (variante A) o 18444 (variante B) |

Cómo se alcanzan los equipos (red local, cualquier VPN, dirección pública) lo decides tú; RCynQ solo necesita que las direcciones respondan. Anota la dirección de cada equipo tal como la ve su vecino.

**Sin Relay.** Si la Workstation y el Origin se alcanzan directamente, sáltate el paso 4 y toda mención del Relay.

## 2. Instala

En cada equipo, una de estas opciones:

| Sistema | Paquete | Dónde quedan los ajustes y los datos |
|---|---|---|
| Windows | `RCynQ-<version>-setup.exe`: instala para el usuario actual en `%LOCALAPPDATA%\Programs\RCynQ`, sin permisos de administrador | `config` y `data` dentro de la instalación |
| Windows | `RCynQ-<version>-win-x64.zip`: descomprímelo en una carpeta con permiso de escritura, por ejemplo `C:\RCynQ`, no en `Program Files` | `config` y `data` junto al programa |
| Debian, Ubuntu | `RCynQ-<version>-linux-x64.deb`: `sudo apt install ./RCynQ-<version>-linux-x64.deb`; Git y el cliente SSH se instalan con él | `~/.config/RCynQ` y `~/.local/share/RCynQ` de cada usuario |
| Linux, macOS | `RCynQ-<version>-<platform>.tar.gz`: descomprímelo en una carpeta con permiso de escritura, por ejemplo `~/rcynq` | `config` y `data` junto al programa |

Después inicia RCynQ: desde el menú Inicio o `RCynQ.exe` (Windows), desde el menú de aplicaciones o `rcynq-ui` (`.deb`),
`./RCynQ.sh` (archivos para Linux y macOS). El `README.txt` del paquete dice lo mismo para su sistema.

En la Workstation y el Origin instala Git y compruébalo: `git --version`.

Usa un paquete limpio para cada equipo. No copies una carpeta en la que RCynQ ya se haya ejecutado: sus carpetas `config` y `data` son la identidad de ese nodo.

## 3. Elige cómo acepta conexiones el nodo

El Relay usa siempre la variante A. Para la Workstation y el Origin elige en cada equipo.

### Variante A — servidor SSH

Lo más rápido: **Rol y nodos → Exportar la configuración → Instalar el servidor SSH** (o `rcynq ssh install-server`) y confirmar la solicitud de administrador. RCynQ instala, inicia y abre el servidor por sí mismo; si no es posible, muestra los comandos para ejecutarlos a mano. Los comandos de abajo hacen lo mismo a mano.

**Windows** (PowerShell como administrador):

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
```

Si el primer comando falla con `0x800f0954`, instala `OpenSSH-Win64-*.msi` de las [versiones de Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH/releases) con `msiexec /i <archivo> ADDLOCAL=Server`, inicia el servicio como arriba y abre el puerto:

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Linux:**

```bash
sudo apt install openssh-server      # o: sudo dnf install openssh-server
sudo systemctl enable --now ssh      # en Fedora y RHEL el servicio se llama sshd
```

**macOS:** Ajustes del Sistema → General → Compartir → Sesión remota.

Comprueba: `ssh -V` muestra una versión y el servicio está en marcha (`Get-Service sshd` / `systemctl status ssh`).

### Variante B — puerto TLS propio, sin servidor SSH

Para un equipo donde no se puede instalar un servidor SSH. Abre el puerto TCP de entrada `18444`.

Lo más rápido: **Rol y nodos → Exportar la configuración → Abrir el puerto 18444 en el cortafuegos** (o `rcynq endpoint open-port`) y confirmar la solicitud de administrador. A mano:

**Windows** (PowerShell como administrador):

```powershell
New-NetFirewallRule -Name rcynq -DisplayName 'RCynQ' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 18444
```

**Linux:** `sudo ufw allow 18444/tcp` o `sudo firewall-cmd --permanent --add-port=18444/tcp && sudo firewall-cmd --reload`.

El *cliente* SSH sigue haciendo falta en este equipo cuando se usa un Relay: `ssh -V`.

## Lo más rápido: invitaciones

Haz los pasos 1–3 y fija el rol de cada equipo en la ventana de configuración. Después, dos líneas de texto configuran toda la cadena.

Tras la primera configuración el asistente **Conectar un nodo** se abre solo; después está en **Rol y nodos → Conectar un nodo**. Comprueba que el equipo acepta vecinos, ofrece instalar el servidor SSH si no es así y dibuja la cadena mientras trabaja.

1. En el Relay: en el asistente **Siguiente**, luego **Crear la invitación** para el **Origin** y cópiala.
2. En el Origin: **A través de un Relay → Siguiente → Tengo una invitación**, pégala, **Unirse**. Después el asistente ofrece **Invitar a un nodo**: crea la invitación para la Workstation y cópiala.
3. En la Workstation: **A través de un Relay → Siguiente → Tengo una invitación**, pégala, **Unirse**.

Sin Relay elige **Directamente**: el Origin invita y la Workstation se une con su invitación. Cada etapa de la unión se muestra con su resultado; luego sigue con el paso 9.

Si el Relay tiene que permitir un nodo con derechos de administrador, confirma la solicitud en el Relay: la unión lo espera hasta tres minutos y después continúa sola.

Una invitación es un secreto: quien la tenga puede unirse una vez hasta que caduque (1 hora, 24 horas o 7 días). Pásala en privado. Lleva el token de enlace, así que no hay que copiar nada a mano. Línea de comandos: `rcynq invite --role Workstation`, `rcynq join <invitación>`, `rcynq invitation list`, `rcynq invitation revoke <id>`.

Los pasos 4–8 de abajo son la misma configuración con archivos, para cuando no se puede usar una invitación.

## 4. Configura el Relay

1. Inicia RCynQ. En la ventana de configuración indica el identificador del nodo (por ejemplo `relay-1`), el rol **Relay**, y guarda.
2. **Rol y nodos → Exportar la configuración.** Todas las direcciones del equipo van en el archivo; escribe además un nombre de host si al Relay se llega por un nombre o tras NAT. Exporta `relay.rcynq.json`. Los dos extremos importan este único archivo y cada uno usa la dirección que le responde.

## 5. Configura el Origin

1. Inicia RCynQ. En la ventana de configuración indica:
   - el identificador del nodo, por ejemplo `origin-1`;
   - el rol **Origin**;
   - la carpeta del repositorio Git (la que contiene `.git`);
   - **Cómo se conectan los vecinos a este nodo** — *A través del servidor SSH de este equipo* (variante A) o *Sin servidor SSH: puerto propio con TLS* (variante B).
2. Copia el **token de enlace compartido** generado. La Workstation necesita el mismo.
3. Guarda.
4. **Rol y nodos → Exportar la configuración.** La clave se crea sola si el equipo no tiene ninguna. Exporta `origin.rcynq.json`.

El repositorio se llama como su carpeta. Ese es el nombre que verá la Workstation.

## 6. Configura la Workstation

1. Inicia RCynQ. En la ventana de configuración indica:
   - el identificador del nodo, por ejemplo `workstation-1`;
   - el rol **Workstation**;
   - una carpeta para la copia de trabajo. Una carpeta vacía sirve: el primer Pull la llena;
   - el **token de enlace compartido** copiado del Origin;
   - **Cómo se conectan los vecinos a este nodo** — variante A o B como en el paso 3.
2. Guarda.
3. **Rol y nodos → Exportar la configuración.** Exporta `workstation.rcynq.json`.

## 7. Intercambia los archivos de nodo

En cada equipo: **Rol y nodos → Importar una configuración**, en cualquier orden. Tras cada importación RCynQ ejecuta la comprobación y dice si el enlace funciona.

| En | Importar |
|---|---|
| Workstation | `relay.rcynq.json` y `origin.rcynq.json` |
| Origin | `relay.rcynq.json` y `workstation.rcynq.json` |
| Relay | `workstation.rcynq.json` y `origin.rcynq.json` |

Sin Relay: la Workstation importa `origin.rcynq.json` y el Origin importa `workstation.rcynq.json`.

Los archivos no contienen claves privadas. Pásalos por un canal de confianza.

## 8. Permite las conexiones

Tras cada importación RCynQ ofrece permitir la clave del vecino.

- **Variante A:** confirma. En Windows confirma también la petición de administrador. La clave solo puede reenviar un puerto y no puede ejecutar comandos.
- **Variante B:** no hay nada que hacer; no hay servidor SSH que configurar.

Si lo rechazaste, pulsa **Rol y nodos → Permitir conexiones de nodos**.

## 9. Comprueba y haz Pull

1. En la Workstation y el Origin: **Diagnóstico → Ejecutar comprobación**. Corrige lo que salga en rojo; cada comprobación fallida dice cómo.
2. En la Workstation: **Ramas → Pull desde Origin**. La carpeta contiene ahora el proyecto.
3. Edita un archivo, abre **Sincronización** y pulsa **Enviar cambios**. El archivo aparece en el Origin.

A partir de ahora: **Pull** para obtener commits nuevos, **Enviar cambios** para entregar tus ediciones. Los cambios hechos en el Origin se envían desde allí de la misma forma.

## 10. Más repositorios

1. En el Origin: **Ramas → Añadir un repositorio** y elige la carpeta de otro repositorio Git.
2. En cualquiera de los nodos elige el repositorio en la lista de la parte superior de la ventana. El nodo cambia sin reiniciarse.
3. En la Workstation, un repositorio que aún no está en el equipo pide una carpeta y después se descarga solo.
4. El otro nodo muestra un aviso: **Cambiar también** o **Quedarse**. Los cambios solo se intercambian mientras ambos nodos trabajan con el mismo repositorio.

Línea de comandos: `rcynq repo list`, `rcynq repo add <name> <path>`, `rcynq repo use <name>`, `rcynq repo remove <name>`.

Quitar un repositorio de la lista nunca borra su carpeta ni sus archivos.

## 11. Rutas de reserva y varios Relay

Cuando ya hay en la cadena un nodo de ese rol, una importación no cambia nada en silencio: pregunta qué es el archivo.

| Qué se importa | Opciones |
|---|---|
| El mismo nodo en otra dirección | **Otra ruta hacia el mismo nodo** — la dirección se añade a las rutas del nodo. **Sustitución** — la nueva dirección ocupa el lugar de la que está en uso |
| Otro Relay | **Un nodo más** — el Relay entra en la cadena antes del extremo. **Sustitución** — ocupa el lugar del Relay elegido |
| Otro extremo | Solo **Sustitución**: una cadena termina en un nodo |

Un nodo sustituido se olvida junto con sus claves. Tras una sustitución pulsa **Permitir conexiones de nodos** para que su clave pierda el acceso.

**Una ruta de reserva.** Ejemplo: dos equipos están unidos por un túnel AmneziaWG sobre UDP y, junto a él, funciona un túnel sobre TCP.

1. En el vecino exporta otra vez la configuración eligiendo la dirección del segundo túnel.
2. Importa el archivo y elige **Otra ruta hacia el mismo nodo**.
3. Bajo el nodo, en **Nodos de la cadena**, aparecen sus rutas con su estado y su latencia.

La **elección automática** está activada por defecto: se usa la ruta preferida que responde y, entre iguales, la más rápida. Cuando una ruta deja de responder, el tráfico pasa a la siguiente; cuando la mejor vuelve a responder, el nodo regresa a ella. Las flechas cambian la prioridad (la menor es la preferida). La marca junto a una ruta la fija a mano hasta que vuelvas a la elección automática.

Si el camino pasa por un Relay, importa también allí la segunda dirección: un Relay solo reenvía a direcciones que conoce.

**Una cadena de dos Relay** (`Workstation → Relay 1 → Relay 2 → Origin`).

1. En la Workstation y el Origin importa ambos Relay; para el segundo elige **Un nodo más**. El orden es el orden de importación, contado desde este nodo.
2. En cada Relay importa el Relay vecino y ambos extremos.
3. En cada nodo pulsa **Permitir conexiones de nodos**.

Línea de comandos: `rcynq import <file> --as route|replace|hop [--link <link>]`. Sin `--as`, una importación así no cambia nada y muestra las opciones.

En `syncbridge.json`, las direcciones de reserva de un enlace se indican en `Routes` (`Host`, `Name` opcional, `SshPort`, `SshUser`, `Priority`); la prioridad de la dirección del propio enlace es `RoutePriority`.

## 12. Commits preparados en la Workstation

Cuando preparas una rama en la Workstation (cherry-picks, fusiones para una versión), sus commits pueden llegar al Origin tal cual: los mismos hashes, autores y orden, en la rama local del Origin y listos para que hagas push desde allí. RCynQ nunca hace push por sí mismo.

1. En el Origin: **Ajustes → Commits de la Workstation → Aceptar commits de la Workstation**. Está desactivado por defecto; mientras lo esté, el Origin rechaza los commits.
2. En la Workstation, la rama debe existir en el Origin y partir justo de su último commit: haz primero un **Pull**.
3. **Sincronización → Commits por enviar** muestra los commits que el Origin no tiene, los más recientes arriba. Marca el más reciente que quieras enviar; todos los anteriores se marcan con él.
4. Pulsa **Enviar commits**. El Origin avanza la misma rama y la lista se vacía.

Si el árbol de trabajo del Origin tiene cambios, los commits esperan allí: ocúpate de los cambios (guárdalos con stash o descártalos) y pulsa **Aplicar** en el Origin, o **Rechazar**. Hasta entonces el Origin rechaza los parches de archivos. Los commits construidos sobre otro commit, o para otra rama distinta de la activa en el Origin, se rechazan en el acto.

**Deshacer commits** deshace los commits sin enviar más recientes, como `git reset`: Soft deja sus cambios en el índice, Mixed en el árbol de trabajo y Hard los descarta. Solo funciona si el árbol de trabajo no tiene cambios.

Línea de comandos: `rcynq commits list`, `rcynq commits send [--up-to <hash>]`, `rcynq commits reset --to <hash> --mode Soft|Mixed|Hard`, en el Origin `rcynq commits apply <id>` y `rcynq commits decline <id>`. En `syncbridge.json` el interruptor es `AcceptCommits`.

## Actualizar desde una versión anterior

Las versiones anteriores llamaban a los roles Source, Mediator y Dest. Ahora son Workstation, Relay y Origin.

1. Actualiza **todos** los nodos a la vez: las versiones antiguas y nuevas no se entienden.
2. Inicia cada nodo. Su configuración se convierte sola; el archivo anterior se conserva como `syncbridge.json.bak`.
3. En los nodos con servidor SSH pulsa una vez **Rol y nodos → Permitir conexiones de nodos**. Reescribe las líneas de claves antiguas, que permitían más de lo necesario ahora.

## Si algo no funciona

| Síntoma | Comprueba |
|---|---|
| Un enlace está desconectado | Ejecuta el **Diagnóstico** en ese nodo. Desde el vecino: `Test-NetConnection <dirección> -Port 22` (Windows) o `nc -vz <dirección> 22`. Para la variante B usa el puerto `18444`. |
| «El token de enlace falta o no es válido» | El token de enlace compartido difiere entre la Workstation y el Origin. Cópialo otra vez en la pestaña **Rol y nodos**. |
| Falta la clave de un nodo | Exporta otra vez su archivo marcando **Incluir la clave SSH pública del nodo** e impórtalo. |
| «trabaja con el repositorio … y el parche es para …» | Los nodos trabajan con repositorios distintos. Cambia uno de ellos. |
| Pull se niega por cambios locales | Los archivos indicados se editaron y no se enviaron. Envíalos primero o deshaz las ediciones. |
| La dirección de un nodo cambió | Exporta su archivo con la nueva dirección, impórtalo en los vecinos y pulsa **Permitir conexiones de nodos**. |

Los registros están en la pestaña **Registros** y en `data/logs`.
