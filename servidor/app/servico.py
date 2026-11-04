"""A conta ConnectLife no servidor: le a nuvem, guarda historico e ciclos, roda agendamentos.

Mesma logica do ServicoCasa do Mhiagos (src/CasaServico.cs e src/CasaLavadora.cs),
para o historico do servidor e o do PC baterem quando sincronizam.
"""

import json
import logging
import os
import secrets
import threading
import time

from . import banco, casa, tabelas
from .connectlife import ConnectLife, ConnectLifeErro, ar_condicionado, lavadora, endereco_de_login

log = logging.getLogger("servico")

PERIODO = int(os.environ.get("MHIAGOS_PERIODO", "60"))
# Com alguem olhando (painel web ou o app pedindo estado), le a nuvem mais vezes.
PERIODO_RAPIDO = int(os.environ.get("MHIAGOS_PERIODO_RAPIDO", "10"))
ATIVO_POR = 180
DIAS_AR = int(os.environ.get("MHIAGOS_DIAS_AR", "400"))

# Propriedades que o aparelho so aceita num comando sozinhas (ar de janela 008/301,
# testado em 26/10/2026: t_sleep junto de outras era aceito pela nuvem e ignorado).
SO_SOZINHAS = ("t_sleep",)

_trava = threading.RLock()
_nuvem = None
_aparelhos = []
_metas = {}
_ultima_leitura = 0
_erro = None
_estado_login = None
_acordar = threading.Event()
_lendo = threading.Lock()
_ultimo_cliente = 0
_diag = {}      # puid -> (quando, lista oficial do autodiagnostico)
_mudou = threading.Condition()
_versao = 0     # sobe a cada mudanca que o painel web precisa mostrar
_assinatura = None


# ---------------- avisar o painel web ----------------

def avisar_mudanca():
    """Algo que a pagina mostra mudou: acorda quem espera em /api/mudancas."""
    global _versao
    with _mudou:
        _versao += 1
        _mudou.notify_all()


def esperar_mudanca(desde, limite=25):
    """Espera ate a versao sair de 'desde' (ou o limite) e devolve a atual."""
    with _mudou:
        _mudou.wait_for(lambda: _versao != desde, timeout=limite)
        return _versao


# ---------------- conta ----------------

def _gravar_token(refresh, source):
    banco.gravar_kv("conta", {"refresh_token": refresh, "source_id": source})


def iniciar():
    conta = banco.ler_kv("conta")
    if conta and conta.get("refresh_token"):
        _usar(ConnectLife(conta["refresh_token"], conta.get("source_id"), _gravar_token))
    threading.Thread(target=_laco, name="leitura", daemon=True).start()
    threading.Thread(target=_laco_agendamentos, name="agendamentos", daemon=True).start()


def _usar(nuvem):
    global _nuvem, _aparelhos, _erro
    with _trava:
        _nuvem = nuvem
        _aparelhos = []
        _erro = None
    _acordar.set()


def logado():
    with _trava:
        return _nuvem is not None


def receber_conta(refresh_token, source_id):
    """O PC entrega o acesso. Valida renovando na hora (o token gira e fica so aqui)."""
    n = ConnectLife(refresh_token, source_id, _gravar_token)
    n._renovar()
    _usar(n)
    ler()


def url_de_login():
    global _estado_login
    _estado_login = secrets.token_urlsafe(16)
    return endereco_de_login(_estado_login)


def entrar_com_codigo(code):
    n = ConnectLife(None, None, _gravar_token)
    n.trocar_codigo(code)
    _usar(n)
    ler()


def sair():
    global _nuvem, _aparelhos
    with _trava:
        _nuvem = None
        _aparelhos = []
    banco.apagar_kv("conta")
    avisar_mudanca()


def tocar():
    """Alguem esta olhando: passa a ler a cada PERIODO_RAPIDO por ATIVO_POR segundos."""
    global _ultimo_cliente
    parado = time.time() - _ultimo_cliente > ATIVO_POR
    _ultimo_cliente = time.time()
    if parado and time.time() - _ultima_leitura > PERIODO_RAPIDO:
        _acordar.set()


