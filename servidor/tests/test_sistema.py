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
        self.assertEqual(set(r.json()), {"carga", "cpus", "memoria", "disco", "temperatura", "ligadoHoras", "banco", "backup"})


if __name__ == "__main__":
    unittest.main()
