"""Testes do servidor da Casa, sem nuvem de verdade: a ConnectLife e simulada.

Rodar de dentro de servidor/:  python -m unittest discover -s tests -v
So usa a biblioteca padrao e o que o requirements.txt ja instala (httpx e o TestClient do FastAPI).
"""

import json
import os
import shutil
import tempfile
import threading
import time
import unittest

CHAVE = "chave-de-teste-0123456789"
_PASTA = tempfile.mkdtemp(prefix="mhiagos-casa-teste-")
os.environ["MHIAGOS_CHAVE"] = CHAVE
os.environ["MHIAGOS_DADOS"] = _PASTA

import httpx  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

from app import banco, connectlife, servico  # noqa: E402
from app import main  # noqa: E402
from app.connectlife import ConnectLife, ConnectLifeErro  # noqa: E402

AR = {"puid": "p-ar", "deviceId": "d1", "apelido": "Gil do Frio", "tipo": "008", "tipoNome": "", "modelo": "301",
      "codigoDoModelo": "301", "comodo": "", "online": True,
      "estado": {"t_power": "1", "t_temp": "23", "f_temp_in": "22", "t_work_mode": "2", "t_fan_speed": "2"}}


class NuvemFalsa:
    """O minimo que o servico usa da ConnectLife."""

    def __init__(self, lista=None, erro=None):
        self.lista = lista if lista is not None else [dict(AR, estado=dict(AR["estado"]))]
        self.erro = erro
        self.definidos = []

    def listar(self):
        if self.erro:
            raise self.erro
        return [dict(a, estado=dict(a["estado"])) for a in self.lista]

    def propriedades(self, tipo, modelo):
        return {}

    def autodiagnostico(self, puid):
        return {"data": {"selfCheckList": []}}

    def definir(self, puid, props):
        self.definidos.append((puid, dict(props)))

    def fechar(self):
        pass


class Base(unittest.TestCase):
    def setUp(self):
        self.pasta = tempfile.mkdtemp(prefix="caso-", dir=_PASTA)
        banco.ARQUIVO = os.path.join(self.pasta, "casa.db")
        servico._nuvem = None
        servico._aparelhos = []
        servico._erro = None
        servico._primeira_recusa = 0.0
        self.cliente = TestClient(main.app)   # sem o "with": o startup (threads de leitura) nao roda
        self.cab = {"Authorization": "Bearer " + CHAVE}

    def tearDown(self):
        banco.fechar()


class Autenticacao(Base):
    def test_rotas_pedem_a_chave(self):
        self.assertEqual(self.cliente.get("/api/aparelhos").status_code, 401)
        self.assertEqual(self.cliente.get("/api/aparelhos", headers={"Authorization": "Bearer errada"}).status_code, 401)
        self.assertEqual(self.cliente.get("/api/aparelhos", headers=self.cab).status_code, 200)

    def test_saude_sem_chave(self):
        r = self.cliente.get("/api/saude")
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["versao"], main.VERSAO)


