# Arquitetura do RCynQ

[English](../En/Architecture.md) · [Deutsch](../De/Architecture.md) · [Español](../Es/Architecture.md) · [Français](../Fr/Architecture.md) · **Português** · [Русский](../Ru/Architecture.md)

## Nós

Um só programa desempenha três papéis; o papel é escolhido na configuração.

| Papel | O que é | O que faz |
|---|---|---|
| **Workstation** | O computador onde trabalha | Guarda as cópias de trabalho, obtém ramos, envia e recebe patches |
| **Origin** | O computador com os repositórios privados | Oferece os seus repositórios, recebe e envia patches |
| **Relay** | Um computador que ambas as extremidades alcançam | Reencaminha ligações SSH. Não tem repositório nem vê conteúdo |

Uma cadeia é `Workstation → Relay(s) → Origin`. Sem Relay, as duas extremidades ligam-se diretamente.

## Regra: o repositório do Origin só é lido

- O RCynQ nunca faz push nem cria num repositório Git de um Origin commits, remotes, definições ou referências próprios, nem atualiza o seu índice (o Git corre com `GIT_OPTIONAL_LOCKS=0`).
- A árvore de trabalho e o ramo ativo do Origin só mudam pelo que o utilizador enviou: os ficheiros de um patch ou, se o Origin aceitar commits, os próprios commits do utilizador, até aos quais o ramo avança (fast-forward). O reflog do ramo guarda esse avanço, e o Git anula-o sem mais.
- Guardar, aplicar ou retirar um ficheiro patch no Origin é uma ação do próprio utilizador nesse nó: altera apenas ficheiros da árvore de trabalho. O patch é construído num índice e num diretório de objetos privados, por isso nada é escrito em `.git`, e o que foi preparado ali à mão continua preparado.
- Tudo o que o Origin guarda (a lista de repositórios, a base de sincronização, o histórico, as cópias de segurança, os commits à espera de serem aplicados) está em `syncbridge.json` e na pasta de dados do nó.
- A manutenção do Git (criar o repositório numa pasta vazia, o remote `rcynq`, `refs/remotes/rcynq/*`, o avanço rápido no Pull) acontece apenas na cópia de trabalho da Workstation.

Testes de ponta a ponta comparam cada ficheiro em `.git` antes e depois de registar, mudar, servir e inspecionar um repositório, e antes e depois de uma entrega de commits recusada ou em espera.

## Canais

Ambos os canais usam a mesma ligação e a mesma autorização.

**Canal Git (Origin → Workstation).** A Workstation indica um repositório e os commits que já tem. O Origin responde com um pacote Git incremental. A Workstation obtém-no para `refs/remotes/rcynq/*` e faz avanço rápido. Nunca se faz push de nada. Depois, o Origin lista todos os seus ramos e `refs/remotes/rcynq/*` é alinhado com essa lista: um pacote só leva ramos com commits novos, por isso um ramo criado num commit que a Workstation já tem, um ramo recuado e um ramo eliminado chegam por esta via. Um Pull faz o ramo atual seguir `rcynq/<ramo>`, para que o Git o compare com o Origin e não com um remote que este computador não alcança.

**Canal de patches (nos dois sentidos).** Um patch é a diferença entre a árvore de trabalho e a *base de sincronização* — o que se sabe que o outro nó tem. A base de sincronização é construída automaticamente a partir do commit do Git que ambos os nós partilham, mais os ficheiros já trocados. Entrega: um manifesto assinado, depois cada ficheiro pelo seu SHA-256, depois um commit que aplica tudo ou nada. Um conflito é resolvido mantendo o ficheiro local, aceitando o recebido (com cópia de segurança), com uma fusão do Git a três vias ou numa ferramenta de fusão externa escolhida nas definições (VS Code, Visual Studio, os IDE da JetBrains, KDiff3, Meld, WinMerge, Beyond Compare, P4Merge, TortoiseGitMerge ou qualquer outra descrita pela sua linha de comandos). Com uma ferramenta de fusão, o Git funde sozinho as alterações em sítios diferentes, os ficheiros cujas edições colidem abrem-se na ferramenta um a um e nada é escrito antes de todos os ficheiros estarem fundidos; uma fusão feita sobre um ficheiro local que entretanto mudou é recusada.

