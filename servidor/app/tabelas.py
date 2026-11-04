"""Nomes que a nuvem nao manda: os mesmos do Mhiagos (TabelaDaLavadora, TabelaDoAr, T.CasaFuncao).

Manter em dia com src/CasaLavadora.cs, src/CasaAr.cs e src/CasaPage.cs.
"""

# Confirmados pelo Thiago no painel da maquina (025-1wj110009v0t).
PROGRAMAS_CONFIRMADOS = {7: "Algodão", 42: "Automático"}

# Provaveis, do connectlife-ha para a mesma familia de lava e secas.
PROGRAMAS_PROVAVEIS = {
    1: "Algodão + secagem", 2: "Sintético + secagem", 4: "Refrescar", 5: "Antialérgico",
    6: "Limpeza do tambor", 7: "Algodão", 8: "Sintético", 9: "Eco 40-60", 10: "Lã",
    11: "Rápido 15'", 12: "Misto", 14: "Centrifugar", 16: "Bebê", 18: "Roupa esportiva",
    19: "Edredom", 20: "Enxaguar e centrifugar", 21: "Delicadas", 22: "Lavar e secar 60'",
    24: "Camisas", 41: "Power 49'", 42: "Automático", 44: "Roupa de cama", 45: "Jeans",
    55: "Lavagem à mão",
}

FASES = {
    1: "Pesando a roupa", 2: "Pré-lavagem", 3: "Lavando", 4: "Enxaguando", 5: "Amaciando",
    6: "Antirrugas", 7: "Centrifugando", 8: "Secando", 9: "Arejando", 10: "Fim do programa",
    11: "Aguardando o início programado",
}

SECAGEM = {0: "Sem secagem", 1: "Para passar", 2: "Para vestir", 3: "Para guardar", 4: "Intensa"}
SUJEIRA = {1: "Leve", 2: "Média", 3: "Pesada"}
PORTA = {1: "destravada", 2: "travada", 3: "fechada"}

MODOS = {0: "Ventilar", 1: "Calor", 2: "Frio", 3: "Desumidificar", 4: "Automático", 5: "ECO"}
VENTO_009 = {0: "Auto", 5: "Mínima", 6: "Baixa", 7: "Média", 8: "Alta", 9: "Máxima"}
VENTO_008 = {2: "Baixa", 3: "Média", 4: "Alta"}

FUNCOES = {
    "t_eco": "Econômico", "t_super": "Turbo", "t_fan_mute": "Silencioso", "t_sleep": "Modo noturno",
    "t_up_down": "Oscilar na vertical", "t_left_right": "Oscilar na horizontal", "t_8heat": "Antifrio 8 °C",
    "t_purify": "Ionizador", "t_fresh_air": "Ar externo", "t_tms": "Modo inteligente",
    "t_dimmer": "Luz do painel", "t_beep": "Bipe ao receber comando",
}
# Ordem dos interruptores no cartao do ar (CartaoDoAr).
ORDEM_FUNCOES = ["t_sleep", "t_eco", "t_super", "t_fan_mute", "t_up_down", "t_left_right",
                 "t_8heat", "t_purify", "t_fresh_air", "t_tms", "t_dimmer"]
# Controladas por outros botoes, nunca viram interruptor.
INTERNAS = {"t_device_info", "t_temp_type", "t_power", "t_temp", "t_work_mode", "t_fan_speed", "t_beep"}

FALHAS_AR = {
    "f_e_incoiltemp": "falha no sensor da serpentina", "f_e_incom": "falha de comunicação interna",
    "f_e_ineeprom": "falha na memória da placa", "f_e_intemp": "falha no sensor de temperatura",
    "f_e_over_cold": "proteção contra congelamento", "f_e_over_hot": "proteção contra superaquecimento",
}


def programa(i):
    if i <= 0:
        return "Sem programa"
    return PROGRAMAS_CONFIRMADOS.get(i) or PROGRAMAS_PROVAVEIS.get(i) or "Programa %d" % i


def modo(v):
    return MODOS.get(v, "Modo %d" % v)


def ventilador(v, tipo):
    if tipo == "009" and v in VENTO_009:
        return VENTO_009[v]
    if tipo == "008" and v in VENTO_008:
        return VENTO_008[v]
    return "Auto" if v == 0 else "Velocidade %d" % v


def funcao(chave, nome_da_nuvem=None):
    return FUNCOES.get(chave) or nome_da_nuvem or chave


def temperatura_lavagem(v):
    return "Fria" if v <= 0 else "%d °C" % (v * 10)


def centrifugacao(v):
    return "Sem centrifugar" if v <= 0 else "%d rpm" % (v * 100)


def secagem(v):
    return SECAGEM.get(v, "%d min" % v)


def sujeira(v):
    return SUJEIRA.get(v, "Normal")


def ler_meta(resposta):
    """propertyList do modelo: {chave: {nome, grava, valores: {v: nome}, faixas: [[a, b]]}} (como MetaPropriedade)."""
    meta = {}
    for p in (resposta or {}).get("properties") or []:
        if not isinstance(p, dict):
            continue
        chave = str(p.get("propertyKey") or "")
        if not chave:
            continue
        m = {"nome": str(p.get("propertyName") or ""), "grava": "W" in str(p.get("propertyRW") or ""),
             "valores": {}, "faixas": []}
        lista = str(p.get("propertyValueList") or "").replace("，", ",").replace("\n", ",")
        for parte in lista.split(","):
            parte = parte.strip()
            if not parte:
                continue
            if "~" in parte:
                a, _, b = parte.partition("~")
                try:
                    m["faixas"].append([int(a), int(b)])
                except ValueError:
                    pass
                continue
            n, _, nome = parte.partition(":")
            try:
                m["valores"].setdefault(str(int(n)), nome.strip())
            except ValueError:
                pass
        meta[chave] = m
    return meta
