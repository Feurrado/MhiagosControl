#!/bin/bash
# Configura o Drive criptografado no rclone do servidor. Uso (como root, uma vez):
#   configurar-nuvem.sh '<token JSON do rclone authorize>'
# Cria o remote "gdrive" (drive.file, pasta MhiagosCasaBackups) e o "gcrypt" por cima.
# Gera duas senhas aleatorias e as mostra UMA vez: guarde-as FORA do servidor. Sem elas as
# copias na nuvem nao abrem. Reexecutar mantem as senhas ja existentes.
set -eu
TOKEN="$1"
umask 077
if rclone listremotes | grep -qx 'gcrypt:'; then
  echo "gcrypt ja existe; so atualizando o token do Drive"
  rclone config update gdrive token "$TOKEN" --non-interactive >/dev/null
  exit 0
fi
rclone config create gdrive drive scope drive.file token "$TOKEN" root_folder_id "" --non-interactive >/dev/null
S1=$(head -c 24 /dev/urandom | base64 | tr -d '/+=\n')
S2=$(head -c 24 /dev/urandom | base64 | tr -d '/+=\n')
rclone config create gcrypt crypt remote "gdrive:MhiagosCasaBackups" filename_encryption standard \
  password "$S1" password2 "$S2" --non-interactive >/dev/null
# create ja ofusca as senhas; o que importa guardar sao as originais abaixo.
echo "GUARDE FORA DO SERVIDOR (senha / senha2):"
echo "$S1"
echo "$S2"
