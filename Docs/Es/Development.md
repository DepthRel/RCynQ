# Desarrollo de RCynQ

[English](../En/Development.md) · [Deutsch](../De/Development.md) · **Español** · [Français](../Fr/Development.md) · [Português](../Pt/Development.md) · [Русский](../Ru/Development.md)

## Entorno

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git y un cliente OpenSSH (`ssh`, `ssh-keygen`, `ssh-keyscan`): el producto los invoca, y también las pruebas de
  integración.
- VS Code con C# Dev Kit y la extensión de Avalonia, o Visual Studio / Rider.
- Windows PowerShell 5.1 o PowerShell 7 para `Scripts/*/*.ps1`, bash para `Scripts/*/*.sh`. Los scripts se agrupan
  según lo que construyen: `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (consulta [Scripts/README.md](../../Scripts/README.md)).

## Compilación y pruebas

```bash
dotnet build RCynQ.slnx                                   # se esperan 0 advertencias
dotnet test RCynQ.slnx                                    # todas las pruebas
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # un proyecto
```

Las pruebas son xUnit 2 sobre el ejecutor VSTest. Los paquetes de prueba comunes (xUnit, NSubstitute,
FluentAssertions) se referencian en `Directory.Build.props` para cada proyecto `*.Tests`, así que un archivo de proyecto
de pruebas solo enumera referencias a proyectos.

| Proyecto | Qué comprueba |
|---|---|
| RCynQ.Conventions.Tests | las reglas de AGENTS.md que los analizadores no pueden comprobar, sobre las fuentes analizadas con Roslyn: un tipo por archivo, tipos anidados privados, comentarios XML, enumeraciones en carpetas `Enums`, operadores lógicos al final de la línea, el orden de los using, la hora solo a través de `TimeProvider`, el esquema solo mediante migraciones, los proyectos en sus carpetas de solución |
| RCynQ.Core.Tests | roles de nodo, registros, ubicación de la configuración (instalación portátil), la marca de instalación, la validación y la conversión de configuraciones anteriores, los repositorios en la configuración |
| RCynQ.Localization.Tests | paquetes de idioma: lectura, el catálogo, elección del idioma según la cultura, el respaldo en inglés, las mismas claves y marcadores en cada idioma, existe cada clave pedida (código y pantallas), ningún texto ruso en el código, nodos nombrados solo por su rol |
| RCynQ.Core.Data.Tests | migraciones, repositorios, almacenamiento, permisos de archivo de la carpeta de datos |
| RCynQ.Git.Tests | comandos de Git, paquetes, construcción de parches, hashes, `.gitignore`, la exportación de un repositorio, el remote de la copia de trabajo, integración con un Git real |
| RCynQ.Ssh.Tests | comandos SSH, reenvíos de puertos, restricciones de `authorized_keys`, fijación de claves de host, `known_hosts`, el archivo de configuración del cliente |
| RCynQ.MergeTools.Tests | el catálogo de herramientas de fusión, su búsqueda en los lugares habituales, en `PATH`, con `vswhere` y en la configuración de Git, las plantillas de sus argumentos, su arranque |
| RCynQ.Transports.Tests | los transportes y el gestor de enlaces, la comprobación de un extremo |
| RCynQ.Sync.Tests | parches (firma, repetición, entrega, aplicación de todo o nada, conflictos, fusión de Git y sesiones de fusión para una herramienta de fusión), la base de sincronización, el canal Git, repositorios, rutas y su elección, diagnóstico, transferencia de configuración, conservación de datos |
| RCynQ.Service.Tests | la API HTTP local, su token y sus puntos públicos, el orden de arranque, Pull, envío y ping de extremo a extremo entre dos servicios, repositorios de extremo a extremo |
| RCynQ.Cli.Tests | los comandos de `rcynq` y su salida |
| RCynQ.UI.Tests | modelos de vista, diálogos, la ventana «Acerca de RCynQ» y la carta sobre un problema, el controlador del proceso del servicio, el cliente de la API, los almacenes de idioma y de ajustes, la fusión en una herramienta de fusión y la elección de la herramienta |
| RCynQ.UI.Headless.Tests | la ventana principal y la ventana «Acerca de RCynQ» en Avalonia Headless: cada texto cambia al instante con el idioma |

Una prueba indica el idioma en que están escritas sus expectativas: `TestTexts.English` o `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`) se pasa al código probado. Nada en el proceso elige un idioma a sus espaldas, así que las
pruebas en paralelo no se molestan.

## Localización

- Todo lo que lee un usuario (etiquetas, mensajes de error y de excepción, eventos, diagnóstico, salida de la CLI)
  está en `Sources/Core/RCynQ.Localization/Languages/<code>.json` y se toma por clave: `ILocalizer.Get("Key")` /
  `ILocalizer.Format("Key", …)` en el código, `{loc:Loc Key}` en XAML. No hay cadenas en lenguaje humano en el código
  ni en el marcado, ni ramas «si el idioma es …».
- Los idiomas son inglés (por defecto), alemán, español, francés, portugués (europeo) y ruso. En el primer inicio se
  toma el idioma del sistema operativo si hay un paquete para él; si no, el inglés. Una clave se añade a todos los
  archivos de idioma a la vez; una traducción que pierde una clave o un marcador hace fallar
  `RCynQ.Localization.Tests`.
- Los nodos se nombran solo por su rol: Workstation, Relay, Origin, las mismas palabras inglesas en todos los idiomas.
  Las palabras de versiones anteriores (Source, Mediator, Dest) y sus traducciones no se usan en los textos
  (`RoleWordingTests`).

### Añadir un idioma

1. Copia `Languages/en.json` a `<code>.json` (un código de dos letras ISO 639-1), escribe el nombre del idioma y
   traduce los valores. Las claves y los marcadores `{0}`, `{1}` no cambian.
2. Dibuja su bandera en `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` con `Scripts/Assets/generate-flags.ps1`.
3. Traduce lo que vive fuera de la aplicación: el asistente de instalación (`[Languages]`, `[Messages]` y
   `[CustomMessages]` de `Scripts/App/rcynq.iss`, la introducción de su página de licencia
   `Scripts/Shared/License/intro.<code>.txt`), las entradas de menú de los paquetes de Linux (`Comment[<code>]` en
   `Scripts/App/build-deb.sh` y `publish-common.sh`), las notas de los paquetes (`Scripts/App/Notes/README.<code>.txt`),
   la página del proyecto (una entrada más en `assets/languages.js`, una bandera en `assets/flags/<code>.svg` y una
   tabla en `assets/site.js` de `Sources/Apps/RCynQ.Pages`) y la documentación (abajo).

## Documentación

Los documentos se guardan por idioma: `Docs/En` es el conjunto principal; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` y
`Docs/Ru` contienen el mismo conjunto en alemán, español, francés, portugués y ruso. La línea bajo el título de cada
documento enlaza el mismo documento en los demás idiomas.

