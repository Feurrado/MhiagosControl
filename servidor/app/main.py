"""API da Casa na rede local. Toda rota sob /api, menos /api/saude, pede a chave."""

import asyncio
import hmac
import json
import logging
import os
import re
import time
from contextlib import asynccontextmanager
from datetime import date, datetime, time as dtime, timedelta

from fastapi import Depends, FastAPI, Header, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse, Response
from pydantic import BaseModel, Field, model_validator

from . import banco, casa, servico
from .connectlife import ConnectLifeErro, ar_condicionado, extrair_codigo, extrair_estado

VERSAO = "1.3.0"
CHAVE = os.environ.get("MHIAGOS_CHAVE", "")

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logging.getLogger("httpx").setLevel(logging.WARNING)   # uma linha por leitura, e a URL leva o sourceId

@asynccontextmanager
async def _vida(_app):
    if len(CHAVE) < 16:
        raise RuntimeError("MHIAGOS_CHAVE ausente ou curta (minimo 16 caracteres)")
    banco.con()
    servico.iniciar()
    yield


app = FastAPI(title="Mhiagos Casa", version=VERSAO, docs_url=None, redoc_url=None, openapi_url=None, lifespan=_vida)

# A pagina so carrega o que vem daqui (sem CDN): bloqueia script de fora, moldura em outro
# site (clique enganado) e vazamento do endereco no Referer. Script e estilo sao embutidos
# no index.html, por isso o 'unsafe-inline'.
CABECALHOS = {
    "Content-Security-Policy": "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; "
                               "script-src 'self' 'unsafe-inline'; connect-src 'self'; manifest-src 'self'; "
                               "worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "no-referrer",
}
CORPO_MAXIMO = 32 * 1024 * 1024   # a importacao de 400 dias de ar cabe folgada


@app.middleware("http")
async def _protecao(request: Request, call_next):
    tamanho = request.headers.get("content-length")
    if tamanho and tamanho.isdigit() and int(tamanho) > CORPO_MAXIMO:
        return JSONResponse({"detail": "corpo grande demais"}, status_code=413)
    resposta = await call_next(request)
    for k, v in CABECALHOS.items():
        resposta.headers.setdefault(k, v)
    if request.url.path.startswith("/api/"):
        resposta.headers.setdefault("Cache-Control", "no-store")
    return resposta


def autorizado(authorization: str = Header(default="")):
    dado = authorization[7:] if authorization.startswith("Bearer ") else ""
    if not CHAVE or not hmac.compare_digest(dado.encode(), CHAVE.encode()):
        raise HTTPException(401, "chave invalida")


@app.exception_handler(ConnectLifeErro)
def _erro_nuvem(_, e: ConnectLifeErro):
    return JSONResponse({"erro": str(e), "pedeLogin": e.pede_login}, status_code=409 if e.pede_login else 502)


@app.exception_handler(casa.PreferenciaInvalida)
def _pref_invalida(_, e):
    return JSONResponse({"erro": str(e)}, status_code=422)


# ---------------- validacao do que chega ----------------

CHAVE_PROP = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,47}$")
VALOR_PROP = re.compile(r"^[A-Za-z0-9_.\-]{1,24}$")
INTEIRO = re.compile(r"^-?\d{1,5}$")


def validar_props(props, a=None):
    """Formato de comando da nuvem: poucas chaves curtas, valores curtos. Num ar com o
    modelo conhecido, so propriedades que ele grava, e com valor inteiro (como o app manda)."""
    if not props:
        raise HTTPException(400, "sem propriedades")
    if len(props) > 12:
        raise HTTPException(400, "propriedades demais")
    for k, v in props.items():
        if not CHAVE_PROP.match(k) or not VALOR_PROP.match(v):
            raise HTTPException(400, "propriedade invalida: %s" % k[:48])
        if a is not None and ar_condicionado(a):
            if not casa.grava(a, k):
                raise HTTPException(400, "o aparelho nao aceita %s" % k)
            if not INTEIRO.match(v):
                raise HTTPException(400, "valor invalido para %s" % k)


