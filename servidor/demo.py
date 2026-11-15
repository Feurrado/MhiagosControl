"""Modo demo: o painel da Casa com um ar simulado, clima e historico sinteticos, sem nuvem nem rede.

Serve para testar telas e tirar capturas sem mexer nos aparelhos reais (nem na conta ConnectLife).
Nada e gravado fora de uma pasta temporaria. Rodar de dentro de servidor/:

    python demo.py            # http://127.0.0.1:8770 , chave: demo-chave-0123456789
"""

import os
import random
import tempfile
import threading
import time

CHAVE = "demo-chave-0123456789"
PORTA = int(os.environ.get("MHIAGOS_DEMO_PORTA", "8770"))
os.environ["MHIAGOS_CHAVE"] = CHAVE
os.environ["MHIAGOS_DADOS"] = tempfile.mkdtemp(prefix="mhiagos-demo-")

import uvicorn  # noqa: E402

from app import banco, casa, clima, clima_exemplo, main, servico  # noqa: E402


class NuvemDemo:
    """O minimo que o servico usa da ConnectLife; o ar reage aos comandos e a temperatura anda sozinha."""

    def __init__(self):
        self.ar = {"puid": "demo-ar", "deviceId": "demo1", "apelido": "Ar da sala (demo)", "tipo": "008", "tipoNome": "Ar-condicionado",
                   "modelo": "demo", "codigoDoModelo": "demo", "comodo": "Sala", "online": True,
                   "estado": {"t_power": "1", "t_temp": "23", "f_temp_in": "27", "t_work_mode": "2", "t_fan_speed": "2"}}
        threading.Thread(target=self._andar, daemon=True).start()

    def _andar(self):
        while True:
            time.sleep(20)
            e = self.ar["estado"]
            dentro, alvo = float(e["f_temp_in"]), float(e["t_temp"])
            if e["t_power"] == "1":
                dentro += max(-0.5, min(0.5, alvo - dentro))
            else:
                dentro += 0.2 if dentro < 29 else 0
            e["f_temp_in"] = str(round(dentro, 1))

    def listar(self):
        return [dict(self.ar, estado=dict(self.ar["estado"]))]

    def propriedades(self, tipo, modelo):
        return {}

    def autodiagnostico(self, puid):
        return {"data": {"selfCheckList": []}}

    def definir(self, puid, props):
        self.ar["estado"].update({k: str(v) for k, v in props.items()})

    def fechar(self):
        pass


def semear_historico():
    """14 dias de uso noturno do ar (amostras de 1 min), para o Consumo e a previsao do mes."""
    rnd = random.Random(7)
    c = banco.con()
    agora = int(time.time())
    base = agora - agora % 86400
    for d in range(14, 0, -1):
        ini = base - d * 86400 + (21 + rnd.randint(0, 2)) * 3600 + 3 * 3600   # ~horario local de Brasilia
        for m in range(rnd.randint(120, 330)):
            t = ini + m * 60
            if t < agora:
                c.execute("INSERT OR REPLACE INTO amostras_ar (puid,t,amb,alvo,ligado,modo) VALUES (?,?,?,?,1,2)",
                          ("demo-ar", t, round(28 - min(m, 40) * 0.1, 1), 23))
    c.commit()


def clima_falso(lat, lon, agora=None):
    return dict(clima._normalizar(clima_exemplo.exemplo()), velho=False, atualizado=int(time.time()))


if __name__ == "__main__":
    clima.previsao = clima_falso
    clima.cidades = lambda q: [{"nome": "Cidade demo, SP, BR", "lat": -23.55, "lon": -46.63}]
    banco.con()
    semear_historico()
    casa.gravar_preferencias({"local": {"nome": "Cidade demo, SP, BR", "lat": -23.55, "lon": -46.63}}, "demo")
    servico._usar(NuvemDemo())
    servico.iniciar()
    print("Demo em http://127.0.0.1:%d/#chave=%s" % (PORTA, CHAVE))
    uvicorn.run(main.app, host="127.0.0.1", port=PORTA, log_level="warning")
