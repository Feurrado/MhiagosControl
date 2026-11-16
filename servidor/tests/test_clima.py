"""Clima e sugestao de conforto: regras puras, normalizacao da Open-Meteo e rotas (sem rede)."""

import unittest
from unittest import mock

from comum import Base, NuvemFalsa  # noqa: F401  (primeiro: prepara o ambiente)

from datetime import datetime  # noqa: E402

from app import banco, casa, clima, clima_exemplo, servico  # noqa: E402

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

    def test_umido_sugere_seco_sem_temperatura(self):
        # o aparelho trava o seco em 22 °C: a cena nao manda temperatura nem mexe no ventilador
        s = clima.sugestao(27, 26, 50, 26, 30, 14, False, umidade_dentro=80)
        self.assertEqual((s["acao"], s["cena"]["modo"], s["cena"]["temp"], s["cena"]["vento"]), ("seco", 3, -1, -1))
        self.assertIn("24,5 °C", s["texto"])

    def test_umidade_da_rua_nao_escolhe_o_seco(self):
        # chuva la fora (94%) e o ar sem sensor de umidade: o quarto pode estar em 62%, entao nada de seco
        s = clima.sugestao(25, 24, 94, 22.6, 29, 20, True, modo_atual=2)
        self.assertEqual(s["acao"], "nada")
        self.assertNotEqual(clima.sugestao(26, 27, 94, 26, 30, 14, True)["cena"]["modo"], 3)
        # e se estiver no seco (posto antes, ou a mao), o automatico sai dele
        s = clima.sugestao(25, 24, 94, 22.6, 29, 20, True, modo_atual=3)
        self.assertEqual((s["acao"], s["cena"]["modo"]), ("ventilar", 0))
        self.assertEqual(clima.sugestao(25, 24, 94, 22.6, 29, 23, True, modo_atual=3)["cena"]["modo"], 2)

    def test_quente_e_umido_esfria_primeiro(self):
        # 4 °C acima do alvo: o frio chega la mais rapido (e tambem seca); o seco entra perto do alvo
        self.assertEqual(clima.sugestao(30, 26, 50, 26, 30, 14, False, umidade_dentro=80)["cena"]["modo"], 2)
        self.assertEqual(clima.sugestao(27.5, 26, 50, 26, 30, 14, True, modo_atual=2, umidade_dentro=80)["cena"]["modo"], 2)
        self.assertEqual(clima.sugestao(27, 26, 50, 26, 30, 14, True, modo_atual=2, umidade_dentro=80)["cena"]["modo"], 3)

    def test_seco_nao_entra_perto_do_piso(self):
        # alvo 26, piso 24,5: abaixo do alvo o seco sairia logo, entao nem entra
        self.assertNotEqual(clima.sugestao(25.5, 27, 50, 26, 30, 14, True, umidade_dentro=80)["cena"]["modo"], 3)
        self.assertEqual(clima.sugestao(26, 27, 50, 26, 30, 14, True, umidade_dentro=80)["cena"]["modo"], 3)

    def test_seco_para_no_piso_e_nao_nos_22_do_aparelho(self):
        self.assertEqual(clima.sugestao(25, 27, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=80)["cena"]["modo"], 3)
        s = clima.sugestao(24.5, 27, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=80)
        self.assertEqual((s["acao"], s["cena"]["modo"]), ("ventilar", 0))
        # a noite sai para o frio no alvo (o ar fica parado ate o quarto passar dele)
        s = clima.sugestao(24.5, 27, 50, 26, 30, 23, True, modo_atual=3, umidade_dentro=80)
        self.assertEqual((s["cena"]["modo"], s["cena"]["temp"]), (2, 26))
        # com conforto de 23 °C o piso nao cola na trava de 22
        self.assertEqual(clima.piso_do_seco(23), 22.5)

    def test_seco_histerese_da_umidade(self):
        self.assertEqual(clima.sugestao(26, 26, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=65)["cena"]["modo"], 3)
        self.assertNotEqual(clima.sugestao(26, 26, 50, 26, 30, 14, True, umidade_dentro=65)["cena"]["modo"], 3)
        self.assertNotEqual(clima.sugestao(26, 26, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=60)["cena"]["modo"], 3)

    def test_seco_no_conforto_nao_vira_nao_precisa(self):
        # dentro do conforto e mais fresco la fora daria "nao precisa": no seco isso o deixaria passar do piso
        self.assertEqual(clima.sugestao(26, 25, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=65)["acao"], "seco")
        self.assertEqual(clima.sugestao(26, 25, 50, 26, 30, 14, True, modo_atual=2, umidade_dentro=65)["acao"], "nada")

    def test_seco_esquentando_volta_ao_frio(self):
        self.assertEqual(clima.sugestao(28.4, 27, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=80)["cena"]["modo"], 3)
        self.assertEqual(clima.sugestao(28.5, 27, 50, 26, 30, 14, True, modo_atual=3, umidade_dentro=80)["cena"]["modo"], 2)

    def test_frio_tem_histerese(self):
        # alvo 26: entra no frio a 27,5 e, ja no frio, fica ate o quarto cair a 25 (o proprio ar segura os 26)
        self.assertEqual(clima.sugestao(27, 31, 40, 27, 33, 14, True, modo_atual=0)["acao"], "ventilar")
        self.assertEqual(clima.sugestao(27.5, 31, 40, 27, 33, 14, True, modo_atual=0)["acao"], "frio")
        for dentro in (27, 26, 25.5):
            self.assertEqual(clima.sugestao(dentro, 31, 40, 27, 33, 14, True, modo_atual=2)["acao"], "frio")
        self.assertEqual(clima.sugestao(25, 31, 40, 27, 33, 14, True, modo_atual=2)["acao"], "ventilar")

    def test_desligado_e_fresco_nao_liga_o_ventilador(self):
        for hora in (14, 23):
            s = clima.sugestao(24, 31, 40, 27, 33, hora, False)
            self.assertEqual(s["acao"], "nada")
            self.assertNotIn("cena", s)
        self.assertEqual(clima.sugestao(24, 31, 40, 27, 33, 14, True)["acao"], "ventilar")

    def test_ventilar_quando_esta_perto_do_conforto(self):
        s = clima.sugestao(27.4, 26.8, 50, 27, 28, 14, True)
        self.assertEqual((s["acao"], s["cena"]["modo"], s["cena"]["vento"]), ("ventilar", 0, -2))

    def test_ventilador_alto_longe_do_alvo_e_baixo_perto(self):
        longe = clima.sugestao(31, 30, 40, 27, 33, 14, True)["cena"]
        perto = clima.sugestao(26.5, 31, 40, 27, 33, 14, True)["cena"]
        self.assertEqual(longe["vento"], -3)
        self.assertEqual(perto["vento"], -2)

    def test_muda_livremente_entre_frio_ventilar_e_seco(self):
        modos = {clima.sugestao(d, f, 50, 27, 33, 14, True, umidade_dentro=u)["cena"]["modo"]
                 for d, f, u in ((31, 30, 40), (27.4, 26.8, 50), (27, 26, 80))}
        self.assertEqual(modos, {2, 0, 3})

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