@app.get("/")
def pagina():
    return FileResponse(os.path.join(os.path.dirname(__file__), "static", "index.html"))


# Icone do Mhiagos Control (assets/app-icon.png reduzido) e o que o celular pede para
# instalar o painel como app. So estes arquivos, sem chave.
ESTATICOS = {
    "icone.png": "image/png", "icone-180.png": "image/png", "icone-192.png": "image/png", "icone-512.png": "image/png",
    "manifest.webmanifest": "application/manifest+json", "sw.js": "text/javascript",
}


@app.get("/{nome}")
def estatico(nome: str):
    if nome not in ESTATICOS:
        raise HTTPException(404, "nao encontrado")
    return FileResponse(os.path.join(os.path.dirname(__file__), "static", nome), media_type=ESTATICOS[nome],
                        headers={"Cache-Control": "no-cache"} if nome == "sw.js" else None)


@app.get("/api/saude")
def saude():
    return {"versao": VERSAO, "agora": int(time.time()), **servico.situacao()}


# ---------------- conta ----------------

class Conta(BaseModel):
    refresh_token: str
    source_id: str | None = None


class Codigo(BaseModel):
    colado: str = Field(max_length=4096)


@app.post("/api/conta", dependencies=[Depends(autorizado)])
def receber_conta(c: Conta):
    servico.receber_conta(c.refresh_token, c.source_id)
    return servico.situacao()


@app.get("/api/conta/login", dependencies=[Depends(autorizado)])
def login_url():
    return {"url": servico.url_de_login()}


@app.post("/api/conta/codigo", dependencies=[Depends(autorizado)])
def login_codigo(c: Codigo):
    code = extrair_codigo(c.colado)
    if not code:
        raise HTTPException(400, "nenhum code no endereco colado")
    # O endereco final traz o state do pedido: tem de ser o deste servidor (senao e o
    # login de outra conta, colado por engano ou de proposito).
    estado = extrair_estado(c.colado)
    if estado is not None and not servico.estado_de_login_valido(estado):
        raise HTTPException(400, "este endereco nao e do login aberto agora: abra o login de novo")
    servico.entrar_com_codigo(code)
    return servico.situacao()


@app.delete("/api/conta", dependencies=[Depends(autorizado)])
def sair():
    servico.sair()
    return servico.situacao()


# ---------------- aparelhos ----------------

class Comando(BaseModel):
    props: dict[str, str] = Field(max_length=12)
    semBipe: bool | None = None
    # Mudanca manual cancela o retorno da cena (como no app).
    manual: bool = True


def _leves(lista):
    """Sem a meta do modelo (centenas de propriedades da lavadora): ela vai em /propriedades."""
    return [{k: v for k, v in a.items() if k != "meta"} for a in lista]


def _aparelho(puid):
    a = servico.achar(puid)
    if a is None:
        raise HTTPException(404, "aparelho desconhecido")
    return a


def _sem_bipe(pedido):
    return casa.preferencias()["dados"].get("semBipe", False) if pedido is None else pedido


def _estado_completo():
    return {**servico.situacao(), "lista": _leves(servico.aparelhos()), "agendamentos": _agendamentos(),
            "agendamentosFalhos": servico.falhas_recentes(int(time.time()) - 86400)}


@app.get("/api/aparelhos", dependencies=[Depends(autorizado)])
def aparelhos():
    servico.tocar()
    return _estado_completo()


@app.post("/api/aparelhos/ler", dependencies=[Depends(autorizado)])
def ler_agora():
    servico.tocar()
    servico.ler(esperar=True)   # com uma leitura em curso, espera ela em vez de devolver o estado velho
    return _estado_completo()


@app.get("/api/aparelhos/{puid}/propriedades", dependencies=[Depends(autorizado)])
def propriedades(puid: str):
    return {"propriedades": casa.propriedades(_aparelho(puid))}


