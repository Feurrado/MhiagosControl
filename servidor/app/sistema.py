"""Saude do servidor (HOME01): carga, memoria, disco, temperatura e o ultimo backup.

Tudo vem de /proc, /sys e do disco, sem dependencia nova. Dentro do container o /proc
mostra o host (carga e memoria); a temperatura so aparece se o /sys deixar ler. O backup
e conferido numa pasta montada so para leitura (MHIAGOS_BACKUPS, padrao /backups): sem ela
o campo vem nulo e nada e dito sobre backup.
"""

import glob
import os
import shutil
import time

BACKUPS = os.environ.get("MHIAGOS_BACKUPS", "/backups")
BACKUP_VELHO = 36 * 3600     # s: o cron e diario; passar de 36 h pede atencao


def _ler(caminho):
    try:
        with open(caminho) as f:
            return f.read()
    except OSError:
        return None


def memoria():
    """(total, disponivel) em MB, do /proc/meminfo; None se nao der para ler."""
    t = _ler("/proc/meminfo")
    if not t:
        return None
    v = {}
    for linha in t.splitlines():
        k, _, resto = linha.partition(":")
        if k in ("MemTotal", "MemAvailable"):
            try:
                v[k] = int(resto.split()[0]) // 1024
            except (ValueError, IndexError):
                pass
    return (v["MemTotal"], v["MemAvailable"]) if len(v) == 2 else None


def temperatura():
    """Maior temperatura de zona termica legivel, em graus C (ou None)."""
    melhor = None
    for arq in glob.glob("/sys/class/thermal/thermal_zone*/temp") + glob.glob("/sys/class/hwmon/hwmon*/temp*_input"):
        t = _ler(arq)
        try:
            c = int(t) / 1000
        except (TypeError, ValueError):
            continue
        if 0 < c < 130 and (melhor is None or c > melhor):
            melhor = c
    return None if melhor is None else round(melhor, 1)


def backup(agora=None, pasta=None):
    """Ultimo casa-*.db da pasta de backups: {quando, idadeHoras, quantidade, velho}; None sem pasta."""
    pasta = BACKUPS if pasta is None else pasta
    if not os.path.isdir(pasta):
        return None
    arqs = glob.glob(os.path.join(pasta, "casa-*.db"))
    agora = agora or time.time()
    if not arqs:
        return {"quando": 0, "idadeHoras": None, "quantidade": 0, "velho": True}
    ult = max(os.path.getmtime(a) for a in arqs)
    return {"quando": int(ult), "idadeHoras": round((agora - ult) / 3600, 1), "quantidade": len(arqs),
            "velho": agora - ult > BACKUP_VELHO}


def estado(dados="/data", agora=None):
    agora = agora or time.time()
    m = memoria()
    try:
        carga = [round(x, 2) for x in os.getloadavg()]
    except OSError:
        carga = None
    try:
        du = shutil.disk_usage(dados)
        disco = {"totalGB": round(du.total / 1e9, 1), "livreGB": round(du.free / 1e9, 1)}
    except OSError:
        disco = None
    up = _ler("/proc/uptime")
    banco = os.path.join(dados, "casa.db")
    return {"carga": carga, "cpus": os.cpu_count(),
            "memoria": {"totalMB": m[0], "livreMB": m[1]} if m else None,
            "disco": disco, "temperatura": temperatura(),
            "ligadoHoras": round(float(up.split()[0]) / 3600, 1) if up else None,
            "banco": {"MB": round(os.path.getsize(banco) / 1e6, 2)} if os.path.exists(banco) else None,
            "backup": backup(agora)}
