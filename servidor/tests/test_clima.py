"""Clima e sugestao de conforto: regras puras, normalizacao da Open-Meteo e rotas (sem rede)."""

import unittest
from unittest import mock

from comum import Base, NuvemFalsa  # noqa: F401  (primeiro: prepara o ambiente)

from app import banco, casa, clima, servico  # noqa: E402

JSON_OM = {
    "current": {"time": "2026-11-15T15:00", "temperature_2m": 24.4, "relative_humidity_2m": 61, "weather_code": 2},
    "hourly": {"time": ["2026-11-15T15:00", "2026-11-15T16:00", "2026-11-15T17:00"], "temperature_2m": [24.4, 25, 26],
               "weather_code": [2, 3, 61]},
    "daily": {"time": ["2026-11-13", "2026-11-14", "2026-11-15", "2026-11-16"], "temperature_2m_max": [28, 30, 29, 31],
              "temperature_2m_min": [18, 20, 19, 21], "weather_code": [0, 0, 2, 80]},
}


class Regras(unittest.TestCase):
    def test_sem_dado_nao_sugere(self):
        self.assertIsNone(clima.sugestao(None, 20, 50, 20, 25, 12, False))
        self.assertIsNone(clima.sugestao(25, None, 50, 20, 25, 12, False))

    def test_fresco_la_fora_e_quente_dentro_manda_abrir_janela(self):
        s = clima.sugestao(27, 21, 50, 22, 26, 20, False)
        self.assertEqual(s["acao"], "janela")
        self.assertNotIn("cena", s)

    def test_ligado_com_fresco_la_fora_sugere_desligar(self):
        s = clima.sugestao(27, 21, 50, 22, 26, 20, True)
        self.assertEqual(s["cena"], {"desligar": True})

    def test_calor_previsto_nao_manda_desligar(self):
        self.assertEqual(clima.sugestao(27, 21, 50, 22, 33, 20, True)["acao"], "frio")

    def test_frio_sugere_aquecer(self):
        s = clima.sugestao(16, 10, 50, 12, 15, 9, False)
        self.assertEqual(s["acao"], "quente")
        self.assertEqual(s["cena"]["modo"], 1)
        self.assertTrue(20 <= s["cena"]["temp"] <= 23)

    def test_umido_sugere_seco(self):
        self.assertEqual(clima.sugestao(30, 26, 80, 26, 30, 14, False)["cena"]["modo"], 3)

    def test_noite_liga_o_modo_dormir(self):
        self.assertEqual(clima.sugestao(30, 28, 50, 27, 31, 23, False)["cena"]["noturno"], 1)
        self.assertEqual(clima.sugestao(30, 28, 50, 27, 31, 14, False)["cena"]["noturno"], -1)

    def test_alvo_do_frio_fica_entre_23_e_26(self):
        for media in (10, 22, 30, 40):
            s = clima.sugestao(31, 30, 40, media, 33, 14, False)
            if s["acao"] == "frio":
                self.assertTrue(23 <= s["cena"]["temp"] <= 26, s)


class Normalizacao(unittest.TestCase):
    def test_normaliza(self):
        r = clima._normalizar(JSON_OM)
        self.assertEqual(r["agora"]["temp"], 24.4)
        self.assertEqual([h["hora"] for h in r["horas"]], ["16:00", "17:00"])
        self.assertEqual(r["dias"][0]["dia"], "2026-11-15")
        self.assertEqual(r["mediaRecente"], round(((28 + 18) / 2 + (30 + 20) / 2) / 2, 1))
        self.assertEqual(r["maxHoje"], 29)

    def test_codigos(self):
        self.assertEqual(clima.descricao(0)[1], "sol")
        self.assertEqual(clima.descricao(63)[1], "chuva")
        self.assertEqual(clima.descricao(None)[1], "nuvem")


class Rotas(Base):
    def setUp(self):
        super().setUp()
        clima._cache.clear()
        servico._aparelhos = [dict(NuvemFalsa().lista[0])]

    def test_sem_local(self):
        self.assertEqual(self.cliente.get("/api/clima", headers=self.cab).json(), {"local": None})
        self.assertIsNone(self.cliente.get("/api/sugestao", headers=self.cab).json()["sugestao"])

    def test_local_invalido_recusado(self):
        r = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"local": {"nome": "X", "lat": 99, "lon": 0}}, "origem": "t"})
        self.assertEqual(r.status_code, 422)

    def test_clima_e_sugestao(self):
        banco.con()
        casa.gravar_preferencias({"local": {"nome": "Casa", "lat": -23.5, "lon": -46.6}}, "t")
        with mock.patch.object(clima, "previsao", return_value=clima._normalizar(JSON_OM) | {"velho": False}):
            r = self.cliente.get("/api/clima", headers=self.cab).json()
            self.assertEqual(r["clima"]["agora"]["temp"], 24.4)
            s = self.cliente.get("/api/sugestao", headers=self.cab).json()
            self.assertEqual(s["puid"], "p-ar")
            self.assertIsNotNone(s["sugestao"])

    def test_aplicar_sem_sugestao_da_409(self):
        self.assertEqual(self.cliente.post("/api/sugestao/aplicar", headers=self.cab).status_code, 409)

    def test_rotas_pedem_a_chave(self):
        for u in ("/api/clima", "/api/cidades?q=sao", "/api/sugestao"):
            self.assertEqual(self.cliente.get(u).status_code, 401)

    def test_previsao_do_mes(self):
        r = self.cliente.get("/api/resumo-mes", headers=self.cab).json()
        self.assertEqual(set(r["previsao"]), {"kwh", "custo", "mediaDia", "diasComUso", "faltam"})
        self.assertGreaterEqual(r["previsao"]["kwh"], r["atual"]["kwh"])



class Inicio(Base):
    def put(self, v):
        return self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"inicio": v}, "origem": "t"})

    def test_arranjo_valido_e_sem_repeticao(self):
        r = self.put({"ordem": ["b", "a", "b"], "ocultos": ["clima"]})
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["dados"]["inicio"], {"ordem": ["b", "a"], "ocultos": ["clima"]})

    def test_arranjo_invalido(self):
        self.assertEqual(self.put({"ordem": "a", "ocultos": []}).status_code, 422)
        self.assertEqual(self.put({"ordem": [1], "ocultos": []}).status_code, 422)
        self.assertEqual(self.put({"ordem": ["x"] * 33, "ocultos": []}).status_code, 422)
        self.assertEqual(self.put(None).status_code, 422)


if __name__ == "__main__":
    unittest.main()
