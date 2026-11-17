"""Metricas do servidor via Prometheus: consultas fixas, sem PromQL do cliente, tolerancia a falhas (sem rede)."""

import os
import unittest
from unittest import mock

from comum import Base  # noqa: F401  (primeiro: prepara o ambiente)

import httpx  # noqa: E402

from app import metricas  # noqa: E402


class Resposta:
    def __init__(self, dados, status=200):
        self.dados, self.status = dados, status

    def raise_for_status(self):
        if self.status >= 400:
            raise httpx.HTTPStatusError("x", request=None, response=None)

    def json(self):
        return self.dados


def instantaneo(v):
    return Resposta({"status": "success", "data": {"result": [{"value": [1700000000, str(v)]}]}})


class Metricas(Base):
    def setUp(self):
        super().setUp()
        self._antes = os.environ.get("PROMETHEUS_URL")
        os.environ["PROMETHEUS_URL"] = "http://prometheus.interno:9090/"

    def tearDown(self):
        if self._antes is None:
            os.environ.pop("PROMETHEUS_URL", None)
        else:
            os.environ["PROMETHEUS_URL"] = self._antes
        super().tearDown()

    def get(self, u, **q):
        return self.cliente.get(u, headers=self.cab, params=q)

    def test_desligado_sem_url(self):
        os.environ.pop("PROMETHEUS_URL", None)
        self.assertEqual(self.get("/api/metricas").json(), {"disponivel": False})
        self.assertEqual(self.get("/api/metricas/serie", nome="cpu").status_code, 409)

    def test_pede_a_chave(self):
        self.assertEqual(self.cliente.get("/api/metricas").status_code, 401)
        self.assertEqual(self.cliente.get("/api/metricas/serie?nome=cpu").status_code, 401)

    def test_resumo_e_a_consulta_vai_so_ao_prometheus_configurado(self):
        with mock.patch.object(metricas.httpx, "get", return_value=instantaneo(42.5)) as g:
            r = self.get("/api/metricas").json()
        self.assertTrue(r["disponivel"])
        self.assertEqual(r["valores"]["cpu"], 42.5)
        self.assertEqual(set(r["valores"]), set(metricas.CONSULTAS))
        for chamada in g.call_args_list:
            self.assertTrue(chamada.args[0].startswith("http://prometheus.interno:9090/api/v1/query"))
        consultas = {c.kwargs["params"]["query"] for c in g.call_args_list}
        self.assertEqual(consultas, {q for q, _ in metricas.CONSULTAS.values()})   # so as fixas

    def test_metrica_ausente_vem_nula_e_tudo_fora_da_502(self):
        vazio = Resposta({"status": "success", "data": {"result": []}})
        with mock.patch.object(metricas.httpx, "get", return_value=vazio):
            self.assertIsNone(self.get("/api/metricas").json()["valores"]["temp"])
        with mock.patch.object(metricas.httpx, "get", side_effect=httpx.ConnectError("fora")):
            self.assertEqual(self.get("/api/metricas").status_code, 502)

    def test_serie(self):
        valores = Resposta({"status": "success", "data": {"result": [{"values": [[1700000000, "1.5"], [1700000060, "NaN"], [1700000120, "2"]]}]}})
        with mock.patch.object(metricas.httpx, "get", return_value=valores) as g:
            r = self.get("/api/metricas/serie", nome="ram", horas=6).json()
        self.assertEqual(r["pontos"], [[1700000000, 1.5], [1700000120, 2.0]])      # NaN descartado
        self.assertEqual((r["unidade"], r["horas"]), ("%", 6))
        self.assertEqual(g.call_args.kwargs["params"]["query"], metricas.CONSULTAS["ram"][0])

    def test_serie_recusa_nome_e_periodo_fora_da_lista(self):
        with mock.patch.object(metricas.httpx, "get") as g:
            self.assertEqual(self.get("/api/metricas/serie", nome="up{job='x'}").status_code, 404)
            self.assertEqual(self.get("/api/metricas/serie", nome="uptime").status_code, 404)
            self.assertEqual(self.get("/api/metricas/serie", nome="cpu", horas=5).status_code, 422)
        g.assert_not_called()

    def test_url_invalida_conta_como_desligado(self):
        os.environ["PROMETHEUS_URL"] = "file:///etc/passwd"
        self.assertEqual(self.get("/api/metricas").json(), {"disponivel": False})


if __name__ == "__main__":
    unittest.main()
