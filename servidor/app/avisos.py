"""Notificacao no celular pelo ntfy (app gratuito para Android e iOS).

O usuario assina um topico no app do ntfy e grava o mesmo topico em Ajustes. O topico
e o segredo: quem souber o nome recebe os avisos, por isso a pagina gera um aleatorio.
Os alertas seguem as mesmas chaves dos alertas do Windows (alertaFalha etc.).
"""

import logging
import os
import re
import threading

import httpx

from . import casa

log = logging.getLogger("casa.avisos")

SERVIDOR = os.environ.get("MHIAGOS_NTFY", "https://ntfy.sh").rstrip("/")
TOPICO_VALIDO = re.compile(r"^[A-Za-z0-9_-]{8,64}$")

_trava = threading.Lock()
_antes = {}     # puid -> {"online": bool, "falhas": set, "quente": bool}


def _topico():
    t = casa.preferencias()["dados"].get("ntfyTopico") or ""
    return t if TOPICO_VALIDO.match(t) else ""


def publicar(titulo, texto, prioridade=3, tags=None, topico=None):
    """Manda agora. Devolve None se foi, ou o texto do erro."""
    topico = topico or _topico()
    if not topico:
        return "sem tópico configurado"
    corpo = {"topic": topico, "title": titulo, "message": texto, "priority": prioridade, "tags": tags or []}
    try:
        r = httpx.post(SERVIDOR + "/", json=corpo, timeout=10)
        if r.status_code >= 400:
            return "ntfy respondeu %d" % r.status_code
    except httpx.HTTPError as e:
        return "ntfy fora do ar: %s" % e.__class__.__name__
    return None


def _enviar(titulo, texto, prioridade=3, tags=None):
    """Sem segurar a leitura da nuvem: vai numa linha de execucao propria."""
    if not _topico():
        return

    def vai():
        erro = publicar(titulo, texto, prioridade, tags)
        if erro:
            log.warning("aviso no celular nao saiu: %s", erro)
    threading.Thread(target=vai, daemon=True).start()


def _nome(a):
    return a.get("apelido") or a.get("modelo") or "Aparelho"


def evento(a, tipo, texto):
    """Chamado quando o servico grava um evento da lavadora."""
    p = casa.preferencias()["dados"]
    if tipo == "ciclo_fim" and p.get("alertaLavadoraFim"):
        _enviar(_nome(a) + ": ciclo terminou", texto, 4, ["white_check_mark"])
    elif tipo == "lavadora_erro" and p.get("alertaFalha"):
        _enviar(_nome(a) + ": falha", texto, 5, ["warning"])


def conferir(lista):
    """Compara a leitura nova com a anterior e avisa so nas transicoes (a primeira leitura so registra)."""
    p = casa.preferencias()["dados"]
    with _trava:
        for a in lista:
            info = a.get("info") or {}
            falhas = {f["nome"] for f in (a.get("diagnostico") or {}).get("ativas") or []}
            falhas |= {f["nome"] for f in info.get("falhas") or []}
            amb = info.get("ambiente")
            quente = amb is not None and info.get("unidade", "°C") == "°C" and amb > p.get("limiteQuente", 28)
            agora = {"online": bool(a.get("online")), "falhas": falhas, "quente": quente}
            antes = _antes.get(a["puid"])
            _antes[a["puid"]] = agora
            if antes is None:
                continue
            if antes["online"] and not agora["online"] and p.get("alertaDesconectado"):
                _enviar(_nome(a) + ": desconectado", "O aparelho saiu da nuvem da ConnectLife.", 3, ["electric_plug"])
            novas = falhas - antes["falhas"]
            if novas and p.get("alertaFalha"):
                _enviar(_nome(a) + ": falha", ", ".join(sorted(novas)), 5, ["warning"])
            if quente and not antes["quente"] and p.get("alertaQuente"):
                _enviar(_nome(a) + ": ambiente quente", "%s °C, acima do limite de %d °C." % (round(amb), p.get("limiteQuente", 28)),
                        3, ["thermometer"])


def esquecer():
    with _trava:
        _antes.clear()
