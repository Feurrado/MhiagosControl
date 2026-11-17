"""Historico de eventos: origem, descricao, filtros, paginacao, retencao e migracao do banco antigo."""

import sqlite3
import time
import unittest

from comum import AR, Base, NuvemFalsa  # noqa: F401  (primeiro: prepara o ambiente)

from app import banco, casa, servico  # noqa: E402


class Eventos(Base):
    def setUp(self):
        super().setUp()
        servico._aparelhos = [dict(AR, estado=dict(AR["estado"]))]
        banco.con()

    def lista(self, **q):
        return self.cliente.get("/api/historico/eventos", headers=self.cab, params=q).json()

    def test_comando_grava_origem_e_descricao(self):
        servico._nuvem = NuvemFalsa()
        servico.enviar("p-ar", {"t_power": "1", "t_work_mode": "2", "t_temp": "25"}, False, origem="recomendacao")
        e = self.lista()["eventos"][0]
        self.assertEqual((e["origem"], e["tipo"], e["dispositivo"]), ("recomendacao", "comando", "Gil do Frio"))
        self.assertEqual(e["descricao"], "Ligou · modo Frio, 25 °C")

    def test_comando_do_painel_e_manual_por_padrao(self):
        servico._nuvem = NuvemFalsa()
        r = self.cliente.post("/api/aparelhos/p-ar/comando", headers=self.cab, json={"props": {"t_temp": "24"}})
        self.assertEqual(r.status_code, 200)
        self.assertEqual(self.lista()["eventos"][0]["origem"], "manual")
        r = self.cliente.post("/api/aparelhos/p-ar/comando", headers=self.cab, json={"props": {"t_temp": "24"}, "origem": "x"})
        self.assertEqual(r.status_code, 422)

    def test_ordem_filtros_e_paginacao(self):
        agora = int(time.time())
        for i in range(7):
            servico._evento("p-ar" if i % 2 == 0 else "p-lav", "comando", "{}", origem="manual" if i < 5 else "automacao", descricao="e%d" % i)
        todos = self.lista()["eventos"]
        self.assertEqual([e["descricao"] for e in todos], ["e%d" % i for i in range(6, -1, -1)])   # mais novo primeiro
        self.assertEqual(len(self.lista(puid="p-ar")["eventos"]), 4)
        self.assertEqual(len(self.lista(origem="automacao")["eventos"]), 2)
        self.assertEqual(len(self.lista(de=agora + 60)["eventos"]), 0)
        self.assertEqual(len(self.lista(ate=agora - 60)["eventos"]), 0)
        p1 = self.lista(limite=3)
        self.assertEqual(len(p1["eventos"]), 3)
        self.assertIsNotNone(p1["proximo"])
        p2 = self.lista(limite=3, antes=p1["proximo"])
        self.assertEqual([e["descricao"] for e in p2["eventos"]], ["e3", "e2", "e1"])
        p3 = self.lista(limite=3, antes=p2["proximo"])
        self.assertEqual([e["descricao"] for e in p3["eventos"]], ["e0"])
        self.assertIsNone(p3["proximo"])

    def test_mudanca_fora_do_app_vira_evento_manual_so_sem_comando_recente(self):
        a = dict(AR, estado=dict(AR["estado"]))
        servico._conferir_ar(a, time.time())                                     # primeira leitura: so memoriza
        self.assertEqual(self.lista()["eventos"], [])
        b = dict(AR, estado=dict(AR["estado"], t_temp="26"))
        servico._conferir_ar(b, time.time())
        e = self.lista()["eventos"][0]
        self.assertEqual((e["tipo"], e["origem"], e["descricao"]), ("ar_externo", "manual", "Ligado · Frio, 26 °C"))
        servico._evento("p-ar", "comando", "{}", origem="manual", descricao="x")  # comando recente: a mudanca e dele
        n = len(self.lista()["eventos"])
        servico._conferir_ar(dict(AR, estado=dict(AR["estado"], t_temp="27")), time.time())
        self.assertEqual(len(self.lista()["eventos"]), n)

    def test_retencao_configuravel(self):
        agora = int(time.time())
        c = banco.con()
        c.execute("INSERT INTO eventos(t,puid,tipo,texto,origem) VALUES(?,?,?,?,?)", (agora - 40 * 86400, "p-ar", "comando", "{}", "manual"))
        c.execute("INSERT INTO eventos(t,puid,tipo,texto,origem) VALUES(?,?,?,?,?)", (agora - 5 * 86400, "p-ar", "comando", "{}", "manual"))
        casa.gravar_preferencias({"retencaoEventosDias": 30}, "t")
        servico._ultima_limpeza = 0
        servico._limpar(agora)
        self.assertEqual(len(self.lista()["eventos"]), 1)
        self.assertEqual(self.lista()["retencaoDias"], 30)
        r = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"retencaoEventosDias": 1}, "origem": "t"})
        self.assertEqual(r.status_code, 422)

    def test_evento_antigo_sem_descricao_ganha_uma(self):
        banco.con().execute("INSERT INTO eventos(t,puid,tipo,texto) VALUES(?,?,?,?)",
                            (int(time.time()), "p-ar", "comando", '{"t_power":"0"}'))
        e = self.lista()["eventos"][0]
        self.assertEqual((e["descricao"], e["origem"]), ("Desligou", "sistema"))

    def test_pede_a_chave(self):
        self.assertEqual(self.cliente.get("/api/historico/eventos").status_code, 401)


class Migracao(Base):
    def test_banco_sem_as_colunas_novas_e_migrado(self):
        import os
        arq = os.path.join(self.pasta, "velho.db")
        c = sqlite3.connect(arq)
        c.execute("CREATE TABLE eventos (id INTEGER PRIMARY KEY AUTOINCREMENT, t INTEGER NOT NULL, puid TEXT, tipo TEXT, texto TEXT)")
        c.execute("INSERT INTO eventos(t,puid,tipo,texto) VALUES(1700000000,'p','ciclo_fim','Programa 1, 40 min')")
        c.commit()
        c.close()
        banco.fechar()
        banco.ARQUIVO = arq
        r = banco.con().execute("SELECT origem, descricao, texto FROM eventos").fetchone()
        self.assertEqual((r["origem"], r["descricao"], r["texto"]), ("sistema", None, "Programa 1, 40 min"))


if __name__ == "__main__":
    unittest.main()
