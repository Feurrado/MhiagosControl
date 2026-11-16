"""Clima (Open-Meteo, gratuito e sem chave) e a sugestao de conforto para o ar.

O navegador nao fala com a Open-Meteo (a CSP so deixa 'self'): o servidor busca,
guarda 15 minutos em memoria e entrega pronto.
"""

import threading
import time

import httpx

URL_PREVISAO = "https://api.open-meteo.com/v1/forecast"
URL_CIDADES = "https://geocoding-api.open-meteo.com/v1/search"
URL_AR = "https://air-quality-api.open-meteo.com/v1/air-quality"
VALIDADE_AR = 30 * 60
VALIDADE = 15 * 60

_trava = threading.Lock()
_cache = {}     # (lat, lon) -> (instante, dados)
_cache_ar = {}  # (lat, lon) -> (instante, dados)

# Codigos WMO da Open-Meteo: (ate este codigo, texto, icone). O icone de dia vira o de noite em _NOITE.
_CODIGOS = ((0, "Céu limpo", "sol"), (1, "Predomínio de sol", "sol-nuvem"), (2, "Parcialmente nublado", "sol-nuvem"),
            (3, "Nublado", "nuvem"), (48, "Neblina", "neblina"), (55, "Garoa", "garoa"), (57, "Garoa congelante", "garoa"),
            (63, "Chuva", "chuva"), (65, "Chuva forte", "chuva-forte"), (67, "Chuva congelante", "chuva"), (77, "Neve", "neve"),
            (81, "Pancadas de chuva", "chuva"), (82, "Pancadas fortes", "chuva-forte"), (86, "Neve", "neve"),
            (99, "Tempestade", "tempestade"))
_NOITE = {"sol": "lua", "sol-nuvem": "lua-nuvem"}

PASSADO = 7      # dias de historico
FUTURO = 10      # dias de previsao (contando hoje)
HORAS = 24       # horas na faixa por hora


def descricao(codigo, dia=True):
    """(texto, icone) de um codigo WMO; de noite, sol vira lua."""
    try:
        c = int(codigo)
    except (TypeError, ValueError):
        return "", "nuvem"
    for limite, texto, icone in _CODIGOS:
        if c <= limite:
            if not dia:
                texto = "Céu limpo" if c == 0 else texto
                icone = _NOITE.get(icone, icone)
            return texto, icone
    return "", "nuvem"


def arredondar(v, casas=1):
    return None if v is None else round(float(v), casas)


def _serie(bloco, nome, n):
    """A coluna 'nome' do bloco (hourly/daily) com o tamanho certo; None onde faltar."""
    v = bloco.get(nome) or []
    return list(v) + [None] * (n - len(v))


def _hora(iso):
    return (iso or "")[11:16]


