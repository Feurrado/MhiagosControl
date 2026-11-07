"""Cliente da nuvem ConnectLife (Hisense), porte do src/ConnectLife.cs do Mhiagos.

Os aparelhos nao abrem porta na rede local: tudo passa pela nuvem. O acesso e o
OAuth2 do cliente que a Hisense publicou para o Home Assistant; as chamadas vao
para a API de terceiros (juapi-3rd), assinadas com HMAC-SHA256 nos cabecalhos.

O refresh token GIRA a cada renovacao: quem guarda tem de gravar o novo sempre
(evento ao_mudar_token), senao a sessao cai na proxima renovacao.
"""

import base64
import hashlib
import hmac
import json
import logging
import threading
import time
import uuid
from email.utils import formatdate
from urllib.parse import quote, urlencode, urlsplit, parse_qs

import httpx

CLIENT_ID = "9793620883275788"
CLIENT_SECRET = "7h1m3gZVlILyBvIFBNmzXwoFYLhkGqG9NQd2jBzuZCqJKCTyCtYwQtXi4tVBjg9B"
URL_LOGIN = "https://oauth.hijuconn.com/login"
URL_TOKEN = "https://oauth.hijuconn.com/oauth/token"
RETORNO = "https://my.home-assistant.io/redirect/oauth"
BASE = "https://juapi-3rd.hijuconn.com"

TOKEN_INVALIDO = 100026
RANDSTR_REPETIDO = 101005
DIGEST_VAZIO = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU="

log = logging.getLogger("connectlife")


class ConnectLifeErro(Exception):
    """Falha da nuvem. pede_login=True: o acesso nao vale mais."""

    def __init__(self, mensagem, pede_login=False):
        super().__init__(mensagem)
        self.pede_login = pede_login


def pede_login_no_token(status):
    """So 400 e 401 do endpoint de token dizem que o acesso acabou (invalid_grant).
    403, 408, 429 e o resto sao do caminho, nao da conta: tratar como "entre de novo"
    apagava o login por um soluco da nuvem."""
    return status in (400, 401)


def pede_login_na_api(status):
    """Na API, so o 401: um 403 tambem sai de proxy e de limite de taxa."""
    return status == 401


def novo_source_id():
    semente = uuid.uuid4().hex + str(time.time_ns())
    return "td001002000" + hashlib.md5(semente.encode()).hexdigest()


def endereco_de_login(estado):
    return (URL_LOGIN + "?response_type=code&client_id=" + CLIENT_ID +
            "&redirect_uri=" + quote(RETORNO, safe="") + "&state=" + quote(estado, safe=""))


def extrair_codigo(colado):
    """O code no endereco colado (final, pagina intermediaria do HA) ou sozinho."""
    texto = (colado or "").strip()
    if not texto:
        return None
    if "://" not in texto and "code=" not in texto:
        return texto if " " not in texto else None
    p = _consulta(texto)
    for _ in range(3):
        if "code" in p:
            break
        aninhado = p.get("redirect", "")
        if "?" not in aninhado:
            break
        p = _consulta(aninhado)
    return p.get("code") or None


def _consulta(texto):
    q = texto.split("?", 1)[1] if "?" in texto else texto
    q = q.split("#", 1)[0]
    return {k: v[0] for k, v in parse_qs(q, keep_blank_values=True).items()}


def _caminho(url):
    """Caminho com a consulta, sem esquema e host: e o que entra na assinatura."""
    s = urlsplit(url)
    return s.path + ("?" + s.query if s.query else "")


def _texto(v):
    if v is None:
        return None
    if isinstance(v, str):
        return v
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float) and v.is_integer():
        return str(int(v))
    if isinstance(v, (int, float)):
        return str(v)
    return json.dumps(v, separators=(",", ":"), ensure_ascii=False)


