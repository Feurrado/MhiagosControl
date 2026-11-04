"""API da Casa na rede local. Toda rota sob /api, menos /api/saude, pede a chave."""

import hmac
import json
import logging
import os
import time

from fastapi import Depends, FastAPI, Header, HTTPException
from fastapi.responses import FileResponse, JSONResponse, Response
from pydantic import BaseModel

from . import banco, casa, servico
from .connectlife import ConnectLifeErro, extrair_codigo

VERSAO = "1.1.0"
CHAVE = os.environ.get("MHIAGOS_CHAVE", "")

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logging.getLogger("httpx").setLevel(logging.WARNING)   # uma linha por leitura, e a URL leva o sourceId
app = FastAPI(title="Mhiagos Casa", version=VERSAO, docs_url=None, redoc_url=None)


def autorizado(authorization: str = Header(default="")):
    dado = authorization[7:] if authorization.startswith("Bearer ") else ""
    if not CHAVE or not hmac.compare_digest(dado.encode(), CHAVE.encode()):
        raise HTTPException(401, "chave invalida")


@app.on_event("startup")
def _inicio():
    if len(CHAVE) < 16:
        raise RuntimeError("MHIAGOS_CHAVE ausente ou curta (minimo 16 caracteres)")
    banco.con()
    servico.iniciar()


@app.exception_handler(ConnectLifeErro)
def _erro_nuvem(_, e: ConnectLifeErro):
    return JSONResponse({"erro": str(e), "pedeLogin": e.pede_login}, status_code=409 if e.pede_login else 502)


@app.get("/")
def pagina():
    return FileResponse(os.path.join(os.path.dirname(__file__), "static", "index.html"))


@app.get("/icone.png")
def icone():
    """O icone do Mhiagos Control (assets/app-icon.png reduzido), na barra lateral e na aba."""
    return FileResponse(os.path.join(os.path.dirname(__file__), "static", "icone.png"), media_type="image/png")


@app.get("/api/saude")
def saude():
    return {"versao": VERSAO, "agora": int(time.time()), **servico.situacao()}


# ---------------- conta ----------------

class Conta(BaseModel):
    refresh_token: str
    source_id: str | None = None


class Codigo(BaseModel):
    colado: str


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
    servico.entrar_com_codigo(code)
    return servico.situacao()


@app.delete("/api/conta", dependencies=[Depends(autorizado)])
def sair():
    servico.sair()
    return servico.situacao()


# ---------------- aparelhos ----------------

class Comando(BaseModel):
    props: dict[str, str]
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


@app.get("/api/aparelhos", dependencies=[Depends(autorizado)])
def aparelhos():
    servico.tocar()
    return {**servico.situacao(), "lista": _leves(servico.aparelhos()), "agendamentos": _agendamentos()}


@app.post("/api/aparelhos/ler", dependencies=[Depends(autorizado)])
def ler_agora():
    servico.tocar()
    servico.ler()
    return {**servico.situacao(), "lista": _leves(servico.aparelhos()), "agendamentos": _agendamentos()}


@app.get("/api/aparelhos/{puid}/propriedades", dependencies=[Depends(autorizado)])
def propriedades(puid: str):
    return {"propriedades": casa.propriedades(_aparelho(puid))}


@app.post("/api/aparelhos/{puid}/comando", dependencies=[Depends(autorizado)])
def comando(puid: str, c: Comando):
    if not c.props:
        raise HTTPException(400, "sem propriedades")
    servico.enviar(puid, c.props, _sem_bipe(c.semBipe))
    if c.manual:
        servico.cancelar(puid, "retorno")
    return {"ok": True}


class Cena(BaseModel):
    indice: int


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
    minutos: int


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
    origem: str = "web"


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
            r[a["puid"]] = {"tipo": "lavadora", "meses": casa.consumo_lavadora(a["puid"], meses)}
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


@app.get("/api/eventos", dependencies=[Depends(autorizado)])
def eventos(desde: int = 0):
    return {"eventos": banco.linhas("SELECT * FROM eventos WHERE t > ? ORDER BY id LIMIT 1000", (desde,))}


class Importacao(BaseModel):
    amostras: list[dict] = []
    ciclos: list[dict] = []


@app.post("/api/historico/importar", dependencies=[Depends(autorizado)])
def importar(i: Importacao):
    """O PC manda o que leu enquanto o servidor nao existia. Repetido nao duplica."""
    c = banco.con()
    na = nc = 0
    for a in i.amostras:
        t = int(a["t"])
        # O PC e o servidor lendo a mesma hora: fica so uma (como HistoricoDoAr.Mesclar).
        if c.execute("SELECT 1 FROM amostras_ar WHERE puid=? AND t > ? AND t < ?", (a["puid"], t - 25, t + 25)).fetchone():
            continue
        c.execute("INSERT INTO amostras_ar(puid,t,amb,alvo,ligado,modo) VALUES(?,?,?,?,?,?)",
                  (a["puid"], t, a.get("amb"), a.get("alvo"), int(a.get("ligado", 0)), int(a.get("modo", -1))))
        na += 1
    for x in i.ciclos:
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
    puid: str
    quando: int
    props: dict[str, str]
    nome: str | None = None


def _agendamentos():
    return [dict(a, props=json.loads(a["props"])) for a in banco.linhas("SELECT * FROM agendamentos ORDER BY quando")]


@app.get("/api/agendamentos", dependencies=[Depends(autorizado)])
def agendamentos():
    return {"agendamentos": _agendamentos()}


@app.post("/api/agendamentos", dependencies=[Depends(autorizado)])
def agendar(a: Agendamento):
    if not a.props:
        raise HTTPException(400, "sem propriedades")
    return {"id": servico.agendar(a.puid, a.quando, a.props, a.nome)}


@app.delete("/api/agendamentos/{ident}", dependencies=[Depends(autorizado)])
def cancelar(ident: int):
    banco.con().execute("DELETE FROM agendamentos WHERE id=?", (ident,))
    return {"ok": True}