@app.post("/api/aparelhos/{puid}/comando", dependencies=[Depends(autorizado)])
def comando(puid: str, c: Comando):
    validar_props(c.props, _aparelho(puid))
    servico.enviar(puid, c.props, _sem_bipe(c.semBipe))
    if c.manual:
        servico.cancelar(puid, "retorno")
    return {"ok": True}


class Cena(BaseModel):
    indice: int = Field(ge=0, le=31)


@app.post("/api/aparelhos/{puid}/cena", dependencies=[Depends(autorizado)])
def cena(puid: str, c: Cena):
    """Roda a cena das preferencias; com duracao, agenda o retorno ao padrao (como o app)."""
    a = _aparelho(puid)
    cenas = casa.preferencias()["dados"]["cenas"]
    if not 0 <= c.indice < len(cenas):
        raise HTTPException(404, "cena desconhecida")
    x = cenas[c.indice]
    props = casa.comando_da_cena(x, a)
    if props is None:
        raise HTTPException(409, "este aparelho nao aceita a cena")
    servico.enviar(puid, props, _sem_bipe(None))
    servico.cancelar(puid, "retorno")
    if x.get("duracao", 0) > 0 and not x.get("desligar"):
        r = casa.comando_de_retorno(a)
        if r:
            servico.agendar(puid, int(time.time()) + int(x["duracao"]) * 60, r, "retorno")
    return {"ok": True}


class Timer(BaseModel):
    minutos: int = Field(ge=1, le=24 * 60)


@app.post("/api/aparelhos/{puid}/timer", dependencies=[Depends(autorizado)])
def timer(puid: str, t: Timer):
    if not 1 <= t.minutos <= 24 * 60:
        raise HTTPException(400, "minutos fora da faixa")
    _aparelho(puid)
    return {"id": servico.agendar(puid, int(time.time()) + t.minutos * 60, {"t_power": "0"}, "desligar")}


@app.delete("/api/aparelhos/{puid}/timer", dependencies=[Depends(autorizado)])
def cancelar_timer(puid: str):
    servico.cancelar(puid, "desligar")
    return {"ok": True}


@app.delete("/api/aparelhos/{puid}/retorno", dependencies=[Depends(autorizado)])
def cancelar_retorno(puid: str):
    servico.cancelar(puid, "retorno")
    return {"ok": True}


# ---------------- preferencias e consumo ----------------

class Preferencias(BaseModel):
    dados: dict
    origem: str = Field("web", max_length=16)


@app.get("/api/preferencias", dependencies=[Depends(autorizado)])
def preferencias():
    return casa.preferencias()


@app.put("/api/preferencias", dependencies=[Depends(autorizado)])
def gravar_preferencias(p: Preferencias):
    return casa.gravar_preferencias(p.dados, p.origem)


@app.get("/api/consumo", dependencies=[Depends(autorizado)])
def consumo(dias: int = 30, meses: int = 12):
    dias = max(1, min(400, dias))
    meses = max(1, min(36, meses))
    r = {}
    for a in servico.aparelhos():
        if a["tipo"] in ("008", "009"):
            r[a["puid"]] = {"tipo": "ar", "dias": casa.consumo_ar(a["puid"], dias)}
        elif a["tipo"] in ("003", "025", "027", "004"):
            r[a["puid"]] = {"tipo": "lavadora", "dias": casa.consumo_lavadora_dias(a["puid"], dias),
                            "meses": casa.consumo_lavadora(a["puid"], meses)}
    return {"preferencias": casa.preferencias()["dados"], "aparelhos": r}


def _csv(linhas, colunas):
    out = [";".join(colunas)]
    for l in linhas:
        out.append(";".join("" if l.get(c) is None else str(l[c]) for c in colunas))
    return "\n".join(out) + "\n"


