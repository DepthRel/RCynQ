RCynQ {version} ({runtime}): Remote Code sYNchronization, Quick

[win] Arranque: RCynQ.exe nesta pasta (pode criar um atalho ou afixá-lo na barra de tarefas).
[unix] Arranque: ./RCynQ.sh
[linux] Menu de aplicações: execute uma vez ./install-desktop-entry.sh.
[macos] Ou faça duplo clique em RCynQ.command.
[win] Linha de comandos: cli\rcynq.exe (execute «rcynq --help»).
[unix] Linha de comandos: cli/rcynq (execute «rcynq --help»).
As definições ficam na pasta config e todos os dados (chaves, base de dados, registos) na pasta data junto ao programa: são a identidade deste nó. Mova ou elimine a pasta RCynQ inteira e nunca copie para outro computador uma pasta onde o RCynQ já foi executado.
Na Workstation e no Origin é preciso o Git, e em cada nó que passe por um Relay, um cliente SSH.
[win] O Windows mostra um aviso do SmartScreen enquanto a compilação não estiver assinada.
[linux] Instale o Git e o cliente SSH da sua distribuição: sudo apt install git openssh-client (ou dnf, pacman, ...).
[macos] A compilação não está assinada pela Apple. Antes do primeiro arranque execute nesta pasta: xattr -dr com.apple.quarantine .
[macos] Se o macOS continuar a não o iniciar, assine-o neste Mac: find . -type f \( -perm -u+x -o -name "*.dylib" \) -exec codesign --force --sign - {} \;
O RCynQ é software livre sob a GNU GPL, versão 3 ou posterior (LICENSE.txt); os componentes de outros autores e as respetivas licenças estão em THIRD-PARTY-NOTICES.txt.
Guia de configuração: https://github.com/DepthRel/RCynQ/blob/main/Docs/Pt/SetupGuide.md
