# Guia de configuração do RCynQ

[English](../En/SetupGuide.md) · [Deutsch](../De/SetupGuide.md) · [Español](../Es/SetupGuide.md) · [Français](../Fr/SetupGuide.md) · **Português** · [Русский](../Ru/SetupGuide.md)

Participam três computadores:

| Nó | Computador |
|---|---|
| **Workstation** | onde edita o projeto |
| **Origin** | onde estão os repositórios privados |
| **Relay** | um que os outros dois conseguem alcançar |

Siga os passos por ordem. Quando um passo tem variantes, escolha a que se adequa ao computador.

## 1. Verificar quem alcança quem

| De | Para | Porta |
|---|---|---|
| Workstation | Relay | 22 |
| Origin | Relay | 22 |
| Relay | Origin | 22 (variante A do passo 3) ou 18444 (variante B) |
| Relay | Workstation | 22 (variante A) ou 18444 (variante B) |

A forma como os computadores se alcançam (rede local, qualquer VPN, endereço público) fica ao seu critério; o RCynQ só precisa de que os endereços respondam. Anote o endereço de cada computador tal como o vizinho o vê.

**Sem Relay.** Se a Workstation e o Origin se alcançam diretamente, salte o passo 4 e todas as menções ao Relay.

## 2. Instalar

Em cada computador, uma das opções:

| Sistema | Pacote | Onde ficam as definições e os dados |
|---|---|---|
| Windows | `RCynQ-<version>-setup.exe`: instala para o utilizador atual em `%LOCALAPPDATA%\Programs\RCynQ`, sem direitos de administrador | `config` e `data` dentro da instalação |
| Windows | `RCynQ-<version>-win-x64.zip`: descompacte para uma pasta onde possa escrever, por exemplo `C:\RCynQ`, não para `Program Files` | `config` e `data` junto ao programa |
| Debian, Ubuntu | `RCynQ-<version>-linux-x64.deb`: `sudo apt install ./RCynQ-<version>-linux-x64.deb`; o Git e o cliente SSH são instalados com ele | `~/.config/RCynQ` e `~/.local/share/RCynQ` de cada utilizador |
| Linux, macOS | `RCynQ-<version>-<platform>.tar.gz`: descompacte para uma pasta onde possa escrever, por exemplo `~/rcynq` | `config` e `data` junto ao programa |

Depois inicie o RCynQ: a partir do menu Iniciar ou `RCynQ.exe` (Windows), do menu de aplicações ou `rcynq-ui` (`.deb`),
`./RCynQ.sh` (arquivos para Linux e macOS). O `README.txt` do pacote diz o mesmo para o seu sistema.

Na Workstation e no Origin, instale o Git e verifique-o: `git --version`.

Use um pacote limpo para cada computador. Não copie uma pasta onde o RCynQ já foi executado: as suas pastas `config` e `data` são a identidade desse nó.

## 3. Escolher como o nó aceita ligações

O Relay usa sempre a variante A. Para a Workstation e o Origin, escolha em cada computador.

### Variante A — servidor SSH

O mais rápido: **Papel e nós → Exportar a configuração → Instalar o servidor SSH** (ou `rcynq ssh install-server`) e confirmar o pedido de administrador. O RCynQ instala, inicia e abre o servidor sozinho; se não for possível, mostra os comandos para executar à mão. Os comandos abaixo fazem o mesmo à mão.

