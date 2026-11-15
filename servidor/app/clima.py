"""Clima (Open-Meteo, gratuito e sem chave) e a sugestao de conforto para o ar.

O navegador nao fala com a Open-Meteo (a CSP so deixa 'self'): o servidor busca,
guarda 15 minutos em memoria e entrega pronto.
"""

import threading
import time

import httpx

URL_PREVISAO = "https://api.open-meteo.com/v1/forecast"
URL_CIDADES = "https://geocoding-api.open-meteo.com/v1/search"
VALIDADE = 15 * 60

_trava = threading.Lock()
_cache = {}     # (lat, lon) -> (instante, dados)

# Codigos WMO da Open-Meteo agrupados em poucas descricoes.
_CODIGOS = ((0, "Céu limpo", "sol"), (2, "Parcialmente nublado", "sol-nuvem"), (3, "Nublado", "nuvem"),
            (48, "Neblina", "nuvem"), (57, "Garoa", "chuva"), (67, "Chuva", "chuva"), (77, "Neve", "nuvem"),
            (82, "Pancadas de chuva", "chuva"), (86, "Neve", "nuvem"), (99, "Tempestade", "chuva"))


def descricao(codigo):
    """(texto, icone) de um codigo WMO."""
    try:
        c = int(codigo)
    except (TypeError, ValueError):
        return "", "nuvem"
    for limite, texto, icone in _CODIGOS:
        if c <= limite:
            return texto, icone
    return "", "nuvem"


def arredondar(v, casas=1):
    return None if v is None else round(float(v), casas)


def _normalizar(j):
    """Do JSON da Open-Meteo para o que o painel desenha."""
    atual = j.get("current") or {}
    texto, icone = descricao(atual.get("weather_code"))
    r = {"agora": {"temp": arredondar(atual.get("temperature_2m")), "umidade": arredondar(atual.get("relative_humidity_2m"), 0),
                   "texto": texto, "icone": icone}, "horas": [], "dias": []}
    h = j.get("hourly") or {}
    tempos = h.get("time") or []
    inicio = atual.get("time") or ""
    # Proximas 6 horas a partir da hora cheia seguinte.
    for i, t in enumerate(tempos):
        if t > inicio and len(r["horas"]) < 6:
            tx, ic = descricao((h.get("weather_code") or [None] * len(tempos))[i])
            r["horas"].append({"hora": t[11:16], "temp": arredondar(h["temperature_2m"][i]), "icone": ic, "texto": tx})
    d = j.get("daily") or {}
    dias = d.get("time") or []
    hoje = (atual.get("time") or "")[:10]
    medias = []
    for i, dia in enumerate(dias):
        mx, mn = d["temperature_2m_max"][i], d["temperature_2m_min"][i]
        if dia < hoje:
            if mx is not None and mn is not None:
                medias.append((mx + mn) / 2)
            continue
        if len(r["dias"]) < 5:
            tx, ic = descricao((d.get("weather_code") or [None] * len(dias))[i])
            r["dias"].append({"dia": dia, "max": arredondar(mx), "min": arredondar(mn), "icone": ic, "texto": tx})
    r["mediaRecente"] = arredondar(sum(medias) / len(medias)) if medias else None
    r["maxHoje"] = r["dias"][0]["max"] if r["dias"] else None
    return r


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
            "latitude": chave[0], "longitude": chave[1], "timezone": "auto", "past_days": 3, "forecast_days": 5,
            "current": "temperature_2m,relative_humidity_2m,weather_code", "hourly": "temperature_2m,weather_code",
            "daily": "temperature_2m_max,temperature_2m_min,weather_code"})
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


def sugestao(dentro, fora, umidade, media_recente, max_hoje, hora, ligado):
    """Conforto adaptativo (ASHRAE 55): T_neutra = 0,31 x media externa recente + 17,8.

    Devolve {acao, titulo, texto, cena?}. 'cena' e o que o botao Aplicar manda ao ar
    (mesmo formato das cenas, entregue a casa.comando_da_cena).
    Sem temperatura de dentro ou de fora devolve None.
    """
    if dentro is None or fora is None:
        return None
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
    if abs(dentro - neutra) <= 1 and fora < dentro and (umidade is None or umidade < 70):
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
    alvo = int(min(26, max(23, round(neutra))))
    if umidade is not None and umidade >= 70 and fora <= 27:
        return {"acao": "seco", "titulo": "Desumidificar a %d °C" % alvo,
                "texto": "Umidade em %.0f%%. O modo seco incomoda menos que o frio." % umidade,
                "cena": {"modo": 3, "temp": alvo, "vento": -1, "noturno": -1}}
    cena = {"modo": 2, "temp": alvo, "vento": -1, "noturno": 1 if noite else -1}
    return {"acao": "frio", "titulo": "Frio a %d °C%s" % (alvo, " no modo dormir" if noite else ""),
            "texto": "Conforto estimado em %.1f °C (dentro %.0f °C, fora %.0f °C)." % (neutra, dentro, fora), "cena": cena}
