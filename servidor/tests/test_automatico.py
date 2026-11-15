"""Modo automatico do ar: so age quando ligado, respeita mudanca manual, intervalo minimo e o liga/desliga."""

from unittest import mock

from comum import AR, Base  # noqa: F401  (primeiro: prepara o ambiente)

from app import automatico, banco, casa, servico  # noqa: E402

FRIO = {"acao": "frio", "titulo": "Frio a 25 °C", "texto": "t", "cena": {"modo": 2, "temp": 25, "vento": -1, "noturno": -1}}
DESLIGAR = {"acao": "desligar", "titulo": "Desligue e abra as janelas", "texto": "t", "cena": {"desligar": True}}


def ar(**estado):
    e = dict(AR["estado"])
    e.update({k: str(v) for k, v in estado.items()})
    return dict(AR, estado=e)


class Automatico(Base):
    def setUp(self):
        super().setUp()
        banco.con()
        automatico._estado.update({"ultimo": 0, "visto": None, "pausaAte": 0, "registro": []})
        self.enviados = []
        self.envio = mock.patch.object(servico, "enviar", side_effect=lambda puid, props, sem: self.enviados.append(props))
        self.envio.start()
        self.addCleanup(self.envio.stop)
        self.comando = mock.patch.object(casa, "comando_da_cena", side_effect=self._props)
        self.comando.start()
        self.addCleanup(self.comando.stop)

    @staticmethod
    def _props(cena, a):
        if cena.get("desligar"):
            return {"t_power": "0"}
        return {"t_power": 1, "t_work_mode": cena["modo"], "t_temp": cena["temp"]}

    def ligar(self, **extra):
        cfg = {"ligado": True}
        cfg.update(extra)
        casa.gravar_preferencias({"auto": cfg}, "t")

    def test_desligado_nao_faz_nada(self):
        self.assertIsNone(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), FRIO, None)))
        self.assertEqual(self.enviados, [])

    def test_aplica_e_respeita_intervalo(self):
        self.ligar()
        self.assertEqual(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), FRIO, None)), "Frio a 25 °C")
        self.assertEqual(self.enviados[-1]["t_temp"], 25)
        # 10 min depois o ar ja esta como ele deixou, mas a sugestao mudou: espera os 30 min
        outra = dict(FRIO, titulo="Frio a 24 °C", cena=dict(FRIO["cena"], temp=24))
        self.assertIsNone(automatico.passo(agora=10_600, hora=14, sugerir=lambda: (ar(t_temp=25), outra, None)))
        self.assertEqual(len(self.enviados), 1)
        self.assertEqual(automatico.passo(agora=12_000, hora=14, sugerir=lambda: (ar(t_temp=25), outra, None)), "Frio a 24 °C")

    def test_mudanca_manual_pausa(self):
        self.ligar(pausaMin=60)
        automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), FRIO, None))
        # alguem pos em 21 pelo controle: pausa uma hora, mesmo passado o intervalo minimo
        self.assertIsNone(automatico.passo(agora=12_000, hora=14, sugerir=lambda: (ar(t_temp=21), FRIO, None)))
        self.assertIn("pausado", automatico.situacao()["registro"][0]["texto"])
        self.assertEqual(len(self.enviados), 1)
        self.assertIsNotNone(automatico.passo(agora=12_000 + 3700, hora=14, sugerir=lambda: (ar(t_temp=21), FRIO, None)))

    def test_trava_do_seco_nao_e_mudanca_manual(self):
        # no seco o aparelho poe o alvo em 22 sozinho: isso nao pode pausar o automatico
        seco = {"acao": "seco", "titulo": "Desumidificar", "texto": "t", "cena": {"modo": 3, "temp": -1, "vento": -1, "noturno": -1}}
        self.comando.stop()
        self.ligar(pausaMin=60)
        self.assertEqual(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(t_temp=25), seco, None)), "Desumidificar")
        self.assertNotIn("t_temp", self.enviados[-1])
        self.assertIsNone(automatico.passo(agora=10_400, hora=14, sugerir=lambda: (ar(t_work_mode=3, t_temp=22), seco, None)))
        self.assertEqual(automatico.situacao()["pausaAte"], 0)
        # ja no frio, mexer no alvo continua sendo mudanca manual
        automatico.passo(agora=12_000, hora=14, sugerir=lambda: (ar(t_work_mode=3, t_temp=22), FRIO, None))
        automatico.passo(agora=12_400, hora=14, sugerir=lambda: (ar(t_work_mode=2, t_temp=21), FRIO, None))
        self.assertIn("pausado", automatico.situacao()["registro"][0]["texto"])

    def test_nao_liga_sozinho_por_padrao(self):
        self.ligar()
        self.assertIsNone(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(t_power=0), FRIO, None)))
        self.ligar(podeLigar=True)
        self.assertIsNotNone(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(t_power=0), FRIO, None)))

    def test_desligar_so_com_permissao(self):
        self.ligar(podeDesligar=False)
        self.assertIsNone(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), DESLIGAR, None)))
        self.ligar(podeDesligar=True)
        automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), DESLIGAR, None))
        self.assertEqual(self.enviados[-1], {"t_power": "0"})

    def test_janela_de_horas_e_igual_nao_manda(self):
        self.ligar(inicio=22, fim=7)
        self.assertIsNone(automatico.passo(agora=10_000, hora=14, sugerir=lambda: (ar(), FRIO, None)))
        self.assertIsNotNone(automatico.passo(agora=10_000, hora=23, sugerir=lambda: (ar(), FRIO, None)))
        automatico._estado.update({"ultimo": 0, "visto": None})
        self.assertIsNone(automatico.passo(agora=20_000, hora=23, sugerir=lambda: (ar(t_temp=25), FRIO, None)))

    def test_preferencia_validada(self):
        r = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"auto": {"ligado": "sim"}}, "origem": "t"})
        self.assertEqual(r.status_code, 422)
        r = self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"auto": {"ligado": True, "pausaMin": 5}}, "origem": "t"})
        self.assertEqual(r.status_code, 422)
        self.assertEqual(self.cliente.get("/api/auto", headers=self.cab).json()["config"]["ligado"], False)
