"""Notificacao no celular pelo ntfy: so nas transicoes, respeitando as chaves de alerta."""

import unittest
from unittest import mock

from comum import AR, Base  # primeiro: prepara o ambiente

from app import avisos, casa  # noqa: E402


def ar(online=True, falhas=(), amb=22):
    a = dict(AR, online=online, estado=dict(AR["estado"]))
    a["info"] = {"ambiente": amb, "unidade": "°C", "falhas": [{"nome": f, "chave": f} for f in falhas]}
    a["diagnostico"] = {"conhecidas": 0, "ativas": []}
    return a


class Avisos(Base):
    def setUp(self):
        super().setUp()
        avisos.esquecer()
        casa.gravar_preferencias({"ntfyTopico": "topico-de-teste-123"}, "teste")
        self.enviados = []
        p = mock.patch.object(avisos, "_enviar", lambda t, x, *a, **k: self.enviados.append(t))
        p.start()
        self.addCleanup(p.stop)

    def test_primeira_leitura_so_registra(self):
        avisos.conferir([ar(online=False, falhas=["E1"], amb=35)])
        self.assertEqual(self.enviados, [])

    def test_transicoes(self):
        avisos.conferir([ar()])
        avisos.conferir([ar(falhas=["Sensor"], amb=35)])
        avisos.conferir([ar(online=False, falhas=["Sensor"], amb=35)])
        self.assertEqual(len(self.enviados), 3)
        self.assertTrue(any("falha" in t for t in self.enviados))
        self.assertTrue(any("quente" in t for t in self.enviados))
        self.assertTrue(any("desconectado" in t for t in self.enviados))
        avisos.conferir([ar(online=False, falhas=["Sensor"], amb=35)])
        self.assertEqual(len(self.enviados), 3)

    def test_alerta_desligado_nao_avisa(self):
        casa.gravar_preferencias({"alertaFalha": False}, "teste")
        avisos.conferir([ar()])
        avisos.conferir([ar(falhas=["Sensor"])])
        self.assertEqual(self.enviados, [])

    def test_evento_da_lavadora(self):
        avisos.evento(dict(AR, apelido="Lavadora"), "ciclo_fim", "Programa 3, 90 min")
        casa.gravar_preferencias({"alertaLavadoraFim": False}, "teste")
        avisos.evento(dict(AR, apelido="Lavadora"), "ciclo_fim", "Programa 3, 90 min")
        self.assertEqual(self.enviados, ["Lavadora: ciclo terminou"])


class Topico(Base):
    def _put(self, v):
        return self.cliente.put("/api/preferencias", headers=self.cab, json={"dados": {"ntfyTopico": v}})

    def test_validacao(self):
        self.assertEqual(self._put("abc").status_code, 422)
        self.assertEqual(self._put("tem espaco 123").status_code, 422)
        self.assertEqual(self._put(5).status_code, 422)
        self.assertEqual(self._put("casa-Mh_12345").status_code, 200)
        self.assertEqual(self._put("").status_code, 200)

    def test_teste_sem_topico(self):
        self.assertEqual(self.cliente.post("/api/avisos/teste", headers=self.cab).status_code, 409)

    def test_teste_manda(self):
        self._put("casa-Mh_12345")
        with mock.patch.object(avisos.httpx, "post") as post:
            post.return_value.status_code = 200
            r = self.cliente.post("/api/avisos/teste", headers=self.cab)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(post.call_args.kwargs["json"]["topic"], "casa-Mh_12345")


if __name__ == "__main__":
    unittest.main()
