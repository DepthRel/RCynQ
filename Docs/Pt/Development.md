# Desenvolvimento do RCynQ

[English](../En/Development.md) · [Deutsch](../De/Development.md) · [Español](../Es/Development.md) · [Français](../Fr/Development.md) · **Português** · [Русский](../Ru/Development.md)

## Ambiente

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- Git e um cliente OpenSSH (`ssh`, `ssh-keygen`, `ssh-keyscan`): o produto chama-os, e os testes de integração também.
- VS Code com C# Dev Kit e a extensão do Avalonia, ou Visual Studio / Rider.
- Windows PowerShell 5.1 ou PowerShell 7 para `Scripts/*/*.ps1`, bash para `Scripts/*/*.sh`. Os scripts estão agrupados
  pelo que constroem: `Scripts/App`, `Scripts/Assets`, `Scripts/Shared` (veja [Scripts/README.md](../../Scripts/README.md)).

## Compilação e testes

```bash
dotnet build RCynQ.slnx                                   # esperam-se 0 avisos
dotnet test RCynQ.slnx                                    # todos os testes
dotnet test Tests/RCynQ.Sync.Tests/RCynQ.Sync.Tests.csproj # um projeto
```

Os testes são xUnit 2 no executor VSTest. Os pacotes de teste comuns (xUnit, NSubstitute, FluentAssertions) são
referenciados em `Directory.Build.props` para cada projeto `*.Tests`, por isso um ficheiro de projeto de testes só lista
referências a projetos.

| Projeto | O que verifica |
|---|---|
| RCynQ.Conventions.Tests | as regras do AGENTS.md que os analisadores não conseguem verificar, nas fontes analisadas com o Roslyn: um tipo por ficheiro, tipos aninhados privados, comentários XML, enumerações em pastas `Enums`, operadores lógicos no fim da linha, a ordem dos using, a hora só através de `TimeProvider`, o esquema só através de migrações, os projetos nas suas pastas da solução |
| RCynQ.Core.Tests | papéis dos nós, registos, localização da configuração (instalação portátil), a marca da instalação, a validação e a conversão de configurações anteriores, os repositórios na configuração |
| RCynQ.Localization.Tests | pacotes de idioma: leitura, o catálogo, a escolha do idioma pela cultura, o recurso ao inglês, as mesmas chaves e marcadores em todos os idiomas, existência de cada chave pedida (código e ecrãs), nenhum texto russo no código, nós nomeados só pelo papel |
| RCynQ.Core.Data.Tests | migrações, repositórios de dados, armazenamento, permissões dos ficheiros da pasta de dados |
| RCynQ.Git.Tests | comandos do Git, pacotes, construção de patches, hashes, `.gitignore`, a exportação de um repositório, o remote da cópia de trabalho, integração com um Git real |
| RCynQ.Ssh.Tests | comandos SSH, reencaminhamentos de portas, restrições do `authorized_keys`, fixação de chaves de anfitrião, `known_hosts`, o ficheiro de configuração do cliente |
| RCynQ.Transports.Tests | os transportes e o gestor de ligações, a verificação de uma extremidade |
| RCynQ.Sync.Tests | patches (assinatura, repetição, entrega, aplicação tudo ou nada, conflitos e fusão do Git), a base de sincronização, o canal Git, repositórios, rotas e a sua escolha, diagnóstico, transferência de configuração, retenção de dados |
| RCynQ.Service.Tests | a API HTTP local, o seu token e os pontos públicos, a ordem de arranque, Pull, envio e ping de ponta a ponta entre dois serviços, repositórios de ponta a ponta |
| RCynQ.Cli.Tests | os comandos do `rcynq` e a sua saída |
| RCynQ.UI.Tests | modelos de vista, diálogos, a janela «Sobre o RCynQ» e a mensagem sobre um problema, o controlador do processo do serviço, o cliente da API, os armazenamentos do idioma e das definições |
| RCynQ.UI.Headless.Tests | a janela principal e a janela «Sobre o RCynQ» no Avalonia Headless: cada texto muda de imediato com o idioma |

Um teste indica o idioma em que as suas expectativas estão escritas: `TestTexts.English` ou `TestTexts.Russian`
(`Tests/Shared/TestTexts.cs`) é passado ao código testado. Nada no processo escolhe um idioma pelas suas costas, por
isso os testes em paralelo não se perturbam.

## Localização

- Tudo o que um utilizador lê (etiquetas, mensagens de erro e de exceção, eventos, diagnóstico, saída da CLI) está em
  `Sources/Core/RCynQ.Localization/Languages/<code>.json` e é obtido por chave: `ILocalizer.Get("Key")` /
  `ILocalizer.Format("Key", …)` no código, `{loc:Loc Key}` em XAML. Não há cadeias em linguagem humana no código nem
  na marcação, nem ramos «se o idioma for …».
