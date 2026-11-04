"""O que o cartao do ar e o da lavadora mostram, as preferencias da Casa e o consumo.

Espelha o Mhiagos: CartaoDoAr/CenaCasa (src/CasaAr.cs, src/CasaCenas.cs),
CicloAtual/CartaoDaLavadora (src/CasaLavadora.cs), PreferenciasCasa e
HistoricoDoAr.PorDia (src/CasaServico.cs). Assim o painel web e o app mostram
e mandam a mesma coisa.
"""

import time
from datetime import datetime, timedelta

from . import banco, tabelas


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


def _meta(a, chave):
    return (a.get("meta") or {}).get(chave)


def grava(a, chave):
    m = _meta(a, chave)
    if m is None:
        return chave in a["estado"]
    return m["grava"]


def aceita(a, chave, valor):
    """Como CenaCasa.Aceita: o modelo grava a propriedade e aceita o valor."""
    m = _meta(a, chave)
    if m is None:
        return chave in a["estado"]
    if not m["grava"]:
        return False
    if m["valores"]:
        return str(valor) in m["valores"]
    if m["faixas"]:
        return any(f[0] <= valor <= f[1] for f in m["faixas"])
    return True


def _valores(a, chave, ordem):
    """Os valores que o modelo aceita, na ordem do app; sem meta, a ordem padrao."""
    m = _meta(a, chave)
    if not m or not m["valores"]:
        return list(ordem)
    vs = [int(v) for v, nome in m["valores"].items() if "reserv" not in nome.lower()]
    return [v for v in ordem if v in vs] + sorted(v for v in vs if v not in ordem)


def velocidades(a):
    m = _meta(a, "t_fan_speed")
    return sorted(int(v) for v in (m["valores"] if m else {}))


def faixa_c(a):
    m = _meta(a, "t_temp")
    if m and m["faixas"]:
        return m["faixas"][0][0], m["faixas"][0][1]
    return 16, 30


# ---------------- ar ----------------

def descrever_ar(a):
    e = a["estado"]
    fahrenheit = _int(a, "t_temp_type") == 1
    mn, mx = faixa_c(a)
    if fahrenheit:
        m = _meta(a, "t_temp")
        mn, mx = (m["faixas"][1] if m and len(m["faixas"]) > 1 else (61, 86))
    modos = _valores(a, "t_work_mode", [4, 2, 3, 0, 1])
    ventos = _valores(a, "t_fan_speed", [])
    noturno_botao = aceita(a, "t_sleep", 1)
    funcoes = []
    chaves = [k for k in tabelas.ORDEM_FUNCOES if k in e or (_meta(a, k) and _meta(a, k)["grava"])]
    for k, m in (a.get("meta") or {}).items():
        if k.startswith("t_") and m["grava"] and k not in chaves and k not in tabelas.INTERNAS:
            chaves.append(k)
    for k in chaves:
        if k == "t_sleep" and noturno_botao:
            continue
        m = _meta(a, k)
        funcoes.append({"chave": k, "nome": tabelas.funcao(k, m["nome"] if m else None),
                        "ligado": _int(a, k) != 0, "grava": grava(a, k)})
    falhas = [{"nome": tabelas.FALHAS_AR.get(k, k), "chave": k}
              for k in e if k.startswith("f_e_") and k != "f_e_push" and _int(a, k) != 0]
    return {
        "ligado": _int(a, "t_power") != 0,
        "ambiente": _num(a, "f_temp_in"),
        "alvo": _int(a, "t_temp", padrao=24),
        "unidade": "°F" if fahrenheit else "°C",
        "faixa": [mn, mx],
        "modo": {"valor": _int(a, "t_work_mode", padrao=-1), "nome": tabelas.modo(_int(a, "t_work_mode", padrao=-1))},
        "modos": [{"valor": v, "nome": tabelas.modo(v)} for v in modos] if "t_work_mode" in e else [],
        "vento": {"valor": _int(a, "t_fan_speed", padrao=-1), "nome": tabelas.ventilador(_int(a, "t_fan_speed", padrao=-1), a["tipo"])},
        "ventos": [{"valor": v, "nome": tabelas.ventilador(v, a["tipo"])} for v in ventos] if "t_fan_speed" in e else [],
        "noturno": _int(a, "t_sleep") != 0,
        "noturnoBotao": noturno_botao,
        "funcoes": funcoes,
        "aceitaBipe": grava(a, "t_beep"),
        "unidadeGrava": grava(a, "t_temp_type"),
        "grava": {k: grava(a, k) for k in ("t_power", "t_temp", "t_work_mode", "t_fan_speed")},
        "umidade": _num(a, "f_humidity"),
        "potencia": _num(a, "f_power_display"),
        "filtro": _int(a, "f_filter") != 0 if "f_filter" in e else None,
        "falhas": falhas,
    }


