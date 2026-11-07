#!/bin/sh
# Copia do casa.db para uma pasta do servidor (fora do volume), com a API de backup do
# SQLite: a copia sai consistente mesmo com o servico gravando. Guarda as ultimas MANTER.
#
# Uso (cron do root, todo dia as 3h30):
#   30 3 * * * /opt/mhiagos-casa/backup.sh >> /var/log/mhiagos-casa-backup.log 2>&1
# Restaurar: parar o servico, copiar um casa-*.db por cima de /data/casa.db no volume
# (apagando casa.db-wal e casa.db-shm) e subir de novo.
set -eu

DESTINO="${1:-/opt/mhiagos-casa/backups}"
MANTER="${MANTER:-14}"
ARQ="casa-$(date +%Y%m%d-%H%M%S).db"

mkdir -p "$DESTINO"
chown 10001:10001 "$DESTINO"
chmod 700 "$DESTINO"

docker run --rm --network none --user 10001:10001 --read-only --cap-drop ALL \
  -v mhiagos-casa_dados:/data -v "$DESTINO":/backup \
  --entrypoint python mhiagos-casa:latest -c "
import sqlite3
origem = sqlite3.connect('/data/casa.db', timeout=30)
copia = sqlite3.connect('/backup/$ARQ')
origem.backup(copia)
ok = copia.execute('PRAGMA integrity_check').fetchone()[0]
copia.close()
origem.close()
if ok != 'ok':
    raise SystemExit('copia com problema: ' + ok)
"

ls -1t "$DESTINO"/casa-*.db 2>/dev/null | tail -n +"$((MANTER + 1))" | xargs -r rm -f
echo "$(date '+%F %T') backup em $DESTINO/$ARQ"