- Os idiomas são inglês (por omissão), alemão, espanhol, francês, português (europeu) e russo. No primeiro arranque é
  usado o idioma do sistema operativo se houver um pacote para ele; caso contrário, o inglês. Uma chave é adicionada a
  todos os ficheiros de idioma ao mesmo tempo; uma tradução que perca uma chave ou um marcador faz falhar
  `RCynQ.Localization.Tests`.
- Os nós são nomeados só pelo papel: Workstation, Relay, Origin, as mesmas palavras inglesas em todos os idiomas. As
  palavras de versões anteriores (Source, Mediator, Dest) e as suas traduções não são usadas nos textos
  (`RoleWordingTests`).

### Adicionar um idioma

1. Copie `Languages/en.json` para `<code>.json` (um código ISO 639-1 de duas letras), indique o nome do idioma e
   traduza os valores. As chaves e os marcadores `{0}`, `{1}` não mudam.
2. Desenhe a bandeira em `Sources/Apps/RCynQ.UI/Assets/Flags/<code>.png` com `Scripts/Assets/generate-flags.ps1`.
3. Traduza o que existe fora da aplicação: o assistente de instalação (`[Languages]`, `[Messages]` e
   `[CustomMessages]` de `Scripts/App/rcynq.iss`, a introdução da sua página de licença
   `Scripts/Shared/License/intro.<code>.txt`), as entradas de menu dos pacotes Linux (`Comment[<code>]` em
   `Scripts/App/build-deb.sh` e `publish-common.sh`), as notas dos pacotes (`Scripts/App/Notes/README.<code>.txt`), a
   página do projeto (mais uma entrada em `assets/languages.js`, uma bandeira em `assets/flags/<code>.svg` e uma tabela
   em `assets/site.js` de `Sources/Apps/RCynQ.Pages`) e a documentação (abaixo).

## Documentação

Os documentos são guardados por idioma: `Docs/En` é o conjunto principal; `Docs/De`, `Docs/Es`, `Docs/Fr`, `Docs/Pt` e
`Docs/Ru` contêm o mesmo conjunto em alemão, espanhol, francês, português e russo. A linha por baixo do título de cada
documento liga ao mesmo documento nos outros idiomas.

- o conjunto de ficheiros e os seus nomes são os mesmos em todos os idiomas; uma alteração de sentido entra em todos os
  idiomas ao mesmo tempo;
- as ligações vindas de fora (o README) levam a `Docs/En`; a página do projeto abre os documentos no idioma escolhido
  nela;
- um idioma novo é uma pasta nova `Docs/<Code>` com o mesmo conjunto, mais uma linha na tabela `docs` de
  `Sources/Apps/RCynQ.Pages/assets/languages.js`.

## Execução a partir do VS Code

`.vscode/launch.json`:

| Perfil | O que faz |
|---|---|
| Service | compila e inicia o `RCynQ.Service` com `.vscode/dev/syncbridge.json` |
| UI | compila e inicia o `RCynQ.UI` com a mesma configuração; a UI inicia o serviço sozinha |
| CLI | compila e inicia o `rcynq` com argumentos pedidos ao utilizador (`RCYNQ_CONFIG` aponta para a mesma configuração) |
| DbMigrator | compila e inicia o migrador com argumentos pedidos ao utilizador |
| Service + UI | ambos, para depurar os dois processos ao mesmo tempo |

As tarefas de compilação (`.vscode/tasks.json`) chamam `dotnet build <csproj>`: só é compilado o projeto escolhido e
aquilo de que depende. Uma compilação completa da solução é uma tarefa à parte, `build: solution`.

Variáveis de ambiente:

| Variável | Finalidade |
|---|---|
| `RCYNQ_CONFIG` | o ficheiro de configuração em vez do `config/syncbridge.json` portátil |
| `RCYNQ_SERVICE_EXE` | o executável do serviço que a UI inicia (também `--service-exe`) |
| `RCYNQ_SERVICE_URL`, `RCYNQ_API_TOKEN`, `RCYNQ_API_TOKEN_FILE` | como a CLI chega a um serviço em execução |
| `RCYNQ_*` | qualquer definição do `syncbridge.json` para o serviço (`RCYNQ_SyncBridge__ListenPort`, …) |

## Base de dados

SQLite. O esquema é criado apenas pelas migrações do FluentMigrator; o EF Core só mapeia o modelo para as tabelas. Não
se usam `EnsureCreated` nem migrações do EF Core. Para alterar o esquema:

1. Adicione `Scripts/000N_Name.Up.sql` e `000N_Name.Down.sql` a `RCynQ.Core.Data.Migrations.SQLite`.
2. Adicione uma classe `Versions/M000N_Name.cs` com `[Migration(N, "...")]`.
3. Atualize as entidades, o mapeamento e o `RCynQDbContext`.

O serviço aplica as migrações no arranque. Manualmente:

```bash
dotnet run --project Sources/Database/RCynQ.Dbs.SQLite.DbMigrator -- --database path/rcynq.db [--list]
```

## Licença