# ---------------- lavadora ----------------

def descrever_lavadora(a, agora=None):
    agora = agora or int(time.time())
    status = _int(a, "machine_status", "DeviceStatus", padrao=-1)
    fase = _int(a, "Current_program_phase", "CurrentProgramPhase")
    programa = _int(a, "Selected_program_ID", "Program")
    restante = _int(a, "Selected_program_remaining_time_in_minutes", "Remaining_time_in_minutes")
    total = _int(a, "Selected_program_total_running_time_in_minutes", "Selected_program_total_time_in_minutes")
    codigo = _int(a, "error_code")
    erro = status == 4 or codigo != 0
    pausada = status == 3
    rodando = status == 2 or pausada
    programado = rodando and fase == 11
    concluido = fase == 10
    progresso = 0.0
    if rodando and total > 0:
        progresso = max(0.0, min(1.0, 1 - restante / total))
    elif concluido:
        progresso = 1.0
    agua = energia = None
    if "Water_consumption_int" in a["estado"]:
        agua = _int(a, "Water_consumption_int") + _int(a, "Water_consumption_decimal") / 100.0
    if "Electricit_consumption_int" in a["estado"]:
        energia = _int(a, "Electricit_consumption_int") + _int(a, "Electricit_consumption_decimal") / 100.0

    if not a["online"]:
        titulo = "Desconectada"
    elif erro:
        titulo = "Erro %d" % codigo if codigo else "Erro"
    elif pausada:
        titulo = "Pausada"
    elif programado:
        titulo = tabelas.FASES[11]
    elif rodando:
        titulo = tabelas.FASES.get(fase) or "Em funcionamento"
    elif concluido:
        titulo = "Concluída"
    else:
        titulo = "Ligada" if status == 1 else "Em espera"

    p = []
    if "temperature" in a["estado"]:
        p.append(["Temperatura", tabelas.temperatura_lavagem(_int(a, "temperature"))])
    if "Spin_speed_rpm" in a["estado"]:
        p.append(["Centrifugação", tabelas.centrifugacao(_int(a, "Spin_speed_rpm"))])
    if "RinseNum" in a["estado"]:
        ex = _int(a, "ExtraRinseNum")
        p.append(["Enxágues", str(_int(a, "RinseNum")) + (" + %d extra" % ex if ex > 0 else "")])
    if "dry_time" in a["estado"]:
        p.append(["Secagem", tabelas.secagem(_int(a, "dry_time"))])
    if "Soil_Lever" in a["estado"] and _int(a, "SoilLeverFlag") == 0:
        p.append(["Sujeira", tabelas.sujeira(_int(a, "Soil_Lever"))])
    h, mm = _int(a, "DelayEndTime"), _int(a, "DelayEndTime_Minute")
    if h > 0 or mm > 0 or programado:
        p.append(["Início programado", "%d h %02d" % (h, mm)])
    for k in ("Door_lock_status", "DoorLockStatus", "door_lock"):
        if k in a["estado"]:
            p.append(["Porta", tabelas.PORTA.get(_int(a, k), "--")])
            break
    for k in ("Child_lock", "ChildLockActive", "Child_lock_setting_status"):
        if k in a["estado"]:
            p.append(["Trava infantil", "ativa" if _int(a, k) else "desativada"])
            break
    avisos = []
    if _int(a, "DrumCleanFlag") == 1:
        avisos.append("Hora de limpar o tambor")
    if erro and codigo:
        avisos.append("Erro %d" % codigo)
    termina = agora + restante * 60 if rodando and restante > 0 else None
    return {
        "titulo": titulo, "status": status, "rodando": rodando, "pausada": pausada, "programado": programado,
        "concluido": concluido, "erro": erro, "codigoErro": codigo,
        "programa": {"id": programa, "nome": tabelas.programa(programa)},
        "fase": {"id": fase, "nome": tabelas.FASES.get(fase, "")},
        "restante": restante, "total": total, "progresso": progresso, "terminaEm": termina,
        "agua": agua, "energia": energia, "parametros": p, "avisos": avisos,
    }


