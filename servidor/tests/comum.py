"""Ambiente comum dos testes: chave e pasta de dados ANTES de importar o app (main le a chave ao importar)."""

import atexit
import os
import shutil
import tempfile
import unittest

CHAVE = "chave-de-teste-0123456789"
_PASTA = tempfile.mkdtemp(prefix="mhiagos-casa-teste-")
os.environ["MHIAGOS_CHAVE"] = CHAVE
os.environ["MHIAGOS_DADOS"] = _PASTA
atexit.register(shutil.rmtree, _PASTA, True)

from fastapi.testclient import TestClient  # noqa: E402

from app import banco, main, servico  # noqa: E402

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