- el conjunto de archivos y sus nombres son los mismos en todos los idiomas; un cambio de sentido entra en todos los
  idiomas a la vez;
- los enlaces desde fuera (el README) llevan a `Docs/En`; la página del proyecto abre los documentos en el idioma
  elegido en ella;
- un idioma nuevo es una carpeta nueva `Docs/<Code>` con el mismo conjunto, más una línea en la tabla `docs` de
  `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Ejecución desde VS Code

`.vscode/launch.json`:

| Perfil | Qué hace |
|---|---|
| Service | compila e inicia `RCynQ.Service` con `.vscode/dev/syncbridge.json` |
| UI | compila e inicia `RCynQ.UI` con la misma configuración; la UI inicia el servicio por sí misma |
| CLI | compila e inicia `rcynq` con argumentos pedidos al usuario (`RCYNQ_CONFIG` apunta a la misma configuración) |
| DbMigrator | compila e inicia el migrador con argumentos pedidos al usuario |
| Service + UI | ambos, para depurar los dos procesos a la vez |

Las tareas de compilación (`.vscode/tasks.json`) llaman a `dotnet build <csproj>`: solo se compila el proyecto elegido y
aquello de lo que depende. Una compilación completa de la solución es una tarea aparte, `build: solution`.

Variables de entorno:

| Variable | Propósito |
|---|---|
| `RCYNQ_CONFIG` | el archivo de configuración en lugar del `config/syncbridge.json` portátil |
| `RCYNQ_SERVICE_EXE` | el ejecutable del servicio que inicia la UI (también `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | cómo llega la CLI a un servicio en ejecución |
| `RCYNQ_*` | cualquier ajuste de `syncbridge.json` para el servicio (`RCYNQ_SyncBridge__ListenPort`, …) |

## Base de datos

SQLite. El esquema lo crean solo las migraciones de FluentMigrator; EF Core solo asigna el modelo a las tablas. No se
usan `EnsureCreated` ni las migraciones de EF Core. Para cambiar el esquema:

1. Añade `Scripts/000N_Name.Up.sql` y `000N_Name.Down.sql` a `RCynQ.Core.Data.Migrations.SQLite`.
2. Añade una clase `Versions/M000N_Name.cs` con `[Migration(N, "...")]`.
3. Actualiza las entidades, la asignación y `RCynQDbContext`.

El servicio aplica las migraciones al iniciarse. A mano:

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## Licencia