O RCynQ é software livre sob a Licença Pública Geral GNU, versão 3 ou posterior (`LICENSE.txt`).

- Apenas dependências compatíveis com GPL-3.0-or-later. Uma dependência que acabe num pacote é registada com o texto da
  sua licença em `THIRD-PARTY-NOTICES.txt` na mesma alteração.
- Cada pacote leva `LICENSE.txt` e `THIRD-PARTY-NOTICES.txt` junto ao programa (`Write-PackageLicenses` e
  `write_package_licenses` em `Scripts/Shared/package-notes.*`); o `.deb` acrescenta `/usr/share/doc/rcynq/copyright`.
- O assistente de instalação mostra a página da licença em cada idioma: a introdução de
  `Scripts/Shared/License/intro.<code>.txt` seguida do texto em inglês da GPL, o único com valor legal.
- A janela «Sobre o RCynQ» das definições indica a licença e abre o seu texto e a lista de componentes.

## Regras do código

Veja o [AGENTS.md](../../AGENTS.md): um tipo por ficheiro, um comentário XML em cada membro (CS1591 é um erro),
chavetas em cada bloco, linhas em branco entre os passos lógicos de um método, operadores lógicos no fim da linha,
constantes em `UPPER_SNAKE_CASE`, enumerações em pastas `Enums`, diretivas `using` nos blocos System, bibliotecas,
RCynQ, a hora através de `TimeProvider`, a entrada não fiável é sempre validada. A compilação (analisadores do
`.editorconfig`) e `RCynQ.Conventions.Tests` verificam-nas.

## Empacotamento

| Script | Resultado |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/<runtime>` e `RCynQ-<version>-<runtime>.zip` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` | o mesmo arquivo mais `RCynQ-<version>-setup.exe` (Inno Setup 6 ou mais recente) |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `RCynQ-<version>-<runtime>.tar.gz` e, onde existir `dpkg-deb` (Debian, Ubuntu, WSL), `RCynQ-<version>-<runtime>.deb` |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `RCynQ-<version>-<runtime>.tar.gz` |
| `Scripts/App/publish-linux.ps1`, `Scripts/App/publish-macos.ps1` | os mesmos pacotes para Linux e macOS, compilados a partir do Windows dentro do WSL |
| `Scripts/build-release.ps1 [-Skip <passos>] [-ContinueOnError]` | cada pacote de uma versão, um após o outro, e `SHA256SUMS-<version>.txt` |

A compilação é autónoma; o computador de destino não precisa de .NET, apenas de Git e de um cliente SSH. Um pacote
contém o serviço, a CLI, a UI e o migrador da base de dados em `service/`, `cli/`, `ui/` e `dbmigrator/`. A raiz do
pacote para Windows contém `RCynQ.exe` com o ícone da aplicação: o mesmo anfitrião .NET da UI (o alvo
`RCynQRootLauncher` de `RCynQ.UI.csproj`), apenas com o caminho relativo `ui\RCynQ.UI.dll`.

Os arquivos e o instalador para Windows para o utilizador atual são portáteis: a marca `rcynq.portable` na raiz mantém
a configuração em `config/` e todos os dados em `data/` junto ao programa. O instalador para todos os utilizadores põe o
programa em `Program Files` sem a marca, por isso cada utilizador guarda a configuração em `%APPDATA%\RCynQ` e os dados
em `%LOCALAPPDATA%\RCynQ`. O pacote `.deb` instala-se em `/opt/rcynq` sem a marca, por
isso cada utilizador guarda a configuração em `~/.config/RCynQ` e os dados em `~/.local/share/RCynQ`.

Os pacotes para Linux e macOS são compilados em Linux (o WSL também serve): só aí as permissões de execução se mantêm
no arquivo. O pacote para macOS compilado assim não está assinado; veja as notas que traz dentro.

## Fluxos de trabalho do GitHub

| Fluxo | Quando | O quê |
|---|---|---|
| **CI** (`.github/workflows/ci.yml`) | cada push para `main`, cada pull request, manualmente | compila a solução e executa todos os testes em Windows e Linux; com os analisadores de estilo e os testes de convenções, uma regra quebrada põe a execução a vermelho |
| **Packages** (`.github/workflows/packages.yml`) | manualmente, ou uma etiqueta de versão `X.Y.Z` igual a `<Version>` de `Directory.Build.props` | compila o instalador e o `.zip` para Windows, o `.deb` e o `.tar.gz` para Linux, os arquivos para macOS (em macOS, com assinatura ad hoc); uma etiqueta anexa-os com `SHA256SUMS-<version>.txt` a um **rascunho** de versão |
| **Pages** (`.github/workflows/pages.yml`) | uma alteração de `Sources/Apps/RCynQ.Pages` em `main` | publica a página do projeto |

Uma versão: suba `<Version>`, envie a etiqueta (`git tag 0.2.0; git push origin 0.2.0`), escreva as notas do rascunho
da versão, verifique os ficheiros, publique-a e só depois preencha o `updates.json` da página do projeto.