**Windows** (PowerShell como administrador):

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
```

Se o primeiro comando falhar com `0x800f0954`, instale `OpenSSH-Win64-*.msi` das [versões do Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH/releases) com `msiexec /i <ficheiro> ADDLOCAL=Server`, inicie o serviço como acima e abra a porta:

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Linux:**

```bash
sudo apt install openssh-server      # ou: sudo dnf install openssh-server
sudo systemctl enable --now ssh      # no Fedora e no RHEL o serviço chama-se sshd
```

**macOS:** Definições do Sistema → Geral → Partilha → Início de sessão remoto.

Verifique: `ssh -V` mostra uma versão e o serviço está em execução (`Get-Service sshd` / `systemctl status ssh`).

### Variante B — porta TLS própria, sem servidor SSH

Para um computador onde não é possível instalar um servidor SSH. Abra a porta TCP de entrada `18444`.

O mais rápido: **Papel e nós → Exportar a configuração → Abrir a porta 18444 na firewall** (ou `rcynq endpoint open-port`) e confirmar o pedido de administrador. À mão:

**Windows** (PowerShell como administrador):

```powershell
New-NetFirewallRule -Name rcynq -DisplayName 'RCynQ' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 18444
```

**Linux:** `sudo ufw allow 18444/tcp` ou `sudo firewall-cmd --permanent --add-port=18444/tcp && sudo firewall-cmd --reload`.

O *cliente* SSH continua a ser necessário neste computador quando é usado um Relay: `ssh -V`.

## O mais rápido: convites

Faça os passos 1–3 e defina o papel de cada computador na janela de configuração. Depois, duas linhas de texto configuram toda a cadeia.

Depois da primeira configuração, o assistente **Ligar um nó** abre-se sozinho; mais tarde está em **Papel e nós → Ligar um nó**. Verifica se o computador aceita vizinhos, propõe instalar o servidor SSH se não aceitar e desenha a cadeia enquanto trabalha.

1. No Relay: no assistente **Seguinte**, depois **Criar o convite** para o **Origin**, e copie-o.
2. No Origin: **Através de um Relay → Seguinte → Tenho um convite**, cole-o, **Juntar-se**. Depois o assistente propõe **Convidar um nó**: crie o convite para a Workstation e copie-o.
3. Na Workstation: **Através de um Relay → Seguinte → Tenho um convite**, cole-o, **Juntar-se**.

Sem Relay escolha **Diretamente**: o Origin convida e a Workstation junta-se com o convite dele. Cada etapa da adesão é mostrada com o seu resultado; depois continue no passo 9.

Um convite é um segredo: quem o tiver pode juntar-se uma vez até expirar (1 hora, 24 horas ou 7 dias). Passe-o em privado. Leva o token de ligação, por isso nada tem de ser copiado à mão. Linha de comandos: `rcynq invite --role Workstation`, `rcynq join <convite>`, `rcynq invitation list`, `rcynq invitation revoke <id>`.

Os passos 4–8 abaixo são a mesma configuração com ficheiros, para quando não for possível usar um convite.

## 4. Configurar o Relay

1. Inicie o RCynQ. Na janela de configuração indique o identificador do nó (por exemplo `relay-1`), o papel **Relay**, e guarde.
2. **Papel e nós → Exportar a configuração.** Todos os endereços do computador vão no ficheiro; escreva também um nome de anfitrião se o Relay for alcançado por um nome ou atrás de NAT. Exporte `relay.rcynq.json`. Os dois extremos importam este único ficheiro e cada um usa o endereço que lhe responde.

## 5. Configurar o Origin

1. Inicie o RCynQ. Na janela de configuração indique:
   - o identificador do nó, por exemplo `origin-1`;
   - o papel **Origin**;
   - a pasta do repositório Git (a que contém `.git`);
   - **Como os vizinhos se ligam a este nó** — *Através do servidor SSH deste computador* (variante A) ou *Sem servidor SSH: porta própria com TLS* (variante B).
2. Copie o **token de ligação partilhado** gerado. A Workstation precisa do mesmo.
3. Guarde.
4. **Papel e nós → Exportar a configuração.** A chave é criada sozinha se o computador não tiver nenhuma. Exporte `origin.rcynq.json`.

O repositório tem o nome da sua pasta. É esse o nome que a Workstation vai ver.

## 6. Configurar a Workstation

1. Inicie o RCynQ. Na janela de configuração indique:
   - o identificador do nó, por exemplo `workstation-1`;
   - o papel **Workstation**;
   - uma pasta para a cópia de trabalho. Uma pasta vazia serve: o primeiro Pull preenche-a;
   - o **token de ligação partilhado** copiado do Origin;
   - **Como os vizinhos se ligam a este nó** — variante A ou B como no passo 3.
2. Guarde.
3. **Papel e nós → Exportar a configuração.** Exporte `workstation.rcynq.json`.

## 7. Trocar os ficheiros dos nós

Em cada computador: **Papel e nós → Importar uma configuração**, por qualquer ordem. Depois de cada importação o RCynQ faz a verificação e diz se a ligação funciona.

| Em | Importar |
|---|---|
| Workstation | `relay.rcynq.json` e `origin.rcynq.json` |
| Origin | `relay.rcynq.json` e `workstation.rcynq.json` |
| Relay | `workstation.rcynq.json` e `origin.rcynq.json` |

Sem Relay: a Workstation importa `origin.rcynq.json` e o Origin importa `workstation.rcynq.json`.

Os ficheiros não contêm chaves privadas. Passe-os por um canal de confiança.

## 8. Permitir as ligações

Depois de cada importação, o RCynQ propõe permitir a chave do vizinho.

- **Variante A:** confirme. No Windows confirme também o pedido de administrador. A chave só pode reencaminhar uma porta e não pode executar comandos.
- **Variante B:** não há nada a fazer; não há servidor SSH para configurar.

Se recusou, prima **Papel e nós → Permitir ligações de nós**.

## 9. Verificar e fazer Pull

1. Na Workstation e no Origin: **Diagnóstico → Executar verificação**. Corrija o que estiver a vermelho; cada verificação falhada diz como.
2. Na Workstation: **Ramos → Pull a partir do Origin**. A pasta contém agora o projeto.
3. Edite um ficheiro, abra **Sincronização** e prima **Enviar alterações**. O ficheiro aparece no Origin.

A partir de agora: **Pull** para obter commits novos, **Enviar alterações** para entregar as suas edições. As alterações feitas no Origin são enviadas de lá da mesma forma.

## 10. Mais repositórios

1. No Origin: **Ramos → Adicionar um repositório** e escolha a pasta de outro repositório Git.
2. Em qualquer dos nós escolha o repositório na lista no topo da janela. O nó muda sem reiniciar.
3. Na Workstation, um repositório que ainda não está no computador pede uma pasta e depois obtém-se sozinho.
4. O outro nó mostra um aviso: **Mudar também** ou **Ficar**. As alterações só são trocadas enquanto ambos os nós trabalham com o mesmo repositório.

Linha de comandos: `rcynq repo list`, `rcynq repo add <name> <path>`, `rcynq repo use <name>`, `rcynq repo remove <name>`.

Remover um repositório da lista nunca elimina a sua pasta nem os seus ficheiros.

## 11. Rotas de reserva e vários Relay

Quando já existe na cadeia um nó desse papel, uma importação não altera nada em silêncio: pergunta o que é o ficheiro.

| O que é importado | Opções |
|---|---|
| O mesmo nó noutro endereço | **Outra rota para o mesmo nó** — o endereço é adicionado às rotas do nó. **Substituição** — o novo endereço ocupa o lugar do que está em uso |
| Outro Relay | **Mais um nó** — o Relay entra na cadeia antes da extremidade. **Substituição** — ocupa o lugar do Relay escolhido |
| Outra extremidade | Apenas **Substituição**: uma cadeia termina num nó |

Um nó substituído é esquecido juntamente com as suas chaves. Depois de uma substituição prima **Permitir ligações de nós**, para que a sua chave perca o acesso.

**Uma rota de reserva.** Exemplo: dois computadores estão ligados por um túnel AmneziaWG sobre UDP e, ao lado, funciona um túnel sobre TCP.

1. No vizinho exporte novamente a configuração, escolhendo o endereço do segundo túnel.
2. Importe o ficheiro e escolha **Outra rota para o mesmo nó**.
3. Por baixo do nó, em **Nós da cadeia**, aparecem as suas rotas com o estado e a latência.

A **escolha automática** está ativa por omissão: é usada a rota preferida que responde e, entre iguais, a mais rápida. Quando uma rota deixa de responder, o tráfego passa para a seguinte; quando a melhor volta a responder, o nó regressa a ela. As setas mudam a prioridade (a mais baixa é a preferida). A marca junto a uma rota fixa-a manualmente até voltar à escolha automática.

Se o caminho passar por um Relay, importe lá também o segundo endereço: um Relay só reencaminha para endereços que conhece.

**Uma cadeia de dois Relay** (`Workstation → Relay 1 → Relay 2 → Origin`).

1. Na Workstation e no Origin importe ambos os Relay; para o segundo escolha **Mais um nó**. A ordem é a ordem de importação, contada a partir deste nó.
2. Em cada Relay importe o Relay vizinho e ambas as extremidades.
3. Em cada nó prima **Permitir ligações de nós**.

Linha de comandos: `rcynq import <file> --as route|replace|hop [--link <link>]`. Sem `--as`, uma importação destas não altera nada e lista as opções.

No `syncbridge.json`, os endereços de reserva de uma ligação estão em `Routes` (`Host`, `Name` opcional, `SshPort`, `SshUser`, `Priority`); a prioridade do endereço da própria ligação é `RoutePriority`.

## Atualizar a partir de uma versão anterior

As versões anteriores chamavam aos papéis Source, Mediator e Dest. Agora são Workstation, Relay e Origin.

1. Atualize **todos** os nós ao mesmo tempo: as versões antigas e novas não se entendem.
2. Inicie cada nó. A sua configuração é convertida sozinha; o ficheiro anterior fica guardado como `syncbridge.json.bak`.
3. Nos nós com servidor SSH prima uma vez **Papel e nós → Permitir ligações de nós**. Isso reescreve as linhas de chaves antigas, que permitiam mais do que agora é necessário.

## Se algo não funcionar

| Sintoma | Verificar |
|---|---|
| Uma ligação está offline | Execute o **Diagnóstico** nesse nó. A partir do vizinho: `Test-NetConnection <endereço> -Port 22` (Windows) ou `nc -vz <endereço> 22`. Para a variante B use a porta `18444`. |
| «O token de ligação está em falta ou é inválido» | O token de ligação partilhado difere entre a Workstation e o Origin. Copie-o novamente no separador **Papel e nós**. |
| Falta a chave de um nó | Exporte novamente o ficheiro dele com **Incluir a chave SSH pública do nó** assinalado e importe-o. |
| «trabalha com o repositório … e o patch é para …» | Os nós trabalham com repositórios diferentes. Mude um deles. |
| O Pull recusa por causa de alterações locais | Os ficheiros indicados foram editados e não enviados. Envie-os primeiro ou desfaça as edições. |
| O endereço de um nó mudou | Exporte o ficheiro dele com o novo endereço, importe-o nos vizinhos e prima **Permitir ligações de nós**. |

Os registos estão no separador **Registos** e em `data/logs`.