@app.get("/api/exportar/ar.csv", dependencies=[Depends(autorizado)])
def exportar_ar(desde: int = 0):
    l = banco.linhas("SELECT puid,t,amb,alvo,ligado,modo FROM amostras_ar WHERE t > ? ORDER BY puid,t", (desde,))
    for x in l:
        x["quando"] = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(x["t"]))
    return Response(_csv(l, ["puid", "quando", "t", "amb", "alvo", "ligado", "modo"]), media_type="text/csv",
                    headers={"Content-Disposition": "attachment; filename=historico-ar.csv"})


@app.get("/api/exportar/ciclos.csv", dependencies=[Depends(autorizado)])
def exportar_ciclos(desde: int = 0):
    from . import tabelas
    l = banco.linhas("SELECT * FROM ciclos WHERE fim > ? ORDER BY inicio", (desde,))
    for x in l:
        x["inicio_local"] = time.strftime("%Y-%m-%d %H:%M", time.localtime(x["inicio"]))
        x["minutos"] = (x["fim"] - x["inicio"]) // 60
        x["programa_nome"] = tabelas.programa(x["programa"] or 0)
    return Response(_csv(l, ["puid", "inicio_local", "minutos", "programa", "programa_nome", "agua", "energia", "concluido"]),
                    media_type="text/csv", headers={"Content-Disposition": "attachment; filename=ciclos-lavadora.csv"})


@app.get("/api/aparelhos/{puid}/diagnostico", dependencies=[Depends(autorizado)])
def diagnostico(puid: str):
    return servico.diagnostico(puid)


# ---------------- historico ----------------

@app.get("/api/historico/ar", dependencies=[Depends(autorizado)])
def historico_ar(desde: int = 0, ate: int | None = None, puid: str | None = None):
    sql = "SELECT puid,t,amb,alvo,ligado,modo FROM amostras_ar WHERE t > ?"
    args = [desde]
    if ate is not None:
        sql += " AND t <= ?"; args.append(ate)
    if puid:
        sql += " AND puid = ?"; args.append(puid)
    return {"agora": int(time.time()), "amostras": banco.linhas(sql + " ORDER BY t LIMIT 100000", args)}


@app.get("/api/historico/ciclos", dependencies=[Depends(autorizado)])
def historico_ciclos(desde: int = 0):
    """Ciclos terminados depois de 'desde' (pelo fim) e o que esta em curso."""
    from . import tabelas
    ciclos = banco.linhas("SELECT * FROM ciclos WHERE fim > ? ORDER BY inicio", (desde,))
    for c in ciclos:
        c["programaNome"] = tabelas.programa(c["programa"] or 0)
    return {"agora": int(time.time()),
            "ciclos": ciclos,
            "emCurso": banco.linhas("SELECT * FROM em_curso")}


@app.get("/api/mudancas", dependencies=[Depends(autorizado)])
async def mudancas(desde: int = -1):
    """Espera longa: responde quando algo mudar (leitura nova, timer, ajustes) ou em 25 s.
    O painel web chama de novo na hora, entao ve a mudanca assim que o servidor ve.
    Assincrona: antes cada aba aberta segurava uma das 40 threads do servidor por 25 s."""
    servico.tocar()
    for _ in range(100):
        v = servico.versao()
        if v != desde:
            return {"versao": v}
        await asyncio.sleep(0.25)
    return {"versao": servico.versao()}