def _periodo():
    return PERIODO_RAPIDO if time.time() - _ultimo_cliente < ATIVO_POR else PERIODO


def situacao():
    with _trava:
        return {"logado": _nuvem is not None, "ultimaLeitura": _ultima_leitura, "erro": _erro,
                "aparelhos": len(_aparelhos), "periodo": _periodo()}


def aparelhos():
    with _trava:
        return [dict(a) for a in _aparelhos]


def achar(puid):
    with _trava:
        for a in _aparelhos:
            if a["puid"] == puid:
                return a
    return None


# ---------------- leitura ----------------

def _laco():
    while True:
        _acordar.wait(_periodo())
        _acordar.clear()
        try:
            ler()
        except Exception:
            log.exception("leitura")


def ler():
    # Uma leitura por vez: o laco, o painel e o comando podem pedir juntos.
    if not _lendo.acquire(blocking=False):
        return
    try:
        _ler()
    finally:
        _lendo.release()


def _ler():
    global _aparelhos, _ultima_leitura, _erro, _nuvem, _assinatura
    with _trava:
        nuvem = _nuvem
    if nuvem is None:
        return
    try:
        lista = nuvem.listar()
        agora = int(time.time())
        for a in lista:
            a["meta"] = _meta(nuvem, a)
            _diagnostico(nuvem, a, agora)
            if ar_condicionado(a):
                a["info"] = casa.descrever_ar(a)
            elif lavadora(a):
                a["info"] = casa.descrever_lavadora(a, agora)
        with _trava:
            if nuvem is not _nuvem:
                return
            _aparelhos = lista
            _ultima_leitura = agora
            _erro = None
        for a in lista:
            if lavadora(a):
                _conferir_ciclo(a, agora)
            elif ar_condicionado(a):
                _registrar_ar(a, agora)
        _limpar(agora)
        assinatura = json.dumps([[a["puid"], a.get("online"), a["estado"]] for a in lista], sort_keys=True, default=str)
        if assinatura != _assinatura:
            _assinatura = assinatura
            avisar_mudanca()
    except ConnectLifeErro as e:
        with _trava:
            _erro = str(e)
            if e.pede_login and nuvem is _nuvem:
                _nuvem = None
                _aparelhos = []
                banco.apagar_kv("conta")
        _evento(None, "erro", str(e))
        avisar_mudanca()


def _meta(nuvem, a):
    chave = a["tipo"] + "/" + a["codigoDoModelo"]
    if chave not in _metas:
        try:
            r = nuvem.propriedades(a["tipo"], a["codigoDoModelo"])
            _metas[chave] = tabelas.ler_meta(r if isinstance(r, dict) else None)
        except ConnectLifeErro as e:
            log.warning("propriedades de %s indisponiveis: %s", chave, e)
            return None
    return _metas[chave]


def _diagnostico(nuvem, a, agora):
    """A lista oficial de falhas do modelo (uma consulta por hora) e as que acusam agora."""
    q, lista = _diag.get(a["puid"], (0, None))
    if agora - q > 3600:
        try:
            r = nuvem.autodiagnostico(a["puid"])
            itens = ((r or {}).get("data") or {}).get("selfCheckList") or []
            lista = [{"chave": str(i.get("statusKey") or ""), "valor": str(i.get("statusValue") or "1"),
                      "nome": str(i.get("fault") or i.get("statusKey") or ""), "categoria": str(i.get("content") or ""),
                      "orientacao": str(i.get("guide") or "")} for i in itens if isinstance(i, dict)]
        except ConnectLifeErro as e:
            log.info("autodiagnostico de %s indisponivel: %s", a["puid"][-6:], e)
        _diag[a["puid"]] = (agora, lista)
    ativas = [f for f in (lista or []) if f["chave"] and a["estado"].get(f["chave"]) == f["valor"]]
    a["diagnostico"] = {"conhecidas": len(lista or []), "ativas": ativas}


def _num(a, chave, padrao=None):
    try:
        return float(a["estado"][chave])
    except (KeyError, TypeError, ValueError):
        return padrao