**Ficheiros patch (Workstation e Origin).** O botão **Guardar** do separador Sincronização, ou `rcynq patch save <file>`, escreve os ficheiros alterados num ficheiro `.patch` comum que o `git apply` entende: os ficheiros assinalados, ou todos se nenhum estiver assinalado. O patch é construído em relação ao commit com que o separador compara. Com **Manter as alterações no ramo de trabalho** desligado, ou com `--discard`, as alterações são retiradas aplicando esse mesmo ficheiro ao contrário, e só depois de ele ser escrito: as edições feitas mais tarde noutros sítios ficam, e numa Workstation os ficheiros também saem do índice. **Aplicar patch** no separador Ramos, ou `rcynq patch apply <file>`, traz um patch para a árvore de trabalho: o utilizador escolhe os ficheiros numa lista simples ou numa árvore (`rcynq patch files <file>` lista-os, `--path` escolhe-os), nada é preparado nem confirmado, e o resultado aparece como alterações locais comuns. Um patch ou serve por inteiro ou não altera nada, e o motivo é explicado ao utilizador. O Git recusa caminhos que saem da árvore de trabalho ou entram em `.git`. Os bytes viajam pela API, por isso nenhum caminho do cliente chega ao serviço, e cada utilização deixa uma nota com o SHA-256 do ficheiro no registo do serviço. Um ficheiro patch não chega sozinho ao outro nó: é enviado como qualquer outra alteração.

**Entrega de commits (Workstation → Origin).** Os commits preparados na Workstation (cherry-picks, fusões) chegam ao Origin tal como estão: os mesmos hashes, autores e ordem. A Workstation envia um bloco dos seus commits por enviar que começa no mais antigo, como um pacote Git com um envelope assinado; a sessão do patch transporta-o. O Origin só o aceita se o utilizador tiver ligado **Aceitar commits da Workstation**, só para o ramo ativo e só se o seu `HEAD` for o commit sobre o qual o bloco foi feito; caso contrário, recusa de imediato. Os objetos são desempacotados numa quarentena junto ao repositório e comparados com a lista assinada (a cadeia de primeiros pais, cada caminho que um commit toca); só então os packs passam para `.git` e o ramo avança. Se o avanço falhar, os packs voltam a ser retirados. Enquanto a árvore de trabalho do Origin tiver alterações, os commits esperam: o utilizador aplica-os ou recusa-os lá, e entretanto os patches de ficheiros são recusados. Depois de aplicados, um Fetch põe a Workstation a par do Origin. Enquanto um ramo parte diretamente do commit do Origin, os seus commits não são alterações por enviar: o canal de patches oferece só o que não está em commit, sempre face à versão que o Origin tem. Uma Workstation anula commits por enviar (Soft, Mixed, Hard) só quando a sua árvore de trabalho não tem alterações.

## Como um nó aceita ligações

| Modo | Caminho | O par é verificado por |
|---|---|---|
| Servidor SSH | `ssh -L` para a porta loopback do nó, através dos Relay como anfitriões de salto | Chave de anfitrião SSH fixada |
| Porta TLS própria (por omissão `18444`) | Diretamente para a porta, ou por um reencaminhamento SSH que termina no último Relay | Impressão digital do certificado fixada |

Um Relay usa sempre o seu servidor SSH. As chaves em `authorized_keys` estão limitadas a um reencaminhamento de porta e não podem executar comandos.

A API local de um nó escuta apenas em loopback (por omissão `18443`) e exige um token guardado na pasta de dados.

## Rotas para um nó

Um nó pode estar acessível em vários endereços: por exemplo, através de um túnel sobre UDP e de um segundo túnel sobre TCP. Cada um desses endereços é uma *rota* da ligação. As chaves e as impressões digitais do nó são as mesmas para todas as rotas; só o endereço muda.

- **Armazenamento.** As rotas são guardadas na ligação. O endereço do nó na ligação é sempre o da rota em uso, por isso os patches, o canal Git, o ping e o diagnóstico seguem a rota escolhida sem saberem nada de rotas.
- **Verificação.** A cada 15 segundos, e de imediato após um ping falhado, o nó verifica cada rota. Um vizinho é verificado com uma ligação TCP. Um nó atrás de Relay é verificado através de um reencaminhamento SSH que termina no endereço da rota: o banner de um servidor SSH ou um handshake TLS contam como resposta.
- **Escolha.** Ganha a rota que responde com a prioridade mais baixa; entre iguais, a mais rápida, quando é claramente mais rápida do que a rota em uso. Quando uma rota melhor volta a responder, o nó regressa a ela. Enquanto nenhuma responde, o nó fica onde está.
- **Escolha manual.** Uma rota fixada é usada quer responda quer não, até a escolha ser devolvida ao nó.

Uma ligação com um só endereço não tem rotas: não é verificada nem alterada, e o nó funciona como antes.

O caminho através de vários Relay é definido pela extremidade: enumera-os por ordem e passa-os ela própria ao SSH. Um Relay fica passivo; apenas deixa as chaves das extremidades reencaminhar para os nós do papel oposto e para os servidores SSH dos Relay vizinhos, em cada endereço pelo qual os conhece.

## Convites

Um nó convida um vizinho com um bilhete de uso único: um segredo aleatório, do qual o nó guarda apenas o SHA-256, um papel e uma validade. O convite leva o cartão do nó, os cartões dos Relays no caminho, o bilhete, o token de ligação para a contraparte e o bilhete do Relay quando o nó tem um.

