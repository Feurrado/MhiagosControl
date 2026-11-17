#!/bin/bash
# Envia a copia mais nova do casa.db para o Google Drive, CRIPTOGRAFADA (rclone crypt), e
# aplica a rotacao la: DIARIOS dias, SEMANAIS semanas, MENSAIS meses (um arquivo por periodo,
# o mais novo). Chamado no fim do backup.sh; pode rodar sozinho: backup-nuvem.sh [arquivo.db]
#
# Config em /opt/mhiagos-casa/backup.conf (opcional): DIARIOS= SEMANAIS= MENSAIS= REMOTO=
# O remote (padrao "gcrypt:") e criado por configurar-nuvem.sh; tokens e senhas ficam so em
# /root/.config/rclone/rclone.conf (modo 600), nunca no repositorio.
# O resultado vai para backups/nuvem.json, que o card "Saude do servidor" mostra.
set -u

DIR="${BACKUPS:-/opt/mhiagos-casa/backups}"
CONF="${CONF:-/opt/mhiagos-casa/backup.conf}"
DIARIOS=14; SEMANAIS=8; MENSAIS=6; REMOTO="gcrypt:"
# shellcheck disable=SC1090
[ -f "$CONF" ] && . "$CONF"
STATUS="$DIR/nuvem.json"

gravar() { # ok erro arquivos
  local ult=0 ok="$1" erro="$2" n="$3" agora
  agora=$(date +%s)
  [ "$ok" = true ] && ult=$agora
  [ "$ok" != true ] && [ -f "$STATUS" ] && ult=$(sed -n 's/.*"ultimoOk": *\([0-9]*\).*/\1/p' "$STATUS")
  erro=$(printf '%s' "$erro" | tr -d '"\\\\' | tr '\n' ' ' | cut -c1-200)
  printf '{"ok": %s, "tentativa": %s, "ultimoOk": %s, "arquivos": %s, "erro": "%s"}\n' \
    "$ok" "$agora" "${ult:-0}" "$n" "$erro" > "$STATUS.tmp"
  chown 10001:10001 "$STATUS.tmp"; chmod 644 "$STATUS.tmp"; mv "$STATUS.tmp" "$STATUS"
}

command -v rclone >/dev/null || { gravar false "rclone nao instalado" 0; exit 1; }
rclone listremotes 2>/dev/null | grep -qx "${REMOTO%%:*}:" || { gravar false "remote ${REMOTO%%:*} nao configurado" 0; exit 1; }

ARQ="${1:-$(ls -1t "$DIR"/casa-*.db 2>/dev/null | head -n1)}"
[ -f "$ARQ" ] || { gravar false "nenhuma copia local" 0; exit 1; }

if ! saida=$(rclone copyto "$ARQ" "${REMOTO}$(basename "$ARQ")" --retries 3 2>&1); then
  gravar false "envio: $saida" 0; echo "$saida" >&2; exit 1
fi

# Rotacao: nomes casa-AAAAMMDD-HHMMSS.db, do mais novo para o mais antigo.
mapfile -t NOMES < <(rclone lsf "$REMOTO" --files-only 2>/dev/null | grep -E '^casa-[0-9]{8}-[0-9]{6}\.db$' | sort -r)
declare -A dias semanas meses manter
nd=0; ns=0; nm=0
for n in "${NOMES[@]}"; do
  d=${n:5:8}
  s=$(date -d "$d" +%G-%V 2>/dev/null || echo "$d")
  m=${d:0:6}
  if [ -z "${dias[$d]:-}" ] && [ "$nd" -lt "$DIARIOS" ]; then dias[$d]=1; nd=$((nd+1)); manter[$n]=1; fi
  if [ -z "${semanas[$s]:-}" ] && [ "$ns" -lt "$SEMANAIS" ]; then semanas[$s]=1; ns=$((ns+1)); manter[$n]=1; fi
  if [ -z "${meses[$m]:-}" ] && [ "$nm" -lt "$MENSAIS" ]; then meses[$m]=1; nm=$((nm+1)); manter[$n]=1; fi
done
# Nunca apaga nada se a listagem veio vazia ou sem o arquivo recem-enviado.
if [ "${#NOMES[@]}" -gt 0 ] && [ -n "${manter[$(basename "$ARQ")]:-}" ]; then
  for n in "${NOMES[@]}"; do
    [ -z "${manter[$n]:-}" ] && rclone deletefile "${REMOTO}$n" || true
  done
fi

gravar true "" "${#manter[@]}"
echo "$(date '+%F %T') nuvem: $(basename "$ARQ") enviado, ${#manter[@]} guardados"