def propriedades(a):
    """Todas as propriedades com o nome do modelo e o nome do valor, quando a nuvem diz."""
    r = []
    meta = a.get("meta") or {}
    for k in sorted(a["estado"], key=str.lower):
        v = a["estado"][k]
        m = meta.get(k)
        r.append({"chave": k, "nome": (m or {}).get("nome") or "", "valor": v,
                  "valorNome": ((m or {}).get("valores") or {}).get(str(v), "") if v is not None else ""})
    return r


# ---------------- preferencias ----------------

CENAS_PADRAO = [
    {"nome": "Dormir", "icone": "lua", "desligar": False, "modo": -1, "temp": 24, "vento": -2, "noturno": 1, "duracao": 0},
    {"nome": "Gelar rápido", "icone": "floco", "desligar": False, "modo": 2, "temp": 18, "vento": -3, "noturno": 0, "duracao": 0},
    {"nome": "Econômico", "icone": "folha", "desligar": False, "modo": 5, "temp": 25, "vento": -1, "noturno": -1, "duracao": 0},
]

PREF_PADRAO = {
    "alertaFalha": True, "alertaDesconectado": True, "alertaQuente": True, "alertaLavadoraFim": True,
    "limiteQuente": 28, "potenciaFrio": 1100, "potenciaEco": 1100, "potenciaSeco": 600, "potenciaVentilar": 60,
    "tarifa": 0.85, "retornoModo": 2, "retornoTemp": 24, "retornoVento": 2, "semBipe": True, "cenas": CENAS_PADRAO,
}


def preferencias():
    p = banco.ler_kv("preferencias") or {}
    dados = dict(PREF_PADRAO)
    dados.update(p.get("dados") or {})
    return {"versao": p.get("versao", 0), "atualizado": p.get("atualizado", 0), "origem": p.get("origem", ""), "dados": dados}


def gravar_preferencias(dados, origem):
    atual = preferencias()
    novo = dict(atual["dados"])
    for k, v in (dados or {}).items():
        if k in PREF_PADRAO:
            novo[k] = v
    r = {"versao": atual["versao"] + 1, "atualizado": int(time.time()), "origem": origem, "dados": novo}
    banco.gravar_kv("preferencias", r)
    from . import servico
    servico.avisar_mudanca()
    return preferencias()


def potencia(p, modo):
    return {0: p["potenciaVentilar"], 3: p["potenciaSeco"], 5: p["potenciaEco"]}.get(modo, p["potenciaFrio"])


def comando_da_cena(cena, a):
    """As propriedades da cena para este ar, ou None se o modelo nao aceita alguma escolha (CenaCasa.Comando)."""
    p = {}
    if cena.get("desligar"):
        return {"t_power": "0"} if aceita(a, "t_power", 0) else None
    if aceita(a, "t_power", 1):
        p["t_power"] = 1
    modo = int(cena.get("modo", -1))
    if modo >= 0:
        if not aceita(a, "t_work_mode", modo):
            return None
        p["t_work_mode"] = modo
    temp = int(cena.get("temp", -1))
    if temp > 0:
        mn, mx = faixa_c(a)
        c = max(mn, min(mx, temp))
        t = round(c * 9 / 5 + 32) if _int(a, "t_temp_type") == 1 else c
        if not aceita(a, "t_temp", t):
            return None
        p["t_temp"] = t
    vento = int(cena.get("vento", -1))
    if vento != -1:
        vs = [v for v in velocidades(a) if v != 0]
        v = vento if vento >= 0 else (vs[0] if vento == -2 and vs else vs[-1] if vento == -3 and vs else -1)
        if v >= 0 and aceita(a, "t_fan_speed", v):
            p["t_fan_speed"] = v
        elif vento >= 0:
            return None
    noturno = int(cena.get("noturno", -1))
    if noturno >= 0:
        if not aceita(a, "t_sleep", noturno):
            return None
        p["t_sleep"] = noturno
    return {k: str(v) for k, v in p.items()} or None


