"""Casos compartilhados com o app do PC: casos-tabelas.txt e lido tambem por
tools/CasosCompartilhadosTests.cs. Uma tabela que mude so de um lado falha aqui ou la."""

import os
import unittest

from comum import Base  # primeiro: prepara o ambiente  # noqa: F401

from app import tabelas  # noqa: E402

ARQUIVO = os.path.join(os.path.dirname(__file__), "casos-tabelas.txt")

FUNCOES = {
    "programa": lambda a: tabelas.programa(int(a[0])),
    "modo": lambda a: tabelas.modo(int(a[0])),
    "ventilador": lambda a: tabelas.ventilador(int(a[0]), a[1]),
    "fase": lambda a: tabelas.FASES.get(int(a[0]), ""),
    "porta": lambda a: tabelas.PORTA.get(int(a[0]), "--"),
    "temperatura": lambda a: tabelas.temperatura_lavagem(int(a[0])),
    "centrifugacao": lambda a: tabelas.centrifugacao(int(a[0])),
    "secagem": lambda a: tabelas.secagem(int(a[0])),
    "sujeira": lambda a: tabelas.sujeira(int(a[0])),
}


def casos():
    with open(ARQUIVO, encoding="utf-8") as f:
        for linha in f:
            linha = linha.rstrip("\n")
            if not linha or linha.startswith("#"):
                continue
            nome, args, esperado = linha.split("|", 2)
            yield nome, args.split(","), esperado


class CasosCompartilhados(unittest.TestCase):
    def test_tabelas_iguais_as_do_app(self):
        n = 0
        for nome, args, esperado in casos():
            with self.subTest(caso="%s(%s)" % (nome, ",".join(args))):
                self.assertEqual(esperado, FUNCOES[nome](args))
            n += 1
        self.assertGreater(n, 100)


if __name__ == "__main__":
    unittest.main()
