"""Contrato de /api/aparelhos com o app do PC (docs/CONTRATO-CASA.md): contrato-aparelhos.json e a
amostra que tools/ContratoCasaTests.cs le. Um campo que suma ou mude de tipo aqui falha antes de o
app deixar de entender o servidor."""

import json
import os

from comum import Base, NuvemFalsa  # primeiro: prepara o ambiente

from app import main, servico  # noqa: E402

AMOSTRA = os.path.join(os.path.dirname(__file__), "contrato-aparelhos.json")


class Contrato(Base):
    def setUp(self):
        super().setUp()
        with open(AMOSTRA, encoding="utf-8") as f:
            self.amostra = json.load(f)

    def test_amostra_e_do_contrato_atual(self):
        self.assertEqual(self.amostra["contrato"], main.CONTRATO)

    def test_saude_informa_o_contrato(self):
        self.assertEqual(self.cliente.get("/api/saude").json()["contrato"], main.CONTRATO)

    def test_aparelhos_tem_os_campos_da_amostra(self):
        servico._nuvem = NuvemFalsa()
        servico._ler()
        r = self.cliente.get("/api/aparelhos", headers=self.cab).json()
        for chave, valor in self.amostra.items():
            self.assertIn(chave, r)
            if valor is not None:
                self.assertIsInstance(r[chave], type(valor), chave)
        self.assertTrue(r["logado"])
        self.assertEqual(r["contrato"], main.CONTRATO)
        a, modelo = r["lista"][0], self.amostra["lista"][0]
        for chave, valor in modelo.items():
            self.assertIn(chave, a)
            self.assertIsInstance(a[chave], type(valor), chave)
        self.assertNotIn("meta", a)
        for k, v in a["estado"].items():
            self.assertIsInstance(k, str)
            self.assertIsInstance(v, str)

    def test_comando_do_pc_nao_cancela_o_retorno(self):
        """O PC manda manual=false: quem cancela o retorno da cena e ele, pelo id do agendamento."""
        nuvem = NuvemFalsa()
        servico._nuvem = nuvem
        servico._ler()
        r = self.cliente.post("/api/aparelhos/p-ar/comando", headers=self.cab,
                              json={"props": {"t_temp": "24"}, "semBipe": False, "manual": False})
        self.assertEqual(r.status_code, 200, r.text)
        self.assertEqual(nuvem.definidos[-1], ("p-ar", {"t_temp": "24"}))