def _normalizar(j):
    """Do JSON da Open-Meteo para o que o painel desenha (campos novos so se somam aos antigos)."""
    atual = j.get("current") or {}
    agora_iso = atual.get("time") or ""
    hoje = agora_iso[:10]
    dia_agora = atual.get("is_day", 1) != 0
    texto, icone = descricao(atual.get("weather_code"), dia_agora)

    h = j.get("hourly") or {}
    tempos = h.get("time") or []
    n = len(tempos)
    h_temp, h_cod, h_prob, h_dia, h_uv = (_serie(h, k, n) for k in
                                          ("temperature_2m", "weather_code", "precipitation_probability", "is_day", "uv_index"))
    uv_agora = None
    for i, t in enumerate(tempos):
        if t[:13] == agora_iso[:13]:
            uv_agora = h_uv[i]

    r = {"agora": {"temp": arredondar(atual.get("temperature_2m")), "umidade": arredondar(atual.get("relative_humidity_2m"), 0),
                   "texto": texto, "icone": icone, "dia": dia_agora,
                   "sensacao": arredondar(atual.get("apparent_temperature")),
                   "vento": arredondar(atual.get("wind_speed_10m"), 0), "rajada": arredondar(atual.get("wind_gusts_10m"), 0),
                   "direcao": arredondar(atual.get("wind_direction_10m"), 0), "pressao": arredondar(atual.get("pressure_msl"), 0),
                   "nuvens": arredondar(atual.get("cloud_cover"), 0), "chuva": arredondar(atual.get("precipitation")),
                   "uv": arredondar(uv_agora)},
         "horas": [], "dias": [], "passado": []}

    d = j.get("daily") or {}
    dias = d.get("time") or []
    m = len(dias)
    d_max, d_min, d_cod, d_mm, d_prob, d_nasc, d_por, d_uv, d_vento = (_serie(d, k, m) for k in (
        "temperature_2m_max", "temperature_2m_min", "weather_code", "precipitation_sum", "precipitation_probability_max",
        "sunrise", "sunset", "uv_index_max", "wind_speed_10m_max"))
    for i, dia in enumerate(dias):
        tx, ic = descricao(d_cod[i])
        x = {"dia": dia, "max": arredondar(d_max[i]), "min": arredondar(d_min[i]), "icone": ic, "texto": tx,
             "chuvaMm": arredondar(d_mm[i])}
        if dia < hoje:
            r["passado"].append(x)
        elif len(r["dias"]) < FUTURO:
            x.update(prob=arredondar(d_prob[i], 0), nascer=_hora(d_nasc[i]), por=_hora(d_por[i]),
                     uvMax=arredondar(d_uv[i]), ventoMax=arredondar(d_vento[i], 0))
            r["dias"].append(x)
    r["passado"] = r["passado"][-PASSADO:]

    # Proximas 24 horas a partir da hora cheia seguinte, com o nascer e o por do sol no meio (como no iPhone).
    for i, t in enumerate(tempos):
        if t > agora_iso and len(r["horas"]) < HORAS:
            tx, ic = descricao(h_cod[i], h_dia[i] != 0)
            r["horas"].append({"t": t, "hora": _hora(t), "temp": arredondar(h_temp[i]), "icone": ic, "texto": tx,
                               "prob": arredondar(h_prob[i], 0)})
    if r["horas"]:
        ini, fim = agora_iso, r["horas"][-1]["t"]
        eventos = [(iso, tipo) for iso, tipo in [(x, "nascer") for x in d_nasc] + [(x, "por") for x in d_por]
                   if iso and ini < iso <= fim]
        for iso, tipo in eventos:
            pos = next((k for k, x in enumerate(r["horas"]) if x["t"] > iso), len(r["horas"]))
            r["horas"].insert(pos, {"t": iso, "hora": _hora(iso), "tipo": tipo})

    medias = [(x["max"] + x["min"]) / 2 for x in r["passado"] if x["max"] is not None and x["min"] is not None]
    r["mediaRecente"] = arredondar(sum(medias) / len(medias)) if medias else None
    r["maxHoje"] = r["dias"][0]["max"] if r["dias"] else None
    r["destaques"] = _destaques(r)
    return r


def _destaques(r):
    """Uma ou duas frases como as do app Tempo: chuva chegando e comparacao com a semana."""
    l = []
    chovendo = (r["agora"].get("chuva") or 0) > 0
    proxima = next((x for x in r["horas"][:12] if (x.get("prob") or 0) >= 50), None)
    if proxima and not chovendo:
        l.append("Chuva provável por volta das %s (%d%%)." % (proxima["hora"], proxima["prob"]))
    elif chovendo:
        seca = next((x for x in r["horas"][:12] if x.get("prob") is not None and x["prob"] < 30), None)
        l.append("Chovendo agora" + (", deve parar por volta das %s." % seca["hora"] if seca else "."))
    maximas = [x["max"] for x in r["passado"] if x["max"] is not None]
    if maximas and r["maxHoje"] is not None:
        dif = r["maxHoje"] - sum(maximas) / len(maximas)
        if abs(dif) >= 2:
            l.append("Hoje a máxima fica %.0f° %s que a média dos últimos %d dias." % (abs(dif), "acima" if dif > 0 else "abaixo", len(maximas)))
    return l