@app.get("/api/resumo-mes", dependencies=[Depends(autorizado)])
def resumo_mes():
    """Custo estimado deste mes e do anterior (inteiro e ate o mesmo dia), ar mais lavadora."""
    hoje = date.today()
    ini_atual = hoje.replace(day=1)
    ini_ant = (ini_atual - timedelta(days=1)).replace(day=1)
    mes_atual, mes_ant = ini_atual.strftime("%Y-%m"), ini_ant.strftime("%Y-%m")
    mesmo_dia = ini_ant.replace(day=min(hoje.day, (ini_atual - ini_ant).days))
    lim = int(datetime.combine(mesmo_dia, dtime.max).timestamp())
    tarifa = casa.preferencias()["dados"]["tarifa"]
    chaves = ("atual", "anterior", "anteriorAteHoje")
    vazio = lambda: {"kwh": 0.0, "custo": 0.0, "horas": 0.0, "agua": 0.0, "ciclos": 0}

    def onde(mes, ate_hoje):
        return ["atual"] if mes == mes_atual else ["anterior"] + (["anteriorAteHoje"] if ate_hoje else [])

    r = {k: vazio() for k in chaves}
    r["aparelhos"] = {}
    for a in servico.aparelhos():
        por = {k: vazio() for k in chaves}
        if a["tipo"] in ("008", "009"):
            for d in casa.consumo_ar(a["puid"], (hoje - ini_ant).days + 1):
                for k in onde(d["dia"][:7], d["dia"] <= mesmo_dia.isoformat()):
                    por[k]["kwh"] += d["kwh"]; por[k]["custo"] += d["custo"]; por[k]["horas"] += d["horas"]
        elif a["tipo"] in ("003", "025", "027", "004"):
            ini = int(datetime.combine(ini_ant, dtime.min).timestamp())
            for c in banco.linhas("SELECT inicio, agua, energia FROM ciclos WHERE puid=? AND inicio >= ?", (a["puid"], ini)):
                e = c["energia"] or 0
                for k in onde(datetime.fromtimestamp(c["inicio"]).strftime("%Y-%m"), c["inicio"] <= lim):
                    por[k]["kwh"] += e; por[k]["custo"] += e * tarifa; por[k]["agua"] += c["agua"] or 0; por[k]["ciclos"] += 1
        else:
            continue
        for k in chaves:
            for c, v in por[k].items():
                r[k][c] += v
            por[k] = {c: round(v, 2) for c, v in por[k].items()}
        r["aparelhos"][a["puid"]] = por
    for k in chaves:
        r[k] = {c: round(v, 2) for c, v in r[k].items()}
    r.update(mes=mes_atual, mesAnterior=mes_ant, tarifa=tarifa)
    return r


@app.get("/api/eventos", dependencies=[Depends(autorizado)])
def eventos(desde: int = 0):
    return {"eventos": banco.linhas("SELECT * FROM eventos WHERE t > ? ORDER BY id LIMIT 1000", (desde,))}


PUID = r"^[A-Za-z0-9_.:\-]{1,64}$"
EPOCA = dict(gt=1_500_000_000, lt=4_000_000_000)


class AmostraIn(BaseModel):
    puid: str = Field(pattern=PUID)
    t: int = Field(**EPOCA)
    amb: float | None = Field(None, ge=-50, le=100)
    alvo: float | None = Field(None, ge=-50, le=120)
    ligado: int = Field(0, ge=0, le=1)
    modo: int = Field(-1, ge=-1, le=20)


class CicloIn(BaseModel):
    puid: str = Field(pattern=PUID)
    inicio: int = Field(**EPOCA)
    fim: int = Field(**EPOCA)
    programa: int = Field(0, ge=0, le=999)
    agua: float = Field(0, ge=0, le=10_000)
    energia: float = Field(0, ge=0, le=1_000)
    concluido: int = Field(0, ge=0, le=1)

    @model_validator(mode="after")
    def _duracao(self):
        if not 0 <= self.fim - self.inicio <= 3 * 86400:
            raise ValueError("ciclo com fim antes do inicio ou mais de 3 dias")
        return self


class Importacao(BaseModel):
    amostras: list[AmostraIn] = Field(default_factory=list, max_length=600_000)
    ciclos: list[CicloIn] = Field(default_factory=list, max_length=20_000)


@app.post("/api/historico/importar", dependencies=[Depends(autorizado)])
def importar(i: Importacao):
    """O PC manda o que leu enquanto o servidor nao existia. Repetido nao duplica.
    Numa transacao so: com um commit por linha, a primeira troca grande passava dos 30 s
    do PC e era refeita a cada minuto."""
    with banco.transacao() as c:
        return _importar(c, i)