class Completo(unittest.TestCase):
    def setUp(self):
        self.r = clima._normalizar(clima_exemplo.exemplo(datetime(2026, 11, 15, 15, 40)))

    def test_quantidades(self):
        self.assertEqual(len(self.r["passado"]), 7)
        self.assertEqual(len(self.r["dias"]), 10)
        self.assertEqual(self.r["dias"][0]["dia"], "2026-11-15")
        self.assertEqual(self.r["passado"][-1]["dia"], "2026-11-14")
        self.assertEqual(len([x for x in self.r["horas"] if "tipo" not in x]), 24)

    def test_nascer_e_por_do_sol_na_faixa_em_ordem(self):
        eventos = [x for x in self.r["horas"] if "tipo" in x]
        self.assertEqual([(x["tipo"], x["hora"]) for x in eventos], [("por", "18:02"), ("nascer", "05:14")])
        ts = [x["t"] for x in self.r["horas"]]
        self.assertEqual(ts, sorted(ts))
        self.assertEqual(self.r["horas"][0]["hora"], "16:00")

    def test_detalhes_de_agora(self):
        a = self.r["agora"]
        for k in ("sensacao", "vento", "rajada", "direcao", "pressao", "nuvens", "uv"):
            self.assertIsNotNone(a[k], k)
        self.assertTrue(a["dia"])
        self.assertEqual(self.r["dias"][0]["nascer"], "05:14")

    def test_noite_troca_o_icone(self):
        self.assertEqual(clima.descricao(0, False), ("Céu limpo", "lua"))
        self.assertEqual(clima.descricao(2, False)[1], "lua-nuvem")
        self.assertEqual(clima.descricao(3, False)[1], "nuvem")
        noite = [x for x in self.r["horas"] if x.get("hora") == "23:00"][0]
        self.assertNotIn(noite["icone"], ("sol", "sol-nuvem"))

    def test_destaques(self):
        r = dict(self.r, agora=dict(self.r["agora"], chuva=0), maxHoje=40)
        r["horas"] = [{"hora": "17:00", "prob": 70}]
        d = clima._destaques(r)
        self.assertIn("17:00", d[0])
        self.assertIn("acima", d[1])
        r["agora"]["chuva"] = 1.2
        self.assertTrue(clima._destaques(r)[0].startswith("Chovendo agora"))