def previsao(lat, lon, agora=None):
    """Clima do ponto (cache de 15 min; se a rede cair, devolve o ultimo que tinha, marcado velho)."""
    chave = (round(lat, 2), round(lon, 2))
    t = agora or time.time()
    with _trava:
        c = _cache.get(chave)
    if c and t - c[0] < VALIDADE:
        return dict(c[1], velho=False)
    try:
        r = httpx.get(URL_PREVISAO, timeout=8, params={
            "latitude": chave[0], "longitude": chave[1], "timezone": "auto", "past_days": PASSADO, "forecast_days": FUTURO,
            "current": "temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,"
                       "cloud_cover,pressure_msl,wind_speed_10m,wind_direction_10m,wind_gusts_10m",
            "hourly": "temperature_2m,weather_code,precipitation_probability,is_day,uv_index",
            "daily": "weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,"
                     "sunrise,sunset,uv_index_max,wind_speed_10m_max"})
        r.raise_for_status()
        dados = _normalizar(r.json())
    except (httpx.HTTPError, ValueError, KeyError, TypeError):
        if c:
            return dict(c[1], velho=True)
        return None
    dados["atualizado"] = int(t)
    with _trava:
        _cache[chave] = (t, dados)
    return dict(dados, velho=False)


# Faixas do AQI americano (EPA): ate este valor, nome e cor.
_AQI = ((50, "Boa", "#3EB55A"), (100, "Moderada", "#E5C62D"), (150, "Ruim para sensíveis", "#F39A2B"),
        (200, "Ruim", "#E5463D"), (300, "Muito ruim", "#9D4FD8"), (10_000, "Perigosa", "#7E1F35"))


def categoria_aqi(v):
    """(nome, cor) da faixa do AQI americano; None sem valor."""
    if v is None:
        return None
    for limite, nome, cor in _AQI:
        if v <= limite:
            return nome, cor
    return _AQI[-1][1:]


def _normalizar_ar(j):
    atual = j.get("current") or {}
    aqi = atual.get("us_aqi")
    if aqi is None:
        return None
    h = j.get("hourly") or {}
    tempos, valores = h.get("time") or [], h.get("us_aqi") or []
    agora = atual.get("time") or ""
    prox = [v for t, v in zip(tempos, valores, strict=False) if t > agora and v is not None][:24]
    nome, cor = categoria_aqi(aqi)
    pior = max(prox) if prox else None
    return {"aqi": int(round(aqi)), "categoria": nome, "cor": cor,
            "pm25": arredondar(atual.get("pm2_5")), "pm10": arredondar(atual.get("pm10")),
            "o3": arredondar(atual.get("ozone"), 0), "no2": arredondar(atual.get("nitrogen_dioxide"), 0),
            "pior24": int(round(pior)) if pior is not None else None,
            "categoriaPior": categoria_aqi(pior)[0] if pior is not None else None}


def qualidade_ar(lat, lon, agora=None):
    """Qualidade do ar (AQI americano da Open-Meteo, sem chave), cache de 30 min; None se nao houver."""
    chave = (round(lat, 2), round(lon, 2))
    t = agora or time.time()
    with _trava:
        c = _cache_ar.get(chave)
    if c and t - c[0] < VALIDADE_AR:
        return c[1]
    try:
        r = httpx.get(URL_AR, timeout=8, params={
            "latitude": chave[0], "longitude": chave[1], "timezone": "auto", "forecast_days": 2,
            "current": "us_aqi,pm2_5,pm10,ozone,nitrogen_dioxide", "hourly": "us_aqi"})
        r.raise_for_status()
        dados = _normalizar_ar(r.json())
    except (httpx.HTTPError, ValueError, KeyError, TypeError):
        return c[1] if c else None
    with _trava:
        _cache_ar[chave] = (t, dados)
    return dados


def cidades(q):
    """Busca de cidade pelo nome (ate 6 resultados)."""
    q = (q or "").strip()
    if len(q) < 2:
        return []
    try:
        r = httpx.get(URL_CIDADES, timeout=8, params={"name": q, "count": 6, "language": "pt", "format": "json"})
        r.raise_for_status()
        achados = r.json().get("results") or []
    except (httpx.HTTPError, ValueError):
        return []
    return [{"nome": ", ".join(x for x in (c.get("name"), c.get("admin1"), c.get("country_code")) if x),
             "lat": round(c["latitude"], 2), "lon": round(c["longitude"], 2)} for c in achados
            if "latitude" in c and "longitude" in c]