RCynQ es software libre bajo la Licencia Pública General de GNU, versión 3 o posterior (`LICENSE.txt`).

- Solo dependencias compatibles con GPL-3.0-or-later. Una dependencia que acaba en un paquete se anota con el texto de
  su licencia en `THIRD-PARTY-NOTICES.txt` en el mismo cambio.
- Cada paquete lleva `LICENSE.txt` y `THIRD-PARTY-NOTICES.txt` junto al programa (`Write-PackageLicenses` y
  `write_package_licenses` en `Scripts/Shared/package-notes.*`); el `.deb` añade `/usr/share/doc/rcynq/copyright`.
- El asistente de instalación muestra la página de licencia en cada idioma: la introducción de
  `Scripts/Shared/License/intro.<code>.txt` seguida del texto en inglés de la GPL, el único con validez legal.
- La ventana «Acerca de RCynQ» de los ajustes indica la licencia y abre su texto y la lista de componentes.

## Reglas del código

Consulta [AGENTS.md](../../AGENTS.md): un tipo por archivo, un comentario XML en cada miembro (CS1591 es un error),
llaves en cada bloque, líneas en blanco entre los pasos lógicos de un método, operadores lógicos al final de la línea,
constantes en `UPPER_SNAKE_CASE`, enumeraciones en carpetas `Enums`, directivas `using` en los bloques System,
bibliotecas, RCynQ, la hora a través de `TimeProvider`, la entrada no fiable siempre se valida. Las comprueban la
compilación (analizadores de `.editorconfig`) y `RCynQ.Conventions.Tests`.

## Empaquetado

| Script | Resultado |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` y `RCynQ-<version>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | el mismo archivo más `RCynQ-<version>-setup.exe` (Inno Setup 6 o posterior) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<version>-<runtime>.tar.gz` y, donde hay `dpkg-deb` (Debian, Ubuntu, WSL), `RCynQ-<version>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<version>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | los mismos paquetes para Linux y macOS, compilados desde Windows dentro de WSL |
| `Scripts/build-release.ps1 [-Skip <pasos>] [-ContinueOnError]` | cada paquete de una versión, uno tras otro, y `SHA256SUMS-<version>.txt` |

La compilación es autónoma; el equipo de destino no necesita .NET, solo Git y un cliente SSH. Un paquete contiene el
servicio, la CLI, la UI y el migrador de la base de datos en `service/`, `cli/`, `ui/` y `dbmigrator/`. La raíz del
paquete de Windows contiene `RCynQ.exe` con el icono de la aplicación: el mismo host de .NET que la UI (el objetivo
`RCynQRootLauncher` de `RCynQ.UI.csproj`), solo que con la ruta relativa `ui\RCynQ.UI.dll`.

Los archivos y el instalador de Windows para el usuario actual son portátiles: la marca `rcynq.portable` de la raíz
mantiene la configuración en `config/` y todos los datos en `data/` junto al programa. El instalador para todos los
usuarios pone el programa en `Program Files` sin la marca, así que cada usuario guarda la configuración en
`%APPDATA%\RCynQ` y los datos en `%LOCALAPPDATA%\RCynQ`. El paquete `.deb` se instala en `/opt/rcynq` sin la
marca, así que cada usuario guarda la configuración en `~/.config/RCynQ` y los datos en `~/.local/share/RCynQ`.

Los paquetes para Linux y macOS se compilan en Linux (WSL también vale): solo allí se conservan en el archivo los
permisos de ejecución. El paquete de macOS compilado así no está firmado; consulta las notas que lleva dentro.

## Flujos de trabajo de GitHub

| Flujo | Cuándo | Qué |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | cada push a `main`, cada pull request, a mano | compila la solución y ejecuta todas las pruebas en Windows y Linux; con los analizadores de estilo y las pruebas de convenciones, una regla rota pone el resultado en rojo |
| **Packages** (`.github/workflows/packages.yml`) | a mano, o una etiqueta de versión `X.Y.Z` igual a `<Version>` de `Directory.Build.props` | compila el instalador y el `.zip` de Windows, el `.deb` y el `.tar.gz` de Linux, los archivos de macOS (en macOS, con firma ad hoc); una etiqueta los adjunta con `SHA256SUMS-<version>.txt` a un **borrador** de versión |
| **Pages** (`.github/workflows/pages.yml`) | un cambio de `Sources/Apps/RCynQ.Pages` en `main` | publica la página del proyecto |

Una versión: sube `<Version>`, envía la etiqueta (`git tag 0.2.0; git push origin 0.2.0`), escribe las notas del borrador
de la versión, revisa los archivos, publícala y solo entonces rellena `updates.json` de la página del proyecto.