def _int(a, *chaves, padrao=0):
    for c in chaves:
        v = _num(a, c)
        if v is not None:
            return int(round(v))
    return padrao


def _registrar_ar(a, agora):
    """Uma amostra por leitura real, no maximo uma a cada 50 s (como o HistoricoDoAr)."""
    if not a["online"] or "t_power" not in a["estado"]:
        return
    c = banco.con()
    r = c.execute("SELECT MAX(t) m FROM amostras_ar WHERE puid=?", (a["puid"],)).fetchone()
    if r["m"] is not None and agora - r["m"] < 50:
        return
    c.execute("INSERT OR IGNORE INTO amostras_ar(puid,t,amb,alvo,ligado,modo) VALUES(?,?,?,?,?,?)",
              (a["puid"], agora, _num(a, "f_temp_in"), _num(a, "t_temp"),
               1 if _int(a, "t_power") else 0, _int(a, "t_work_mode", padrao=-1)))


def _conferir_ciclo(a, agora):
    """Inicio: a leitura passa a 'em funcionamento' (fora do aguardo do programado). Fim: deixa de estar."""
    if not a["online"]:
        return
    status = _int(a, "machine_status", "DeviceStatus", padrao=-1)
    fase = _int(a, "Current_program_phase", "CurrentProgramPhase")
    programa = _int(a, "Selected_program_ID", "Program")
    rodando = status in (2, 3)
    concluido = fase == 10
    trabalhando = rodando and fase != 11 and not concluido
    agua = energia = None
    if "Water_consumption_int" in a["estado"]:
        agua = _int(a, "Water_consumption_int") + _int(a, "Water_consumption_decimal") / 100.0
    if "Electricit_consumption_int" in a["estado"]:
        energia = _int(a, "Electricit_consumption_int") + _int(a, "Electricit_consumption_decimal") / 100.0

    c = banco.con()
    atual = c.execute("SELECT * FROM em_curso WHERE puid=?", (a["puid"],)).fetchone()
    if trabalhando:
        if atual is None:
            c.execute("INSERT INTO em_curso(puid,inicio,programa,agua,energia) VALUES(?,?,?,?,?)",
                      (a["puid"], agora, programa, agua or 0, energia or 0))
            _evento(a["puid"], "ciclo_inicio", "Programa %d" % programa)
        else:
            c.execute("UPDATE em_curso SET programa=?, agua=COALESCE(?,agua), energia=COALESCE(?,energia) WHERE puid=?",
                      (programa, agua, energia, a["puid"]))
    elif atual is not None:
        ok = concluido or status in (0, 1)
        inicio = atual["inicio"]
        # O PC pode ter mandado o mesmo ciclo antes (servidor parado no comeco): fica um so.
        for x in c.execute("SELECT inicio FROM ciclos WHERE puid=? AND inicio <= ? AND fim >= ?",
                           (a["puid"], agora, inicio)).fetchall():
            inicio = min(inicio, x["inicio"])
            c.execute("DELETE FROM ciclos WHERE puid=? AND inicio=?", (a["puid"], x["inicio"]))
        c.execute("INSERT OR REPLACE INTO ciclos(puid,inicio,fim,programa,agua,energia,concluido) VALUES(?,?,?,?,?,?,?)",
                  (a["puid"], inicio, agora, atual["programa"],
                   agua if agua is not None else atual["agua"],
                   energia if energia is not None else atual["energia"], 1 if ok else 0))
        c.execute("DELETE FROM em_curso WHERE puid=?", (a["puid"],))
        _evento(a["puid"], "ciclo_fim", "Programa %d, %d min" % (atual["programa"], (agora - atual["inicio"]) // 60))
    erro = status == 4 or _int(a, "error_code") != 0
    if erro:
        _evento(a["puid"], "lavadora_erro", "Codigo %d" % _int(a, "error_code"), uma_vez=True)


def _evento(puid, tipo, texto, uma_vez=False):
    c = banco.con()
    if uma_vez:
        r = c.execute("SELECT tipo FROM eventos WHERE puid IS ? ORDER BY id DESC LIMIT 1", (puid,)).fetchone()
        if r and r["tipo"] == tipo:
            return
    c.execute("INSERT INTO eventos(t,puid,tipo,texto) VALUES(?,?,?,?)", (int(time.time()), puid, tipo, texto))


_ultima_limpeza = 0


def _limpar(agora):
    global _ultima_limpeza
    if agora - _ultima_limpeza < 3600:
        return
    _ultima_limpeza = agora
    c = banco.con()
    c.execute("DELETE FROM amostras_ar WHERE t < ?", (agora - DIAS_AR * 86400,))
    c.execute("DELETE FROM eventos WHERE t < ?", (agora - 90 * 86400,))


# ---------------- comandos ----------------

def partes(props):
    """O comando dividido: as demais juntas primeiro, cada isolada depois (como ServicoCasa.Partes)."""
    juntas, isoladas = {}, []
    for k, v in props.items():
        if k in SO_SOZINHAS and len(props) > 1:
            isoladas.append({k: v})
        else:
            juntas[k] = v
    return ([juntas] if juntas else []) + isoladas


def enviar(puid, props, sem_bipe=False):
    """Como ServicoCasa.Enviar: com sem_bipe, t_beep=0 vai junto; se a nuvem recusar o par, repete sem."""
    with _trava:
        nuvem = _nuvem
    if nuvem is None:
        raise ConnectLifeErro("Servidor sem acesso a ConnectLife.", True)
    for i, parte in enumerate(partes(props)):
        if i > 0:
            time.sleep(1.5)
        if not sem_bipe:
            nuvem.definir(puid, parte)
            continue
        try:
            nuvem.definir(puid, dict(parte, t_beep="0"))
        except ConnectLifeErro as e:
            if e.pede_login:
                raise
            log.info("comando com t_beep=0 recusado, repetindo sem: %s", e)
            nuvem.definir(puid, parte)
    _evento(puid, "comando", json.dumps(props, separators=(",", ":")))
    # O aparelho leva uns segundos para confirmar na nuvem.
    tocar()
    threading.Timer(3.0, ler).start()
    threading.Timer(8.0, ler).start()


def diagnostico(puid):
    with _trava:
        nuvem = _nuvem
    if nuvem is None:
        raise ConnectLifeErro("Servidor sem acesso a ConnectLife.", True)
    return nuvem.autodiagnostico(puid)


# ---------------- agendamentos ----------------

def agendar(puid, quando, props, nome):
    """Um por aparelho e nome: o novo substitui. Timer de desligar cancela o retorno da cena (como o app)."""
    c = banco.con()
    if nome:
        c.execute("DELETE FROM agendamentos WHERE puid=? AND nome=?", (puid, nome))
    if nome == "desligar":
        c.execute("DELETE FROM agendamentos WHERE puid=? AND nome='retorno'", (puid,))
    cur = c.execute("INSERT INTO agendamentos(puid,quando,props,nome,criado) VALUES(?,?,?,?,?)",
                    (puid, int(quando), json.dumps(props), nome, int(time.time())))
    avisar_mudanca()
    return cur.lastrowid


def cancelar(puid, nome):
    banco.con().execute("DELETE FROM agendamentos WHERE puid=? AND nome=?", (puid, nome))
    avisar_mudanca()


def _laco_agendamentos():
    while True:
        time.sleep(10)
        try:
            agora = int(time.time())
            for ag in banco.linhas("SELECT * FROM agendamentos WHERE quando <= ? ORDER BY quando", (agora,)):
                banco.con().execute("DELETE FROM agendamentos WHERE id=?", (ag["id"],))
                avisar_mudanca()
                # Vencido ha mais de 15 min (servidor estava parado): descarta, como o app.
                if agora - ag["quando"] > 900:
                    _evento(ag["puid"], "agendamento_descartado", ag["nome"] or "")
                    continue
                try:
                    enviar(ag["puid"], json.loads(ag["props"]), casa.preferencias()["dados"].get("semBipe", False))
                    _evento(ag["puid"], "agendamento", ag["nome"] or "")
                except Exception as e:
                    _evento(ag["puid"], "agendamento_falhou", "%s: %s" % (ag["nome"] or "", e))
        except Exception:
            log.exception("agendamentos")
