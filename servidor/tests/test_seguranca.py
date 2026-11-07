"""Validacao de entrada, cabecalhos e login do servidor da Casa (revisao para producao)."""

import time
import unittest
from unittest import mock

from comum import AR, Base  # primeiro: prepara o ambiente

from app import casa, main, servico  # noqa: E402
from app.connectlife import extrair_codigo, extrair_estado  # noqa: E402

META = {"t_power": {"grava": True, "valores": {}, "faixas": []},
        "t_temp": {"grava": True, "valores": {}, "faixas": [[16, 30]]},
        "t_trava": {"grava": False, "valores": {}, "faixas": []}}


class Cabecalhos(Base):
    def test_pagina_e_api_com_cabecalhos_de_protecao(self):
        for rota in ("/", "/api/saude"):
            r = self.cliente.get(rota)
            self.assertIn("frame-ancestors 'none'", r.headers["content-security-policy"])
            self.assertEqual(r.headers["x-frame-options"], "DENY")
            self.assertEqual(r.headers["x-content-type-options"], "nosniff")
            self.assertEqual(r.headers["referrer-policy"], "no-referrer")
        self.assertEqual(self.cliente.get("/api/saude").headers["cache-control"], "no-store")

    def test_corpo_grande_demais(self):
        with mock.patch.object(main, "CORPO_MAXIMO", 100):
            r = self.cliente.post("/api/historico/importar", headers=self.cab, content=b"x" * 500)
        self.assertEqual(r.status_code, 413)

    def test_sem_documentacao_publica(self):
        self.assertEqual(self.cliente.get("/openapi.json").status_code, 404)


class Preferencias(Base):
    def _put(self, dados):
        return self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": dados})

    def test_tipos_e_faixas(self):
        self.assertEqual(self._put({"tarifa": "abc"}).status_code, 422)
        self.assertEqual(self._put({"retornoTemp": 50}).status_code, 422)
        self.assertEqual(self._put({"alertaQuente": "sim"}).status_code, 422)
        self.assertEqual(self._put({"cenas": [{"nome": ""}]}).status_code, 422)
        self.assertEqual(self._put({"cenas": "x"}).status_code, 422)
        r = self._put({"tarifa": 0.9, "potenciaFrio": 1000.4, "desconhecida": 1})
        self.assertEqual(r.status_code, 200)
        d = r.json()["dados"]
        self.assertEqual(d["tarifa"], 0.9)
        self.assertEqual(d["potenciaFrio"], 1000)
        self.assertNotIn("desconhecida", d)

    def test_valor_ruim_nao_quebra_o_consumo(self):
        self._put({"tarifa": "abc"})
        self.assertEqual(self.cliente.get("/api/resumo-mes", headers=self.cab).status_code, 200)

    def test_cenas_validas_do_pc(self):
        cenas = [{"nome": "Noite", "icone": "lua", "desligar": False, "modo": -1, "temp": 24, "vento": -2,
                  "noturno": 1, "duracao": 60}]
        r = self._put({"cenas": cenas})
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["dados"]["cenas"], cenas)


class Entradas(Base):
    def test_importar_rejeita_amostra_ou_ciclo_invalido(self):
        sem_t = {"amostras": [{"puid": "p-ar", "amb": 20}]}
        self.assertEqual(self.cliente.post("/api/historico/importar", headers=self.cab, json=sem_t).status_code, 422)
        ao_contrario = {"ciclos": [{"puid": "p-lav", "inicio": 1_800_000_100, "fim": 1_800_000_000}]}
        self.assertEqual(self.cliente.post("/api/historico/importar", headers=self.cab, json=ao_contrario).status_code, 422)
        puid_ruim = {"amostras": [{"puid": "a b<script>", "t": 1_800_000_000}]}
        self.assertEqual(self.cliente.post("/api/historico/importar", headers=self.cab, json=puid_ruim).status_code, 422)

    def test_agendamento_fora_da_faixa_ou_de_aparelho_desconhecido(self):
        agora = int(time.time())
        r = self.cliente.post("/api/agendamentos", headers=self.cab,
                              json={"puid": "p-ar", "quando": agora - 3600, "props": {"t_power": "0"}, "nome": "desligar"})
        self.assertEqual(r.status_code, 400)
        servico._aparelhos = [dict(AR, meta=META)]
        r = self.cliente.post("/api/agendamentos", headers=self.cab,
                              json={"puid": "outro", "quando": agora + 60, "props": {"t_power": "0"}, "nome": "desligar"})
        self.assertEqual(r.status_code, 404)
        r = self.cliente.post("/api/agendamentos", headers=self.cab,
                              json={"puid": "p-ar", "quando": agora + 60, "props": {"t power": "0"}, "nome": "desligar"})
        self.assertEqual(r.status_code, 400)
        r = self.cliente.post("/api/agendamentos", headers=self.cab,
                              json={"puid": "p-ar", "quando": agora + 60, "props": {"t_power": "0"}, "nome": "desligar"})
        self.assertEqual(r.status_code, 200)

    def test_comando_so_com_o_que_o_ar_grava(self):
        servico._aparelhos = [dict(AR, meta=META)]
        enviados = []
        with mock.patch.object(servico, "enviar", lambda puid, props, sem_bipe=False: enviados.append(props)):
            def post(props):
                return self.cliente.post("/api/aparelhos/p-ar/comando", headers=self.cab, json={"props": props})
            self.assertEqual(post({"t_trava": "1"}).status_code, 400)
            self.assertEqual(post({"t_power": "liga"}).status_code, 400)
            self.assertEqual(post({}).status_code, 400)
            self.assertEqual(self.cliente.post("/api/aparelhos/nao-existe/comando", headers=self.cab,
                                               json={"props": {"t_power": "1"}}).status_code, 404)
            self.assertEqual(post({"t_power": "1", "t_temp": "24"}).status_code, 200)
        self.assertEqual(enviados, [{"t_power": "1", "t_temp": "24"}])


class LoginOAuth(Base):
    FINAL = "https://my.home-assistant.io/redirect/oauth?code=COD123&state=%s"

    def test_state_e_codigo_do_endereco(self):
        self.assertEqual(extrair_codigo(self.FINAL % "abc"), "COD123")
        self.assertEqual(extrair_estado(self.FINAL % "abc"), "abc")
        aninhado = "https://my.home-assistant.io/redirect/_change/?redirect=oauth%2F%3Fcode%3DCOD9%26state%3Dxyz"
        self.assertEqual(extrair_codigo(aninhado), "COD9")
        self.assertEqual(extrair_estado(aninhado), "xyz")
        self.assertIsNone(extrair_estado("COD123"))

    def test_login_de_outro_pedido_e_recusado(self):
        servico._estado_login = "certo"
        entradas = []
        with mock.patch.object(servico, "entrar_com_codigo", lambda code: entradas.append(code)):
            r = self.cliente.post("/api/conta/codigo", headers=self.cab, json={"colado": self.FINAL % "errado"})
            self.assertEqual(r.status_code, 400)
            r = self.cliente.post("/api/conta/codigo", headers=self.cab, json={"colado": self.FINAL % "certo"})
            self.assertEqual(r.status_code, 200)
        self.assertEqual(entradas, ["COD123"])

    def test_sem_login_aberto_nenhum_state_vale(self):
        servico._estado_login = None
        self.assertFalse(servico.estado_de_login_valido("qualquer"))


class ValidacaoDireta(unittest.TestCase):
    def test_preferencias_sem_objeto(self):
        with self.assertRaises(casa.PreferenciaInvalida):
            casa.validar_preferencias([1, 2])


if __name__ == "__main__":
    unittest.main()
