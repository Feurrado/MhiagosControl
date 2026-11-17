"""Saude do servidor: leitura de /proc e do backup, sem depender da maquina."""

import os
import tempfile
import time
import unittest

from comum import Base  # noqa: F401  (primeiro: prepara o ambiente)

from app import sistema  # noqa: E402


class Backup(unittest.TestCase):
    def test_sem_pasta_nao_diz_nada(self):
        self.assertIsNone(sistema.backup(pasta="/nao/existe"))

    def test_pasta_vazia_e_velho(self):
        with tempfile.TemporaryDirectory() as p:
            r = sistema.backup(pasta=p)
            self.assertTrue(r["velho"])
            self.assertEqual(r["quantidade"], 0)

    def test_recente_e_antigo(self):
        with tempfile.TemporaryDirectory() as p:
            novo, velho = os.path.join(p, "casa-2.db"), os.path.join(p, "casa-1.db")
            for a in (novo, velho):
                open(a, "w").close()
            agora = time.time()
            os.utime(velho, (agora - 90 * 3600,) * 2)
            os.utime(novo, (agora - 5 * 3600,) * 2)
            r = sistema.backup(agora, p)
            self.assertFalse(r["velho"])
            self.assertEqual(r["quantidade"], 2)
            self.assertAlmostEqual(r["idadeHoras"], 5, 1)
            os.utime(novo, (agora - 40 * 3600,) * 2)
            self.assertTrue(sistema.backup(agora, p)["velho"])


class Rota(Base):
    def test_estado_e_chave(self):
        self.assertEqual(self.cliente.get("/api/sistema").status_code, 401)
        r = self.cliente.get("/api/sistema", headers=self.cab)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(set(r.json()), {"carga", "cpus", "memoria", "disco", "temperatura", "ligadoHoras", "banco", "backup", "nuvem"})


class NuvemTest(unittest.TestCase):
    def _com(self, conteudo):
        import json, tempfile, os
        p = tempfile.mkdtemp()
        if conteudo is not None:
            with open(os.path.join(p, "nuvem.json"), "w") as f:
                json.dump(conteudo, f)
        return p

    def test_sem_arquivo(self):
        self.assertIsNone(sistema.nuvem(pasta=self._com(None)))

    def test_ok_recente_e_falha(self):
        p = self._com({"ok": True, "ultimoOk": 1000, "arquivos": 5, "erro": ""})
        r = sistema.nuvem(1000 + 3600, p)
        self.assertEqual((r["ok"], r["idadeHoras"], r["velho"]), (True, 1.0, False))
        p = self._com({"ok": False, "ultimoOk": 1000, "arquivos": 0, "erro": "x"})
        self.assertTrue(sistema.nuvem(1000 + 3600, p)["velho"])
        p = self._com({"ok": True, "ultimoOk": 1000, "arquivos": 5, "erro": ""})
        self.assertTrue(sistema.nuvem(1000 + 40 * 3600, p)["velho"])


if __name__ == "__main__":
    unittest.main()


class Temperatura(unittest.TestCase):
    """O 108 C da Saude do servidor vinha de um sensor sem calibragem (maior leitura de todos os sensores)."""

    def _montar(self, base, sensores):
        arqs = []
        for i, (chip, milic) in enumerate(sensores):
            d = os.path.join(base, "hwmon%d" % i)
            os.makedirs(d)
            with open(os.path.join(d, "name"), "w") as f:
                f.write(chip + "\n")
            arq = os.path.join(d, "temp1_input")
            with open(arq, "w") as f:
                f.write(str(milic))
            arqs.append(arq)
        return arqs

    def test_prefere_o_chip_da_cpu_e_ignora_leitura_absurda(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as p:
            arqs = self._montar(p, [("acpitz", 108000), ("coretemp", 54000), ("nvme", 61000)])
            with mock.patch.object(sistema.glob, "glob", side_effect=lambda g: [] if "thermal_zone" in g else arqs):
                self.assertEqual(sistema.temperatura(), 54.0)

    def test_sem_chip_conhecido_usa_a_maior_plausivel(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as p:
            arqs = self._montar(p, [("acpitz", 108000), ("nvme", 61000)])
            with mock.patch.object(sistema.glob, "glob", side_effect=lambda g: [] if "thermal_zone" in g else arqs):
                self.assertEqual(sistema.temperatura(), 61.0)
