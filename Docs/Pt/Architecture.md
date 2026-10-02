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

- O RCynQ não escreve nada num repositório Git de um Origin: nem remote, nem definições, nem referências, nem commits, nem atualização do índice (o Git corre com `GIT_OPTIONAL_LOCKS=0`).
- Os ficheiros de trabalho do Origin só mudam quando é aplicado um patch enviado pelo utilizador.
- Tudo o que o Origin guarda (a lista de repositórios, a base de sincronização, o histórico, as cópias de segurança) está em `syncbridge.json` e na pasta de dados do nó.
- A manutenção do Git (criar o repositório numa pasta vazia, o remote `rcynq`, `refs/remotes/rcynq/*`, o avanço rápido no Pull) acontece apenas na cópia de trabalho da Workstation.

Um teste de ponta a ponta compara cada ficheiro em `.git` antes e depois de registar, mudar, servir e inspecionar um repositório.

## Canais

Ambos os canais usam a mesma ligação e a mesma autorização.

**Canal Git (Origin → Workstation).** A Workstation indica um repositório e os commits que já tem. O Origin responde com um pacote Git incremental. A Workstation obtém-no para `refs/remotes/rcynq/*` e faz avanço rápido. Nunca se faz push de nada.

**Canal de patches (nos dois sentidos).** Um patch é a diferença entre a árvore de trabalho e a *base de sincronização* — o que se sabe que o outro nó tem. A base de sincronização é construída automaticamente a partir do commit do Git que ambos os nós partilham, mais os ficheiros já trocados. Entrega: um manifesto assinado, depois cada ficheiro pelo seu SHA-256, depois um commit que aplica tudo ou nada. Um conflito é resolvido mantendo o ficheiro local, aceitando o recebido (com cópia de segurança) ou com uma fusão do Git a três vias.

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

## Segurança da troca

| Proteção | Mecanismo |
|---|---|
| Quem pode enviar | Token de ligação partilhado e um identificador de par conhecido em cada pedido |
| Integridade e proveniência | Assinatura ECDSA P-256 do manifesto, incluindo o nome do repositório |
| Repetição | O identificador e a idade do patch são verificados; um patch é aceite uma só vez |
| Saída de caminhos | Os caminhos de um patch são canónicos e têm de ficar dentro da árvore de trabalho |
| Repositório errado | É recusado um patch para um repositório com o qual o recetor não trabalha |
| Exposição do Origin | Um vizinho só conhece nomes de repositórios, nunca caminhos; um pacote só é servido para um nome registado |

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