def _importar(c, i):
    na = nc = 0
    for a in (x.model_dump() for x in i.amostras):
        t = int(a["t"])
        # O PC e o servidor lendo a mesma hora: fica so uma (como HistoricoDoAr.Mesclar).
        if c.execute("SELECT 1 FROM amostras_ar WHERE puid=? AND t > ? AND t < ?", (a["puid"], t - 25, t + 25)).fetchone():
            continue
        c.execute("INSERT INTO amostras_ar(puid,t,amb,alvo,ligado,modo) VALUES(?,?,?,?,?,?)",
                  (a["puid"], t, a.get("amb"), a.get("alvo"), int(a.get("ligado", 0)), int(a.get("modo", -1))))
        na += 1
    for x in (y.model_dump() for y in i.ciclos):
        ini, fim = int(x["inicio"]), int(x["fim"])
        # Sobreposto a um que ja esta aqui e o mesmo ciclo: junta, do inicio mais cedo ao fim mais tarde.
        igual = c.execute("SELECT * FROM ciclos WHERE puid=? AND inicio <= ? AND fim >= ?", (x["puid"], fim, ini)).fetchone()
        if igual:
            if ini < igual["inicio"] or fim > igual["fim"]:
                c.execute("DELETE FROM ciclos WHERE puid=? AND inicio=?", (x["puid"], igual["inicio"]))
                c.execute("INSERT INTO ciclos(puid,inicio,fim,programa,agua,energia,concluido) VALUES(?,?,?,?,?,?,?)",
                          (x["puid"], min(ini, igual["inicio"]), max(fim, igual["fim"]), igual["programa"] or x.get("programa", 0),
                           max(igual["agua"] or 0, x.get("agua", 0)), max(igual["energia"] or 0, x.get("energia", 0)),
                           int(x.get("concluido", 0)) if fim > igual["fim"] else igual["concluido"]))
            continue
        c.execute("INSERT INTO ciclos(puid,inicio,fim,programa,agua,energia,concluido) VALUES(?,?,?,?,?,?,?)",
                  (x["puid"], ini, fim, int(x.get("programa", 0)), x.get("agua", 0), x.get("energia", 0), int(x.get("concluido", 0))))
        nc += 1
    return {"amostras": na, "ciclos": nc}


# ---------------- agendamentos ----------------

class Agendamento(BaseModel):
    puid: str = Field(pattern=PUID)
    quando: int = Field(**EPOCA)
    props: dict[str, str] = Field(max_length=12)
    nome: str | None = Field(None, pattern=r"^[a-z_]{1,20}$")


def _agendamentos():
    return [dict(a, props=json.loads(a["props"])) for a in banco.linhas("SELECT * FROM agendamentos ORDER BY quando")]


@app.get("/api/agendamentos", dependencies=[Depends(autorizado)])
def agendamentos():
    return {"agendamentos": _agendamentos()}


@app.post("/api/agendamentos", dependencies=[Depends(autorizado)])
def agendar(a: Agendamento):
    agora = int(time.time())
    if not agora - servico.LIMITE_ATRASO <= a.quando <= agora + 7 * 86400:
        raise HTTPException(400, "horario fora da faixa (ate 15 min atras e 7 dias a frente)")
    # Com a lista de aparelhos lida, so os que existem. Sem ela (logo depois de subir,
    # ou sem a conta), aceita: o PC entrega o timer mesmo assim.
    aparelho = servico.achar(a.puid)
    if aparelho is None and servico.aparelhos():
        raise HTTPException(404, "aparelho desconhecido")
    validar_props(a.props, aparelho)
    return {"id": servico.agendar(a.puid, a.quando, a.props, a.nome)}


@app.delete("/api/agendamentos/{ident}", dependencies=[Depends(autorizado)])
def cancelar(ident: int):
    servico.cancelar_id(ident)
    return {"ok": True}


@app.get("/api/agendamentos/{ident}/resultado", dependencies=[Depends(autorizado)])
def resultado_do_agendamento(ident: int):
    """O que aconteceu com um agendamento: o PC pergunta antes de dar o timer como feito."""
    return servico.resultado(ident)