class ConnectLife:
    def __init__(self, refresh_token, source_id=None, ao_mudar_token=None, cliente=None):
        self.refresh_token = refresh_token
        self.source_id = source_id or novo_source_id()
        self.ao_mudar_token = ao_mudar_token
        self._access = None
        self._expira = 0.0
        self._trava = threading.Lock()
        # Uma renovacao por vez: o refresh token gira, e leitura e comando renovando
        # juntos mandariam o mesmo token duas vezes.
        self._renovando = threading.Lock()
        self._http = cliente or httpx.Client(timeout=20.0, headers={"User-Agent": "MhiagosCasa", "Accept": "application/json"})

    def fechar(self):
        try:
            self._http.close()
        except Exception:
            pass

    # ---------------- token ----------------

    def trocar_codigo(self, code):
        self._token({"grant_type": "authorization_code", "code": code, "redirect_uri": RETORNO,
                     "client_id": CLIENT_ID, "client_secret": CLIENT_SECRET})

    def _renovar(self):
        if not self.refresh_token:
            raise ConnectLifeErro("Sem acesso a ConnectLife: entre de novo.", True)
        self._token({"grant_type": "refresh_token", "refresh_token": self.refresh_token,
                     "client_id": CLIENT_ID, "client_secret": CLIENT_SECRET})

    def _token(self, campos):
        try:
            r = self._http.post(URL_TOKEN, content=urlencode(campos),
                                headers={"Content-Type": "application/x-www-form-urlencoded"})
        except httpx.HTTPError as e:
            raise ConnectLifeErro("Sem resposta da ConnectLife: " + type(e).__name__)
        if r.status_code != 200:
            log.warning("token recusado %s %s", r.status_code, r.text[:300])
            if pede_login_no_token(r.status_code):
                raise ConnectLifeErro("A ConnectLife recusou o acesso: entre de novo.", True)
            raise ConnectLifeErro("Falha temporaria da ConnectLife.")
        o = r.json()
        access = _texto(o.get("access_token"))
        if not access:
            raise ConnectLifeErro("A ConnectLife recusou o acesso: entre de novo.", True)
        try:
            seg = int(o.get("expires_in") or 3600)
        except (TypeError, ValueError):
            seg = 3600
        if seg <= 0:
            seg = 3600
        with self._trava:
            self._access = access
            self._expira = time.time() + max(60, seg - 90)
            novo = _texto(o.get("refresh_token"))
            if novo:
                self.refresh_token = novo
        if self.ao_mudar_token:
            self.ao_mudar_token(self.refresh_token, self.source_id)

    def _valido(self):
        with self._trava:
            return self._access if self._access and time.time() < self._expira else None

    def _acesso(self):
        a = self._valido()
        if a:
            return a
        with self._renovando:
            a = self._valido()          # quem esperou na fila acha o acesso ja renovado
            if a:
                return a
            self._renovar()
        with self._trava:
            return self._access

    # ---------------- aparelhos ----------------

    def listar(self):
        r = self._chamar(BASE + "/clife-svc/pu/get_device_status_list", "GET")
        lista = r.get("deviceList")
        if not isinstance(lista, list):
            raise ConnectLifeErro("Resposta estranha da ConnectLife.")
        return [_aparelho(d) for d in lista if isinstance(d, dict) and "deviceId" in d]

    def propriedades(self, tipo, modelo):
        return self._chamar(BASE + "/clife-svc/get_property_list", "GET",
                            {"deviceTypeCode": tipo, "deviceFeatureCode": modelo})

    def autodiagnostico(self, puid):
        return self._chamar(BASE + "/basic/self_check/info", "POST", {"noRecord": "1", "puid": puid})

    def definir(self, puid, props):
        self._chamar(BASE + "/device/pu/property/set", "POST",
                     {"puid": puid, "properties": {k: str(v) for k, v in props.items()}})

    def _sistema(self):
        ts = str(int(time.time() * 1000))
        return {
            "timeStamp": ts, "version": "8.1", "languageId": "1", "timezone": "UTC",
            "randStr": hashlib.md5((uuid.uuid4().hex + ts).encode()).hexdigest(),
            "appId": CLIENT_ID, "sourceId": self.source_id, "platformId": 5,
        }

    def _chamar(self, url, metodo, carga=None, repetir_token=True, repetir_rand=True):
        access = self._acesso()
        dados = dict(carga or {})
        dados.update(self._sistema())
        corpo = None
        alvo = url
        if metodo == "GET":
            # Mesma montagem do app: sem escapar, como a assinatura espera.
            alvo = url + "?" + "&".join(k + "=" + _texto(v) for k, v in dados.items())
        else:
            dados["accessToken"] = access
            corpo = json.dumps(dados, separators=(",", ":"), ensure_ascii=False)

        cab = self._assinatura(metodo, alvo, corpo)
        cab["Content-Type"] = "application/json"
        if metodo == "GET":
            cab["accessToken"] = access
        try:
            if metodo == "GET":
                r = self._http.get(httpx.URL(alvo), headers=cab)
            else:
                r = self._http.post(alvo, content=corpo.encode("utf-8"), headers=cab)
        except httpx.HTTPError as e:
            raise ConnectLifeErro("Sem resposta da ConnectLife: " + type(e).__name__)

        if r.status_code != 200:
            log.warning("%s respondeu %s %s", _caminho(url), r.status_code, r.text[:300])
            if pede_login_na_api(r.status_code):
                raise ConnectLifeErro("A sessao da ConnectLife expirou: entre de novo.", True)
            raise ConnectLifeErro("Falha temporaria da ConnectLife.")
        try:
            o = r.json()
        except ValueError:
            raise ConnectLifeErro("Resposta estranha da ConnectLife.")
        resultado = _texto(o.get("resultCode"))
        if resultado is None or resultado == "0":
            return o

        codigo_texto = _texto(o.get("errorCode")) or resultado
        try:
            codigo = int(codigo_texto)
        except ValueError:
            codigo = 0
        desc = _texto(o.get("errorDesc")) or _texto(o.get("msg")) or ""
        log.warning("%s erro %s %s", _caminho(url), codigo_texto, desc)
        if codigo == TOKEN_INVALIDO and repetir_token:
            with self._trava:
                self._access = None
            return self._chamar(url, metodo, carga, False, repetir_rand)
        if codigo == RANDSTR_REPETIDO and repetir_rand:
            return self._chamar(url, metodo, carga, repetir_token, False)
        if codigo == TOKEN_INVALIDO:
            raise ConnectLifeErro("A sessao da ConnectLife expirou: entre de novo.", True)
        raise ConnectLifeErro("A ConnectLife respondeu erro %s %s" % (codigo_texto, desc))

    @staticmethod
    def _assinatura(metodo, url, corpo):
        data = formatdate(usegmt=True)
        assinar = CLIENT_ID + "\n" + metodo + " " + _caminho(url) + "\ndate: " + data + \
            "\nhi-params-encrypt: " + CLIENT_ID + "\n"
        sig = base64.b64encode(hmac.new(CLIENT_SECRET.encode(), assinar.encode(), hashlib.sha256).digest()).decode()
        digest = DIGEST_VAZIO if not corpo else base64.b64encode(hashlib.sha256(corpo.encode("utf-8")).digest()).decode()
        return {
            "Date": data,
            "hi-params-encrypt": CLIENT_ID,
            "Authorization": 'Signature signature="' + sig + '", keyId="' + CLIENT_ID +
                             '",algorithm="hmac-sha256", headers="@request-target date hi-params-encrypt"',
            "Digest": "SHA-256=" + digest,
        }


def _aparelho(d):
    estado = {}
    st = d.get("statusList")
    if isinstance(st, dict):
        estado = {k: _texto(v) for k, v in st.items()}
    return {
        "puid": _texto(d.get("puid")) or "",
        "deviceId": _texto(d.get("deviceId")) or "",
        "apelido": _texto(d.get("deviceNickName")) or "",
        "tipo": _texto(d.get("deviceTypeCode")) or "",
        "tipoNome": _texto(d.get("deviceTypeName")) or "",
        "modelo": (_texto(d.get("deviceFeatureName")) or "").strip(),
        "codigoDoModelo": _texto(d.get("deviceFeatureCode")) or "",
        "comodo": _texto(d.get("roomName")) or "",
        # A nuvem usa offlineState == 1 para "conectado".
        "online": _texto(d.get("offlineState")) == "1",
        "estado": estado,
    }


def ar_condicionado(a):
    return a["tipo"] in ("009", "008")


def lavadora(a):
    return a["tipo"] in ("003", "025", "027", "004")
