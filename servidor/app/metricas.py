"""Metricas do servidor (CPU, memoria, disco, rede, uptime) lidas do Prometheus da casa.

O Prometheus e o Grafana ficam so na rede interna: o painel NUNCA fala com eles. Quem consulta e este
servico, atras do mesmo login do app, e so com as consultas fixas da lista abaixo (o cliente escolhe um
nome, nunca escreve PromQL). Sem PROMETHEUS_URL no ambiente (.env), o recurso fica desligado.
Os nomes de metrica sao os do node_exporter.
"""

import logging
import os
import time

import httpx

log = logging.getLogger("metricas")

# nome -> (PromQL, unidade). Unidade: "%" percentual, "B/s" bytes por segundo, "s" segundos, "C" graus, "" numero.
CONSULTAS = {
    "cpu": ('100 - (avg(rate(node_cpu_seconds_total{mode="idle"}[2m])) * 100)', "%"),
    "ram": ("100 * (1 - sum(node_memory_MemAvailable_bytes) / sum(node_memory_MemTotal_bytes))", "%"),
    "disco": ('max(100 * (1 - node_filesystem_avail_bytes{mountpoint="/",fstype!~"tmpfs|overlay|squashfs"}'
              ' / node_filesystem_size_bytes{mountpoint="/",fstype!~"tmpfs|overlay|squashfs"}))', "%"),
    "rede_rx": ('sum(rate(node_network_receive_bytes_total{device!~"lo|veth.*|docker.*|br-.*"}[2m]))', "B/s"),
    "rede_tx": ('sum(rate(node_network_transmit_bytes_total{device!~"lo|veth.*|docker.*|br-.*"}[2m]))', "B/s"),
    "uptime": ("time() - max(node_boot_time_seconds)", "s"),
    # So sensores de CPU/placa (chip conhecido) e leituras plausiveis: o max() de TODOS os sensores pegava
    # sensores sem calibragem (acpitz, NVMe composto, ventoinha) que chegam a 100+ graus. Sem chip conhecido, cai no geral.
    "temp": ('max((node_hwmon_temp_celsius * on(chip) group_left() node_hwmon_chip_names{chip_name=~"coretemp|k10temp|zenpower|cpu_thermal|soc_thermal|bcm2835_thermal|pch_.*"}) > 0 < 95)'
             ' or max((node_hwmon_temp_celsius > 0) < 95)', "C"),
    "carga": ("max(node_load1)", ""),
}
# O que desenha serie (o uptime e so um numero).
COM_SERIE = ("cpu", "ram", "disco", "rede_rx", "rede_tx", "temp", "carga")
HORAS = (1, 6, 24, 168)
PONTOS = 120
TEMPO_LIMITE = 5


class MetricasErro(Exception):
    """O Prometheus nao respondeu ou respondeu algo que nao e dado."""


def url():
    u = (os.environ.get("PROMETHEUS_URL") or "").strip().rstrip("/")
    return u if u.startswith(("http://", "https://")) else ""


def grafana():
    """Endereco do Grafana, so para o link 'abrir o painel completo' (o painel nao o consulta)."""
    u = (os.environ.get("GRAFANA_URL") or "").strip()
    return u if u.startswith(("http://", "https://")) else ""


def _get(caminho, params):
    try:
        r = httpx.get(url() + caminho, params=params, timeout=TEMPO_LIMITE)
        r.raise_for_status()
        j = r.json()
    except (httpx.HTTPError, ValueError) as e:
        log.warning("prometheus indisponivel: %s", e)
        raise MetricasErro("Prometheus indisponível") from e
    if j.get("status") != "success":
        raise MetricasErro("Prometheus recusou a consulta")
    return j["data"]["result"]


def _numero(v):
    try:
        x = float(v)
    except (TypeError, ValueError):
        return None
    return None if x != x or x in (float("inf"), float("-inf")) else round(x, 3)


def instante(nome):
    """O valor de agora de uma consulta (None se o Prometheus nao tem a metrica)."""
    r = _get("/api/v1/query", {"query": CONSULTAS[nome][0]})
    return _numero(r[0]["value"][1]) if r else None


def resumo():
    """Os valores de agora de todas as consultas. Cada uma falha sozinha: o que nao existe vem nulo."""
    if not url():
        return {"disponivel": False}
    saida = {"disponivel": True, "grafana": grafana() or None, "agora": int(time.time()), "valores": {}}
    falhas = 0
    for nome in CONSULTAS:
        try:
            saida["valores"][nome] = instante(nome)
        except MetricasErro:
            saida["valores"][nome] = None
            falhas += 1
    if falhas == len(CONSULTAS):
        raise MetricasErro("Prometheus indisponível")
    return saida


def serie(nome, horas, agora=None):
    """Pontos [t, valor] das ultimas 'horas' horas, no maximo PONTOS."""
    fim = int(agora or time.time())
    inicio = fim - horas * 3600
    passo = max(15, horas * 3600 // PONTOS)
    r = _get("/api/v1/query_range", {"query": CONSULTAS[nome][0], "start": inicio, "end": fim, "step": passo})
    pontos = []
    for t, v in (r[0]["values"] if r else []):
        x = _numero(v)
        if x is not None:
            pontos.append([int(t), x])
    return {"nome": nome, "unidade": CONSULTAS[nome][1], "horas": horas, "passo": passo, "pontos": pontos}