class Agendamentos(Base):
    def _falha(self, *a):
        raise ConnectLifeErro("Sem resposta da ConnectLife: ConnectTimeout")

    def test_falha_fica_na_fila_e_tenta_de_novo(self):
        agora = 1_800_000_000
        ident = servico.agendar("p-ar", agora - 5, {"t_power": "0"}, "desligar")
        servico.processar_agendamentos(agora, self._falha)
        r = servico.resultado(ident)
        self.assertEqual(r["estado"], "pendente")
        self.assertEqual(r["tentativas"], 1)

        chamadas = []
        servico.processar_agendamentos(agora + 30, lambda *a: chamadas.append(a))
        self.assertEqual(chamadas, [], "antes da espera nao tenta de novo")

        servico.processar_agendamentos(agora + 61, lambda *a: chamadas.append(a))
        self.assertEqual(len(chamadas), 1)
        self.assertEqual(chamadas[0][1], {"t_power": "0"})
        self.assertEqual(servico.resultado(ident)["estado"], "feito")
        self.assertEqual(banco.linhas("SELECT * FROM agendamentos"), [])

    def test_desiste_depois_do_limite_e_avisa(self):
        agora = 1_800_000_000
        ident = servico.agendar("p-ar", agora, {"t_power": "0"}, "desligar")
        t = agora
        while t <= agora + servico.LIMITE_ATRASO + 200:
            servico.processar_agendamentos(t, self._falha)
            t += 30
        r = servico.resultado(ident)
        self.assertEqual(r["estado"], "falhou")
        self.assertGreater(r["tentativas"], 1)
        self.assertIn("ConnectTimeout", r["erro"])
        falhas = servico.falhas_recentes(0)
        self.assertEqual([f["id"] for f in falhas], [ident])
        tipos = [e["tipo"] for e in banco.linhas("SELECT tipo FROM eventos")]
        self.assertIn("agendamento_falhou", tipos)

    def test_vencido_sem_tentativa_e_descartado(self):
        agora = 1_800_000_000
        ident = servico.agendar("p-ar", agora - servico.LIMITE_ATRASO - 60, {"t_power": "0"}, "desligar")
        chamadas = []
        servico.processar_agendamentos(agora, lambda *a: chamadas.append(a))
        self.assertEqual(chamadas, [])
        self.assertEqual(servico.resultado(ident)["estado"], "descartado")

    def test_cancelar_deixa_resultado(self):
        ident = servico.agendar("p-ar", 2_000_000_000, {"t_power": "0"}, "desligar")
        r = self.cliente.delete("/api/agendamentos/%d" % ident, headers=self.cab)
        self.assertEqual(r.status_code, 200)
        r = self.cliente.get("/api/agendamentos/%d/resultado" % ident, headers=self.cab)
        self.assertEqual(r.json()["estado"], "cancelado")
        self.assertEqual(self.cliente.get("/api/agendamentos/999999/resultado", headers=self.cab).json()["estado"],
                         "desconhecido")

    def test_novo_timer_substitui_e_cancela_o_retorno(self):
        a = servico.agendar("p-ar", 2_000_000_000, {"t_power": "0"}, "desligar")
        ret = servico.agendar("p-ar", 2_000_000_100, {"t_temp": "24"}, "retorno")
        b = servico.agendar("p-ar", 2_000_000_200, {"t_power": "0"}, "desligar")
        self.assertEqual(servico.resultado(a)["estado"], "cancelado")
        self.assertEqual(servico.resultado(ret)["estado"], "cancelado")
        self.assertEqual(servico.resultado(b)["estado"], "pendente")

    def test_banco_antigo_ganha_as_colunas_novas(self):
        banco.fechar()
        arquivo = os.path.join(self.pasta, "antigo.db")
        import sqlite3
        c = sqlite3.connect(arquivo)
        c.execute("CREATE TABLE agendamentos (id INTEGER PRIMARY KEY AUTOINCREMENT, puid TEXT NOT NULL, "
                  "quando INTEGER NOT NULL, props TEXT NOT NULL, nome TEXT, criado INTEGER NOT NULL)")
        c.execute("INSERT INTO agendamentos(puid,quando,props,nome,criado) VALUES('p-ar', 1, '{}', 'desligar', 1)")
        c.commit()
        c.close()
        banco.ARQUIVO = arquivo
        self.assertEqual(servico.resultado(1)["estado"], "pendente")
        self.assertEqual(servico.resultado(1)["tentativas"], 0)


class Login(Base):
    def test_esquecer_conta_so_na_segunda_recusa(self):
        self.assertEqual(servico.esquecer_conta(0.0, 100.0), (False, 100.0))
        self.assertEqual(servico.esquecer_conta(100.0, 130.0), (False, 100.0))
        self.assertEqual(servico.esquecer_conta(100.0, 161.0), (True, 0.0))

    def test_leitura_recusada_nao_apaga_a_conta_na_primeira(self):
        banco.gravar_kv("conta", {"refresh_token": "rt", "source_id": "s"})
        servico._usar(NuvemFalsa(erro=ConnectLifeErro("expirou", True)))
        servico._ler()
        self.assertTrue(servico.logado())
        self.assertIsNotNone(banco.ler_kv("conta"))
        servico._primeira_recusa = time.time() - servico.SEGUNDOS_ENTRE_RECUSAS - 1
        servico._ler()
        self.assertFalse(servico.logado())
        self.assertIsNone(banco.ler_kv("conta"))

    def test_leitura_boa_zera_a_recusa(self):
        nuvem = NuvemFalsa(erro=ConnectLifeErro("expirou", True))
        servico._usar(nuvem)
        servico._ler()
        nuvem.erro = None
        servico._ler()
        self.assertEqual(servico._primeira_recusa, 0.0)
        self.assertEqual(len(servico.aparelhos()), 1)