# ---- Controle do ar por faixas com histerese ----
# Mesma ideia do termostato de banda morta (generic_thermostat do Home Assistant, Climate React da Sensibo):
# um limite para entrar em cada modo e outro, afastado, para sair; entre os dois o ar fica como esta.
CONFORTO_MIN, CONFORTO_MAX = 23, 26      # faixa do alvo do frio
FRIO_ENTRA = 1.5       # entra no frio com o quarto este tanto acima do alvo...
FRIO_SAI = 1.0         # ...e so sai com o quarto este tanto ABAIXO do alvo (no meio, o proprio ar segura o alvo)
TETO_VENTILAR = 28     # acima disto nunca fica so no ventilador
# No modo seco este aparelho trava o alvo em 22 °C e nao aceita mudar. Por isso o seco nunca recebe
# temperatura e e tratado como um ciclo com piso: pode esfriar o quarto so ate SECO_SOBRE abaixo do alvo
# de conforto (o "overcool to dehumidify" dos termostatos com controle de umidade, limitado a ~3 °F).
SECO_TRAVA = 22
SECO_ENTRA_UMIDADE, SECO_SAI_UMIDADE = 70, 60
SECO_SOBRE = 1.5       # quanto o seco pode esfriar abaixo do alvo de conforto
SECO_CURSO = 1.5       # so entra com pelo menos esta folga acima do piso, ou seja, do alvo para cima
SECO_QUENTE_ENTRA, SECO_QUENTE_SAI = 1.5, 2.5   # quarto quente demais para o seco: o frio vem primeiro
SECO_FORA_ENTRA, SECO_FORA_SAI = 27, 28          # calor la fora: o seco nao da conta


def _c(v):
    """28.5 -> "28,5" e 28.0 -> "28" (textos em portugues)."""
    return ("%.1f" % v).rstrip("0").rstrip(".").replace(".", ",")


def piso_do_seco(alvo):
    """Ate onde o seco pode esfriar o quarto: SECO_SOBRE abaixo do conforto, e nunca colado na trava."""
    return max(alvo - SECO_SOBRE, SECO_TRAVA + 0.5)


def _usar_seco(dentro, fora, umidade, alvo, em_seco):
    if umidade is None:
        return False
    piso = piso_do_seco(alvo)
    if em_seco:   # fica ate secar, chegar no piso ou esquentar demais
        return (umidade > SECO_SAI_UMIDADE and dentro > piso and dentro < alvo + SECO_QUENTE_SAI
                and fora <= SECO_FORA_SAI)
    return (umidade >= SECO_ENTRA_UMIDADE and dentro >= piso + SECO_CURSO and dentro < alvo + SECO_QUENTE_ENTRA
            and fora <= SECO_FORA_ENTRA)


