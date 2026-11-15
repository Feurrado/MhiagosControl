"""Um JSON no formato da Open-Meteo, sintetico e deterministico, para os testes e o modo demo
(o servidor de verdade busca na Open-Meteo; aqui nada sai para a rede)."""

import math
from datetime import datetime, timedelta


def exemplo(agora=None, passado=7, futuro=10):
    agora = (agora or datetime.now()).replace(second=0, microsecond=0)
    agora = agora.replace(minute=agora.minute - agora.minute % 15)
    hoje = agora.replace(hour=0, minute=0)
    ini = hoje - timedelta(days=passado)
    iso = lambda d: d.strftime("%Y-%m-%dT%H:%M")
    horas = [ini + timedelta(hours=i) for i in range((passado + futuro) * 24)]
    dias = [ini + timedelta(days=i) for i in range(passado + futuro)]

    def temp(d):
        return round(23 + 5 * math.sin((d.hour - 9) / 24 * 2 * math.pi) + 1.5 * math.sin(d.toordinal() / 3), 1)

    def codigo(d):
        return (0, 1, 2, 3, 61, 80, 2, 0, 3, 51, 95, 1)[(d.toordinal() + d.hour // 6) % 12]

    def prob(d):
        return {61: 80, 80: 65, 51: 40, 95: 90, 3: 20}.get(codigo(d), 5)

    dia = lambda d: 1 if 6 <= d.hour < 18 else 0
    return {
        "current": {"time": iso(agora), "temperature_2m": temp(agora), "relative_humidity_2m": 78, "apparent_temperature": temp(agora) + 1.8,
                    "is_day": dia(agora), "precipitation": 0.0, "weather_code": codigo(agora), "cloud_cover": 64,
                    "pressure_msl": 1012.4, "wind_speed_10m": 11.3, "wind_direction_10m": 128, "wind_gusts_10m": 24.1},
        "hourly": {"time": [iso(h) for h in horas], "temperature_2m": [temp(h) for h in horas],
                   "weather_code": [codigo(h) for h in horas], "precipitation_probability": [prob(h) for h in horas],
                   "is_day": [dia(h) for h in horas], "uv_index": [round(max(0, 9 * math.sin((h.hour - 6) / 12 * math.pi)), 1) for h in horas]},
        "daily": {"time": [d.strftime("%Y-%m-%d") for d in dias],
                  "temperature_2m_max": [max(temp(d.replace(hour=k)) for k in range(24)) for d in dias],
                  "temperature_2m_min": [min(temp(d.replace(hour=k)) for k in range(24)) for d in dias],
                  "weather_code": [codigo(d.replace(hour=12)) for d in dias],
                  "precipitation_sum": [(0, 0, 0.4, 0, 12.5, 6.1, 0, 0, 1.2, 0.8, 22.0, 0)[d.toordinal() % 12] for d in dias],
                  "precipitation_probability_max": [prob(d.replace(hour=12)) for d in dias],
                  "sunrise": [iso(d.replace(hour=5, minute=14)) for d in dias], "sunset": [iso(d.replace(hour=18, minute=2)) for d in dias],
                  "uv_index_max": [9.1 for _ in dias], "wind_speed_10m_max": [18 + d.toordinal() % 7 for d in dias]},
    }