class ClienteDaNuvem(unittest.TestCase):
    def _cliente(self, rota):
        return httpx.Client(transport=httpx.MockTransport(rota))

    def test_uma_renovacao_por_vez(self):
        renovacoes = []

        def rota(req):
            if req.url.path == "/oauth/token":
                renovacoes.append(1)
                time.sleep(0.2)
                return httpx.Response(200, json={"access_token": "a%d" % len(renovacoes), "refresh_token": "r%d" % len(renovacoes),
                                                 "expires_in": 3600})
            return httpx.Response(200, json={"resultCode": 0, "deviceList": []})

        n = ConnectLife("r0", "s", cliente=self._cliente(rota))
        threads = [threading.Thread(target=n.listar) for _ in range(4)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        self.assertEqual(len(renovacoes), 1, "quatro chamadas juntas renovam uma vez so")
        self.assertEqual(n.refresh_token, "r1")

    def _status_do_token(self, status):
        n = ConnectLife("r0", "s", cliente=self._cliente(lambda req: httpx.Response(status, text="x")))
        with self.assertRaises(ConnectLifeErro) as c:
            n.listar()
        return c.exception.pede_login

    def test_so_400_e_401_do_token_pedem_login(self):
        self.assertTrue(self._status_do_token(400))
        self.assertTrue(self._status_do_token(401))
        self.assertFalse(self._status_do_token(403))
        self.assertFalse(self._status_do_token(429))
        self.assertFalse(self._status_do_token(503))

    def test_na_api_so_401_pede_login(self):
        def rota(status):
            def r(req):
                if req.url.path == "/oauth/token":
                    return httpx.Response(200, json={"access_token": "a", "expires_in": 3600})
                return httpx.Response(status, text="x")
            return r
        for status, pede in ((401, True), (403, False), (429, False)):
            n = ConnectLife("r0", "s", cliente=self._cliente(rota(status)))
            with self.assertRaises(ConnectLifeErro) as c:
                n.listar()
            self.assertEqual(c.exception.pede_login, pede, "status %d" % status)


class Historico(Base):
    def test_importar_numa_transacao_e_sem_repetir(self):
        carga = {"amostras": [{"puid": "p-ar", "t": 1_800_000_000 + i * 60, "amb": 22.5, "alvo": 23, "ligado": 1, "modo": 2}
                              for i in range(500)],
                 "ciclos": [{"puid": "p-lav", "inicio": 1_800_000_000, "fim": 1_800_003_600, "programa": 7,
                             "agua": 40.0, "energia": 0.5, "concluido": 1}]}
        r = self.cliente.post("/api/historico/importar", headers=self.cab, json=carga)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json(), {"amostras": 500, "ciclos": 1})
        r = self.cliente.post("/api/historico/importar", headers=self.cab, json=carga)
        self.assertEqual(r.json(), {"amostras": 0, "ciclos": 0})

    def test_ciclo_da_lavadora_abre_e_fecha(self):
        lav = {"puid": "p-lav", "online": True, "estado": {"machine_status": "2", "Current_program_phase": "3",
                                                          "Selected_program_ID": "7"}}
        servico._conferir_ciclo(lav, 1_800_000_000)
        self.assertEqual(len(banco.linhas("SELECT * FROM em_curso")), 1)
        lav["estado"].update(machine_status="1", Current_program_phase="10")
        servico._conferir_ciclo(lav, 1_800_003_600)
        self.assertEqual(banco.linhas("SELECT * FROM em_curso"), [])
        ciclos = banco.linhas("SELECT * FROM ciclos")
        self.assertEqual(len(ciclos), 1)
        self.assertEqual(ciclos[0]["fim"] - ciclos[0]["inicio"], 3600)


class Painel(Base):
    def test_mudancas_responde_na_hora_quando_a_versao_e_outra(self):
        inicio = time.time()
        r = self.cliente.get("/api/mudancas?desde=-5", headers=self.cab)
        self.assertEqual(r.status_code, 200)
        self.assertLess(time.time() - inicio, 2)
        self.assertEqual(r.json()["versao"], servico.versao())

    def test_estado_traz_os_agendamentos_que_falharam(self):
        agora = int(time.time())
        ident = servico.agendar("p-ar", agora - servico.LIMITE_ATRASO - 1, {"t_power": "0"}, "desligar")
        banco.con().execute("UPDATE agendamentos SET tentativas=3, erro='sem resposta' WHERE id=?", (ident,))
        servico.processar_agendamentos(agora, lambda *a: None)
        r = self.cliente.get("/api/aparelhos", headers=self.cab).json()
        self.assertEqual([f["id"] for f in r["agendamentosFalhos"]], [ident])


def tearDownModule():
    banco.fechar()
    shutil.rmtree(_PASTA, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