class QualidadeDoAr(unittest.TestCase):
    def test_categorias(self):
        self.assertEqual(clima.categoria_aqi(12)[0], "Boa")
        self.assertEqual(clima.categoria_aqi(51)[0], "Moderada")
        self.assertEqual(clima.categoria_aqi(180)[0], "Ruim")
        self.assertEqual(clima.categoria_aqi(999)[0], "Perigosa")
        self.assertIsNone(clima.categoria_aqi(None))

    def test_normaliza(self):
        j = {"current": {"time": "2026-11-15T15:00", "us_aqi": 42.4, "pm2_5": 8.12, "pm10": 15.0, "ozone": 61.7, "nitrogen_dioxide": 9.2},
             "hourly": {"time": ["2026-11-15T15:00", "2026-11-15T16:00", "2026-11-15T17:00"], "us_aqi": [42, 70, None]}}
        r = clima._normalizar_ar(j)
        self.assertEqual((r["aqi"], r["categoria"], r["pior24"], r["categoriaPior"]), (42, "Boa", 70, "Moderada"))
        self.assertEqual(r["pm25"], 8.1)
        self.assertIsNone(clima._normalizar_ar({"current": {}}))


class Rotas(Base):
    def setUp(self):
        super().setUp()
        clima._cache.clear()
        servico._aparelhos = [dict(NuvemFalsa().lista[0])]

    def test_sem_local(self):
        self.assertEqual(self.cliente.get("/api/clima", headers=self.cab).json(), {"local": None, "locais": []})
        self.assertIsNone(self.cliente.get("/api/sugestao", headers=self.cab).json()["sugestao"])

    def test_local_invalido_recusado(self):
        r = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"local": {"nome": "X", "lat": 99, "lon": 0}}, "origem": "t"})
        self.assertEqual(r.status_code, 422)

    def test_clima_e_sugestao(self):
        banco.con()
        casa.gravar_preferencias({"local": {"nome": "Casa", "lat": -23.5, "lon": -46.6}}, "t")
        with mock.patch.object(clima, "previsao", return_value=clima._normalizar(JSON_OM) | {"velho": False}), \
             mock.patch.object(clima, "qualidade_ar", return_value=None):
            r = self.cliente.get("/api/clima", headers=self.cab).json()
            self.assertEqual(r["clima"]["agora"]["temp"], 24.4)
            s = self.cliente.get("/api/sugestao", headers=self.cab).json()
            self.assertEqual(s["puid"], "p-ar")
            self.assertIsNotNone(s["sugestao"])

    def test_outras_cidades(self):
        banco.con()
        casa.gravar_preferencias({"local": {"nome": "Casa", "lat": -20.4, "lon": -40.5},
                                  "locais": [{"nome": "Praia", "lat": -20.6, "lon": -40.4}]}, "t")
        vistos = []
        with mock.patch.object(clima, "previsao", side_effect=lambda la, lo: vistos.append((la, lo)) or clima._normalizar(JSON_OM)), \
             mock.patch.object(clima, "qualidade_ar", return_value=None):
            r = self.cliente.get("/api/clima?i=1", headers=self.cab).json()
            self.assertEqual((r["i"], r["local"]["nome"]), (1, "Praia"))
            self.assertEqual([x["nome"] for x in r["locais"]], ["Casa", "Praia"])
            self.assertTrue(r["locais"][0]["casa"])
            self.assertEqual(self.cliente.get("/api/clima?i=9", headers=self.cab).json()["i"], 1)
        self.assertEqual(vistos[0], (-20.6, -40.4))
        ruim = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"locais": [{"nome": "X", "lat": 0, "lon": 999}]}, "origem": "t"})
        self.assertEqual(ruim.status_code, 422)

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
