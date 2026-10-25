#!/usr/bin/env bash
# Mídia Stack para Linux — instalação com um comando:
#   curl -fsSL https://raw.githubusercontent.com/diorgejorge/midia-stack/main/linux/instalar.sh | bash
# Baixa o programa certo para o processador (x64 ou ARM64) em ~/midia-stack e abre o assistente.
set -euo pipefail

case "$(uname -m)" in
  x86_64 | amd64) arq=x64 ;;
  aarch64 | arm64) arq=arm64 ;;
  *) echo "Processador não suportado: $(uname -m) (só x64 e ARM64 de 64 bits)." >&2; exit 1 ;;
esac

pasta="${MIDIA_STACK_DIR:-$HOME/midia-stack}"
mkdir -p "$pasta"
echo "Baixando o Mídia Stack ($arq) para $pasta…"
curl -fL --progress-bar -o "$pasta/midia-stack" \
  "https://github.com/diorgejorge/midia-stack/releases/latest/download/midia-stack-linux-$arq"
chmod +x "$pasta/midia-stack"

# Atalho "midia-stack" no PATH do usuário, para usar de qualquer pasta.
mkdir -p "$HOME/.local/bin"
ln -sf "$pasta/midia-stack" "$HOME/.local/bin/midia-stack"

echo
echo "Pronto. Para abrir de novo depois: $pasta/midia-stack  (ou só midia-stack, numa sessão nova)"
echo
# O script chega pelo pipe do curl: o assistente lê as respostas do terminal.
exec "$pasta/midia-stack" "$@" < /dev/tty