def comando_de_retorno(a):
    p = preferencias()["dados"]
    return comando_da_cena({"modo": p["retornoModo"], "temp": p["retornoTemp"], "vento": p["retornoVento"], "noturno": 0}, a)


# ---------------- consumo ----------------

CONTINUO = 180   # s: lacuna maior e tempo sem leitura, nao conta (HistoricoDoAr.Continuo)


def consumo_ar(puid, dias):
    """Horas ligado, kWh e custo ESTIMADOS por dia (HistoricoDoAr.PorDia)."""
    p = preferencias()["dados"]
    hoje = datetime.now().date()
    de = hoje - timedelta(days=dias - 1)
    inicio = int(datetime.combine(de, datetime.min.time()).timestamp())
    agora = int(time.time())
    d = {(de + timedelta(days=i)).isoformat(): [0.0, 0.0] for i in range(dias)}
    l = banco.linhas("SELECT t, ligado, modo FROM amostras_ar WHERE puid=? AND t >= ? ORDER BY t", (puid, inicio))
    for i, s in enumerate(l):
        if not s["ligado"]:
            continue
        fim = l[i + 1]["t"] if i + 1 < len(l) else s["t"] + 60
        fim = min(fim, s["t"] + CONTINUO, agora)
        ini = s["t"]
        while ini < fim:
            dia = datetime.fromtimestamp(ini).date()
            corte = min(fim, int(datetime.combine(dia + timedelta(days=1), datetime.min.time()).timestamp()))
            h = (corte - ini) / 3600
            if dia.isoformat() in d:
                d[dia.isoformat()][0] += h
                d[dia.isoformat()][1] += h * potencia(p, s["modo"]) / 1000
            ini = corte
    return [{"dia": k, "horas": round(v[0], 3), "kwh": round(v[1], 3), "custo": round(v[1] * p["tarifa"], 2)}
            for k, v in d.items()]


def consumo_lavadora_dias(puid, dias):
    """kWh, custo, agua e ciclos por dia, pelo inicio do ciclo (mesmo formato do ar)."""
    p = preferencias()["dados"]
    hoje = datetime.now().date()
    de = hoje - timedelta(days=dias - 1)
    inicio = int(datetime.combine(de, datetime.min.time()).timestamp())
    d = {(de + timedelta(days=i)).isoformat(): {"kwh": 0.0, "agua": 0.0, "ciclos": 0, "horas": 0.0} for i in range(dias)}
    for c in banco.linhas("SELECT inicio, fim, agua, energia FROM ciclos WHERE puid=? AND inicio >= ?", (puid, inicio)):
        k = datetime.fromtimestamp(c["inicio"]).date().isoformat()
        if k in d:
            d[k]["kwh"] += c["energia"] or 0
            d[k]["agua"] += c["agua"] or 0
            d[k]["ciclos"] += 1
            d[k]["horas"] += (c["fim"] - c["inicio"]) / 3600
    return [{"dia": k, "kwh": round(v["kwh"], 3), "custo": round(v["kwh"] * p["tarifa"], 2), "agua": round(v["agua"], 1),
             "ciclos": v["ciclos"], "horas": round(v["horas"], 3)} for k, v in d.items()]


def consumo_lavadora(puid, meses):
    p = preferencias()["dados"]
    r = []
    hoje = datetime.now().replace(day=1)
    for i in range(meses - 1, -1, -1):
        y, m = hoje.year, hoje.month - i
        while m <= 0:
            m += 12
            y -= 1
        ini = int(datetime(y, m, 1).timestamp())
        fim = int(datetime(y + (m == 12), m % 12 + 1, 1).timestamp())
        x = banco.con().execute(
            "SELECT COUNT(*) n, COALESCE(SUM(agua),0) agua, COALESCE(SUM(energia),0) energia, "
            "COALESCE(SUM(fim-inicio),0) seg FROM ciclos WHERE puid=? AND inicio>=? AND inicio<?", (puid, ini, fim)).fetchone()
        r.append({"mes": "%04d-%02d" % (y, m), "ciclos": x["n"], "agua": round(x["agua"], 1),
                  "kwh": round(x["energia"], 2), "horas": round(x["seg"] / 3600, 1),
                  "custo": round(x["energia"] * p["tarifa"], 2)})
    return r