- **Pelo servidor SSH.** O bilhete vem com uma chave Ed25519 de uso único. A sua linha em `authorized_keys` é `restrict,expiry-time=…,command="rcynq enroll --config …"`: não executa nada além da adesão e o servidor recusa-a depois da validade. O nó que se junta entra com ela, pelos Relays como saltos, e envia o seu cartão como comando; `rcynq enroll` entrega-o ao serviço por loopback. A linha é retirada logo que o bilhete é usado, revogado ou expira.
- **Pela porta TLS própria.** A adesão é enviada para `/peer/enroll` dessa porta; a impressão digital do certificado do convite é fixada.

O nó que convida verifica o bilhete, gasta-o num só passo, importa o cartão e permite a chave. Um Relay responde a um extremo com um bilhete para o extremo oposto, que o extremo passa no seu próprio convite. A chave de anfitrião ou o certificado de cada nó é fixado a partir do convite antes de se enviar qualquer coisa. A adesão automática passa no máximo por um Relay; um caminho mais longo precisa de ficheiros.

## Segurança da troca

| Proteção | Mecanismo |
|---|---|
| Quem pode enviar | Token de ligação partilhado e um identificador de par conhecido em cada pedido |
| Integridade e proveniência | Assinatura ECDSA P-256 do manifesto, incluindo o nome do repositório |
| Repetição | O identificador e a idade do patch são verificados; um patch é aceite uma só vez |
| Saída de caminhos | Os caminhos de um patch são canónicos e têm de ficar dentro da árvore de trabalho |
| Repositório errado | É recusado um patch para um repositório com o qual o recetor não trabalha |
| Exposição do Origin | Um vizinho só conhece nomes de repositórios, nunca caminhos; um pacote só é servido para um nome registado |
| Commits | Desligado até o utilizador os permitir no Origin; o ramo, a base, cada commit e o hash do pacote são assinados; os objetos ficam em quarentena até serem verificados; apenas um avanço (fast-forward) do ramo ativo |
| Adesão | Bilhete de uso único com validade (guarda-se o hash); chave SSH de uso único limitada ao comando de adesão, com `expiry-time`; chave de anfitrião e certificado fixados a partir do convite |

## Repositórios

- O Origin mantém um catálogo: nome → pasta. Um nome tem de 1 a 64 caracteres de `A–Z a–z 0–9 . _ -`.
- A Workstation mantém nome → pasta local e só pode adicionar nomes que o Origin ofereça.
- Cada nó trabalha com um repositório de cada vez. Mudar não precisa de reinício: espera pelo envio, receção ou Pull em curso e depois muda a árvore de trabalho para tudo de uma vez.
- Cada ping leva, nos dois sentidos, o nome do repositório com que o nó trabalha. Um nó que descobre que o outro mudou avisa o utilizador e não o segue por conta própria.
- O nó mantém uma base de sincronização por repositório. O histórico de patches é mostrado para o repositório atual.

## Armazenamento

```
<pasta do programa>/
  config/syncbridge.json     definições do nó, ligações, repositórios
  data/
    rcynq.db                 SQLite: pares, ligações, patches, histórico, segredos cifrados (chave de assinatura)
    keys/                    chave SSH, certificado TLS, token da API, known_hosts
    patches/  backups/       conteúdo dos patches e ficheiros substituídos por um patch
    snapshots/               base de sincronização (uma pasta por cada repositório adicional)
    logs/
```

Um ficheiro de configuração escrito por uma versão anterior é convertido no arranque; o original fica guardado como `syncbridge.json.bak`.

## Projetos

| Projeto | Finalidade |
|---|---|
| `RCynQ.Core` | Contratos, configuração, registos da API |
| `RCynQ.Localization` | Textos, um ficheiro JSON por idioma |
| `RCynQ.Core.Data`, `…Migrations`, `…Migrations.SQLite` | Armazenamento SQLite e migrações do esquema |
| `RCynQ.Git` | Comandos do Git, manifestos, construção de patches |
| `RCynQ.MergeTools` | Ferramentas de fusão externas: o catálogo das conhecidas, a sua procura no computador, o seu arranque |
| `RCynQ.Ssh` | Comandos SSH, reencaminhamentos de portas, gestão de chaves e chaves de anfitrião |
| `RCynQ.Transports.*` | Ligações aos vizinhos e o seu estado |
| `RCynQ.Sync` | Patches, base de sincronização, canal Git, repositórios, diagnóstico |
| `RCynQ.Service` | API HTTP local e eventos SignalR |
| `RCynQ.UI` | Aplicação de ambiente de trabalho (Avalonia); inicia o serviço |
| `RCynQ.Cli` | Linha de comandos `rcynq` |
| `RCynQ.Dbs.SQLite.DbMigrator` | Migrador da base de dados em consola |
| `RCynQ.Pages` | Página estática do projeto para o GitHub Pages (sem compilação) |

Os testes estão em `Tests/<Projeto>.Tests`; `RCynQ.Conventions.Tests` verifica as regras de código de `AGENTS.md`. Como
compilar, testar e empacotar: [Desenvolvimento](Development.md).
