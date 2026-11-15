"""Modo automatico do ar: a cada 5 minutos refaz a sugestao de conforto (temperatura de dentro e de
fora, umidade de dentro e de fora, media dos ultimos dias, maxima de hoje e a hora) e a aplica sozinho.

Freios, para o ar nao ficar mudando nem brigar com quem mexe nele:
- desligado por padrao; so age dentro da janela de horas escolhida;
- nao liga o ar sozinho a menos que "podeLigar" esteja marcado; desliga so com "podeDesligar";
- entre duas mudancas automaticas, pelo menos 15 minutos (o ventilador alto some quando o quarto esfria);
- qualquer mudanca que nao foi dele (painel, app, controle remoto) pausa o automatico por "pausaMin";
- so manda o comando quando o ar esta diferente do que a sugestao pede.

A escolha do modo (clima.sugestao) tem histerese pelo modo em que o ar esta: ver as faixas la.
"""
import logging
import threading
import time
from datetime import datetime

from . import casa, servico

log = logging.getLogger("automatico")

INTERVALO = 300
MINIMO_ENTRE = 15 * 60
PADRAO = {"ligado": False, "podeLigar": False, "podeDesligar": True, "pausaMin": 120, "inicio": 0, "fim": 24}

_trava = threading.Lock()
_estado = {"ultimo": 0, "visto": None, "pausaAte": 0, "registro": []}
_sugerir = None


def config(p=None):
    c = dict(PADRAO)
    c.update((p if p is not None else casa.preferencias()["dados"]).get("auto") or {})
    return c


def _int(e, k):
    try:
        return int(float(e.get(k)))
    except (TypeError, ValueError):
        return None


SEM_ALVO = (0, 3)   # ventilar nao tem alvo e o seco trava o seu (22 °C): o aparelho muda t_temp sozinho


def _assinatura(estado):
    """O que o automatico vigia no ar: ligado, modo e alvo (o alvo so nos modos em que ele e de quem usa)."""
    modo = _int(estado, "t_work_mode")
    return (_int(estado, "t_power"), modo, None if modo in SEM_ALVO else _int(estado, "t_temp"))


def _registrar(agora, texto):
    _estado["registro"] = ([{"t": int(agora), "texto": texto}] + _estado["registro"])[:10]
    log.info("automatico: %s", texto)


def _na_janela(hora, c):
    ini, fim = c["inicio"], c["fim"]
    return ini <= hora < fim if ini <= fim else hora >= ini or hora < fim


def passo(agora=None, hora=None, sugerir=None):
    """Um ciclo do automatico. Devolve o que fez (texto) ou None. Separado do laco para os testes."""
    agora = time.time() if agora is None else agora
    hora = datetime.now().hour if hora is None else hora
    c = config()
    if not c["ligado"]:
        with _trava:
            _estado["visto"] = None
        return None
    ar, s, _ = (sugerir or _sugerir)()
    if not ar:
        return None
    with _trava:
        sig = _assinatura(ar["estado"])
        # Mudou sem ser por ele (painel, app, controle remoto): respeita e pausa.
        if _estado["visto"] is not None and sig != _estado["visto"]:
            _estado["pausaAte"] = agora + c["pausaMin"] * 60
            _registrar(agora, "Mudança manual no ar: automático pausado até %s." % time.strftime("%H:%M", time.localtime(_estado["pausaAte"])))
        _estado["visto"] = sig
        if agora < _estado["pausaAte"] or not _na_janela(hora, c) or agora - _estado["ultimo"] < MINIMO_ENTRE:
            return None
        if not s or not s.get("cena"):
            return None
        cena = s["cena"]
        ligado = sig[0] == 1
        if cena.get("desligar") and (not c["podeDesligar"] or not ligado):
            return None
        if not cena.get("desligar") and not ligado and not c["podeLigar"]:
            return None
        props = casa.comando_da_cena(cena, ar)
        if not props:
            return None
        if all(_int(ar["estado"], k) == _int({k: v}, k) for k, v in props.items()):
            return None
        try:
            servico.enviar(ar["puid"], props, True)
        except Exception as e:  # noqa: BLE001 - a nuvem pode recusar; tenta de novo no proximo ciclo
            _registrar(agora, "Não deu para mandar ao ar: %s" % e)
            return None
        _estado["ultimo"] = agora
        novo = dict(ar["estado"])
        novo.update({k: str(v) for k, v in props.items()})
        _estado["visto"] = _assinatura(novo)
        _registrar(agora, s["titulo"] + ".")
        return s["titulo"]


def situacao():
    c = config()
    with _trava:
        return {"config": c, "pausaAte": int(_estado["pausaAte"]) if _estado["pausaAte"] > time.time() else 0,
                "ultimo": int(_estado["ultimo"]), "registro": list(_estado["registro"])}


def retomar():
    """Tira a pausa (botao "Retomar agora")."""
    with _trava:
        _estado["pausaAte"] = 0
        _estado["visto"] = None


def _laco():
    while True:
        time.sleep(INTERVALO)
        try:
            passo()
        except Exception:  # noqa: BLE001 - o laco nao pode morrer
            log.exception("automatico: falhou")


def iniciar(sugerir):
    global _sugerir
    _sugerir = sugerir
    threading.Thread(target=_laco, name="automatico", daemon=True).start()