def sugestao(dentro, fora, umidade, media_recente, max_hoje, hora, ligado, umidade_dentro=None, modo_atual=None):
    """Conforto adaptativo (ASHRAE 55): T_neutra = 0,31 x media externa recente + 17,8.

    A escolha do modo tem histerese pelo modo em que o ar ja esta (modo_atual, t_work_mode):
    - Frio: entra com o quarto FRIO_ENTRA acima do alvo (ou acima de TETO_VENTILAR, ou a noite) e fica
      ate o quarto cair FRIO_SAI abaixo do alvo. No meio quem regula e o termostato do proprio ar.
    - Seco: so com a umidade de dentro medida pelo ar (umidade_dentro); a de fora nao serve para isso.
      Entra com umidade >= 70% e o quarto entre o alvo e alvo + 1,5 (mais quente que isso, o frio
      vem primeiro); fica ate a umidade cair a 60%, o quarto chegar no piso (alvo - 1,5) ou esquentar a
      alvo + 2,5. Nunca manda temperatura: o aparelho trava o seco em 22 °C.
    - Ventilar: o que sobra com o ar ligado ou com o quarto pouco acima do alvo; desligado e mais fresco
      que o alvo, nao precisa do ar.

    Devolve {acao, titulo, texto, cena?}. 'cena' e o que o botao Aplicar manda ao ar
    (mesmo formato das cenas, entregue a casa.comando_da_cena).
    Sem temperatura de dentro ou de fora devolve None.
    """
    if dentro is None or fora is None:
        return None
    # So a umidade de DENTRO decide alguma coisa. A de fora (umidade) nao diz como esta o quarto: com chuva
    # marca 94% enquanto o quarto com o ar ligado esta em 62%. Sem a medida de dentro, o seco nunca e escolhido.
    umidade = umidade_dentro
    media = media_recente if media_recente is not None else fora
    neutra = round(0.31 * media + 17.8, 1)
    quente_depois = max_hoje is not None and max_hoje >= 30
    if dentro > neutra + 1 and 16 <= fora <= 24 and dentro - fora >= 2 and not quente_depois:
        if not ligado:
            return {"acao": "janela", "titulo": "Abra as janelas",
                    "texto": "Lá fora está %.0f °C e dentro %.0f °C. Dá para refrescar sem ligar o ar." % (fora, dentro)}
        return {"acao": "desligar", "titulo": "Desligue e abra as janelas",
                "texto": "Lá fora está %.0f °C e dentro %.0f °C, sem previsão de calor forte hoje." % (fora, dentro),
                "cena": {"desligar": True}}
    em_frio, em_seco = bool(ligado) and modo_atual == 2, bool(ligado) and modo_atual == 3
    # No seco quem decide ficar ou sair e a faixa do seco, la embaixo (senao ele passaria do piso).
    if not em_seco and abs(dentro - neutra) <= 1 and fora < dentro and (umidade is None or umidade < 70):
        return {"acao": "nada", "titulo": "Não precisa do ar",
                "texto": "Dentro %.0f °C, perto do conforto de %.1f °C, e lá fora está mais fresco." % (dentro, neutra)}
    if media < 18 or fora < 16:
        if abs(dentro - neutra) <= 1 and not ligado:
            return {"acao": "nada", "titulo": "Não precisa do ar",
                    "texto": "Dentro %.0f °C, no conforto de %.1f °C." % (dentro, neutra)}
        alvo = int(min(23, max(20, round(neutra))))
        return {"acao": "quente", "titulo": "Aquecer a %d °C" % alvo,
                "texto": "Está frio (fora %.0f °C). Conforto estimado em %.1f °C." % (fora, neutra),
                "cena": {"modo": 1, "temp": alvo, "vento": -1, "noturno": -1}}
    noite = hora >= 22 or hora < 6
    alvo = int(min(CONFORTO_MAX, max(CONFORTO_MIN, round(neutra))))
    if _usar_seco(dentro, fora, umidade, alvo, em_seco):
        # Sem "temp" (o alvo e fixo do aparelho) e sem mexer no ventilador (o seco regula sozinho).
        piso = piso_do_seco(alvo)
        return {"acao": "seco", "titulo": "Desumidificar",
                "texto": "Umidade em %.0f%%. Neste modo o aparelho trava em %d °C, então o automático sai dele quando "
                         "a umidade cair a %d%% ou o quarto chegar a %s °C." % (umidade, SECO_TRAVA, SECO_SAI_UMIDADE, _c(piso)),
                "cena": {"modo": 3, "temp": -1, "vento": -1, "noturno": -1}}
    # Desligado e mais fresco que o alvo: nao ha por que ligar o ar, nem a noite.
    if not ligado and dentro < alvo:
        return {"acao": "nada", "titulo": "Não precisa do ar",
                "texto": "Dentro %.0f °C, abaixo do conforto de %.1f °C." % (dentro, neutra)}
    # Frio: entra longe do alvo e so sai abaixo dele; no meio o termostato do proprio ar segura o alvo.
    quente = dentro >= alvo + FRIO_ENTRA or dentro > TETO_VENTILAR
    if noite or (dentro > alvo - FRIO_SAI if em_frio else quente):
        # Ventilador: alto enquanto o quarto esta longe do alvo (esfria mais rapido na primeira vez que liga),
        # baixo quando chega perto (mais quieto e economiza). No meio, mantem o que esta.
        falta = dentro - alvo
        vento = -3 if falta >= 2.5 else -2 if falta <= 1 else -1
        cena = {"modo": 2, "temp": alvo, "vento": vento, "noturno": 1 if noite else -1}
        return {"acao": "frio", "titulo": "Frio a %d °C%s" % (alvo, " no modo dormir" if noite else ""),
                "texto": "Conforto estimado em %.1f °C (dentro %.0f °C, fora %.0f °C)%s." % (
                    neutra, dentro, fora, ", ventilador alto para esfriar logo" if vento == -3 else ", ventilador baixo"
                    if vento == -2 else ""), "cena": cena}
    return {"acao": "ventilar", "titulo": "Ventilar",
            "texto": "Dentro %.0f °C, perto do conforto de %.1f °C: o ventilador basta." % (dentro, neutra),
            "cena": {"modo": 0, "temp": -1, "vento": -2, "noturno": -1}}
