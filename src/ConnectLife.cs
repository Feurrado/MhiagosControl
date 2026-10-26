using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Cliente da nuvem ConnectLife (Hisense), pelo login do navegador.
    ///
    /// Os aparelhos nao abrem porta na rede local: o modulo Wi-Fi conversa so
    /// com a nuvem, e o app do celular passa por ela tambem. O caminho que serve
    /// para contas criadas com "Entrar com Google" - que nao tem senha - e o
    /// OAuth2 do navegador, com o cliente que a Hisense publicou para o Home
    /// Assistant:
    ///
    /// 1. Abre oauth.hijuconn.com/login no navegador e a pessoa entra com o Google.
    /// 2. A pagina devolve para my.home-assistant.io/redirect/oauth?code=...
    ///    Essa pagina nao precisa funcionar: so o endereco dela importa.
    /// 3. O code vira tokens; dali em diante o refresh token renova sozinho.
    ///
    /// As chamadas vao para a API de terceiros (juapi-3rd), assinadas com
    /// HMAC-SHA256 nos cabecalhos, como na integracao oficial da Hisense para o
    /// Home Assistant (Connectlife-LLC/HomeAssistantPlugin, MIT).
    /// </summary>
    internal sealed class ConnectLifeNuvem
    {
        internal const string ClientId = "9793620883275788";
        internal const string ClientSecret = "7h1m3gZVlILyBvIFBNmzXwoFYLhkGqG9NQd2jBzuZCqJKCTyCtYwQtXi4tVBjg9B";
        internal const string UrlDeLogin = "https://oauth.hijuconn.com/login";
        internal const string UrlDoToken = "https://oauth.hijuconn.com/oauth/token";
        internal const string Retorno = "https://my.home-assistant.io/redirect/oauth";
        internal const string Base = "https://juapi-3rd.hijuconn.com";

        private const int TokenInvalido = 100026;
        private const int RandStrRepetido = 101005;
        private const string DigestVazio = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

        private readonly object _trava = new object();
        private string _access;
        private DateTime _expira = DateTime.MinValue;

        public string RefreshToken { get; private set; }

        /// <summary>Le o cofre antes de renovar. Desligado nos testes e no login novo.</summary>
        public bool UsarCofre = true;
        public string SourceId { get; private set; }

        /// <summary>O refresh token mudou; quem guarda deve gravar o novo.</summary>
        public event EventHandler TokenMudou;

        static ConnectLifeNuvem()
        {
            // .NET 4 sai so com TLS 1.0 por padrao; a nuvem exige 1.2.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
            catch (Exception ex) { Log.Error("TLS 1.2 para a ConnectLife", ex); }
        }

        public ConnectLifeNuvem(string refreshToken, string sourceId)
        {
            RefreshToken = refreshToken;
            SourceId = string.IsNullOrEmpty(sourceId) ? NovoSourceId() : sourceId;
        }

        public bool Conectado { get { return !string.IsNullOrEmpty(RefreshToken); } }

        // ---------------- login ----------------

        public static string NovoEstado()
        {
            byte[] b = new byte[16];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string EnderecoDeLogin(string estado)
        {
            return UrlDeLogin + "?response_type=code&client_id=" + ClientId +
                "&redirect_uri=" + Uri.EscapeDataString(Retorno) + "&state=" + Uri.EscapeDataString(estado);
        }

        /// <summary>
        /// O code no endereco colado. Aceita o endereco final, o da pagina
        /// intermediaria do Home Assistant (/redirect/_change/?redirect=oauth/?code=...)
        /// e o codigo sozinho. Nulo quando nao ha code nenhum.
        /// </summary>
        public static string ExtrairCodigo(string colado, string estadoEsperado, out string erro)
        {
            erro = null;
            string texto = (colado ?? "").Trim();
            if (texto.Length == 0) { erro = T.CasaColeVazio; return null; }
            if (texto.IndexOf("://", StringComparison.Ordinal) < 0 && texto.IndexOf("code=", StringComparison.Ordinal) < 0)
                return texto.IndexOf(' ') < 0 ? texto : null;

            Dictionary<string, string> p = Consulta(texto);
            for (int i = 0; i < 3 && !p.ContainsKey("code"); i++)
            {
                string aninhado;
                if (!p.TryGetValue("redirect", out aninhado) || aninhado.IndexOf('?') < 0) break;
                p = Consulta(aninhado);
            }

            string code;
            if (!p.TryGetValue("code", out code) || code.Length == 0) { erro = T.CasaSemCodigo; return null; }
            string estado;
            if (!string.IsNullOrEmpty(estadoEsperado) && p.TryGetValue("state", out estado) &&
                estado.Length > 0 && estado != estadoEsperado)
            {
                erro = T.CasaEstadoAntigo;
                return null;
            }
            return code;
        }

        private static Dictionary<string, string> Consulta(string texto)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            int q = texto.IndexOf('?');
            string consulta = q >= 0 ? texto.Substring(q + 1) : texto;
            int h = consulta.IndexOf('#');
            if (h >= 0) consulta = consulta.Substring(0, h);
            foreach (string par in consulta.Split('&'))
            {
                if (par.Length == 0) continue;
                int eq = par.IndexOf('=');
                string k = Uri.UnescapeDataString((eq < 0 ? par : par.Substring(0, eq)).Replace('+', ' '));
                string v = eq < 0 ? "" : Uri.UnescapeDataString(par.Substring(eq + 1).Replace('+', ' '));
                if (!d.ContainsKey(k)) d[k] = v;
            }
            return d;
        }

        public void TrocarCodigo(string code)
        {
            Token(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", Retorno },
                { "client_id", ClientId }, { "client_secret", ClientSecret }
            });
        }

        private void Renovar()
        {
            // O refresh token troca a cada renovacao. Se outra instancia (ou uma
            // versao anterior) ja renovou, o do cofre e o valido: usar o velho
            // derrubaria a sessao.
            string doCofre, fonte;
            if (UsarCofre && CofreConnectLife.Ler(out doCofre, out fonte) && doCofre != RefreshToken && fonte == SourceId)
                RefreshToken = doCofre;
            if (string.IsNullOrEmpty(RefreshToken)) throw new ConnectLifeErro(T.CasaSessaoExpirou, true);
            Token(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" }, { "refresh_token", RefreshToken },
                { "client_id", ClientId }, { "client_secret", ClientSecret }
            });
        }

        private void Token(Dictionary<string, string> campos)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in campos)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }

            HttpWebRequest req = Pedido(UrlDoToken, "POST");
            req.ContentType = "application/x-www-form-urlencoded";
            int status;
            string resposta = Enviar(req, sb.ToString(), out status);
            if (status != 200)
            {
                Log.Write("ConnectLife: token recusado " + status + " " + Corte(resposta));
                if (status >= 400 && status < 500) throw new ConnectLifeErro(T.CasaLoginRecusado, true);
                throw new ConnectLifeErro(T.CasaFalhaTemporaria, false);
            }

            Dictionary<string, object> o = Json.Ler(resposta) as Dictionary<string, object>;
            string access = o != null ? Json.Texto(o, "access_token") : null;
            if (string.IsNullOrEmpty(access)) throw new ConnectLifeErro(T.CasaLoginRecusado, true);

            long seg = 3600;
            long.TryParse(Json.Texto(o, "expires_in") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out seg);
            if (seg <= 0) seg = 3600;

            lock (_trava)
            {
                _access = access;
                _expira = DateTime.UtcNow.AddSeconds(Math.Max(60, seg - 90));
                string novo = Json.Texto(o, "refresh_token");
                if (!string.IsNullOrEmpty(novo)) RefreshToken = novo;
            }
            if (TokenMudou != null) TokenMudou(this, EventArgs.Empty);
        }

        private string Acesso()
        {
            lock (_trava)
                if (_access != null && DateTime.UtcNow < _expira) return _access;
            Renovar();
            lock (_trava) return _access;
        }

        // ---------------- aparelhos ----------------

        public List<Aparelho> ListarAparelhos()
        {
            Dictionary<string, object> r = Chamar(Base + "/clife-svc/pu/get_device_status_list", "GET", null, true, true);
            List<object> lista = r.ContainsKey("deviceList") ? r["deviceList"] as List<object> : null;
            if (lista == null) throw new ConnectLifeErro(T.CasaRespostaEstranha, false);

            List<Aparelho> l = new List<Aparelho>();
            foreach (object item in lista)
            {
                Dictionary<string, object> d = item as Dictionary<string, object>;
                if (d != null && d.ContainsKey("deviceId")) l.Add(Aparelho.De(d));
            }
            return l;
        }

        /// <summary>O que um modelo declara de cada propriedade: gravavel e valores aceitos.</summary>
        public Dictionary<string, MetaPropriedade> Propriedades(string tipo, string modelo)
        {
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["deviceTypeCode"] = tipo;
            c["deviceFeatureCode"] = modelo;
            return MetaPropriedade.DaResposta(Chamar(Base + "/clife-svc/get_property_list", "GET", c, true, true));
        }

        /// <summary>A lista oficial de falhas do modelo e as ativas (basic/self_check/info).</summary>
        public Dictionary<string, object> AutoDiagnostico(string puid)
        {
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["noRecord"] = "1";
            c["puid"] = puid;
            return Chamar(Base + "/basic/self_check/info", "POST", c, true, true);
        }

        public void Definir(string puid, IDictionary<string, string> propriedades)
        {
            Dictionary<string, object> props = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> kv in propriedades) props[kv.Key] = kv.Value;
            Dictionary<string, object> carga = new Dictionary<string, object>();
            carga["puid"] = puid;
            carga["properties"] = props;
            Chamar(Base + "/device/pu/property/set", "POST", carga, true, true);
        }

        private Dictionary<string, object> Chamar(string url, string metodo, Dictionary<string, object> carga,
            bool repetirToken, bool repetirRandStr)
        {
            string access = Acesso();
            Dictionary<string, object> dados = new Dictionary<string, object>();
            if (carga != null) foreach (KeyValuePair<string, object> kv in carga) dados[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, object> kv in Sistema()) dados[kv.Key] = kv.Value;

            string corpo = null;
            string alvo = url;
            if (metodo == "GET")
            {
                StringBuilder q = new StringBuilder();
                foreach (KeyValuePair<string, object> kv in dados)
                {
                    if (q.Length > 0) q.Append('&');
                    q.Append(kv.Key).Append('=').Append(kv.Value is string ? (string)kv.Value : Json.Escrever(kv.Value));
                }
                if (q.Length > 0) alvo = url + "?" + q;
            }
            else
            {
                dados["accessToken"] = access;
                corpo = Json.Escrever(dados);
            }

            HttpWebRequest req = Pedido(alvo, metodo);
            req.ContentType = "application/json";
            Assinar(req, metodo, alvo, corpo);
            if (metodo == "GET") req.Headers["accessToken"] = access;

            int status;
            string resposta = Enviar(req, corpo, out status);
            if (status != 200)
            {
                Log.Write("ConnectLife: " + Caminho(url) + " respondeu " + status + " " + Corte(resposta));
                if (status == 401 || status == 403) throw new ConnectLifeErro(T.CasaSessaoExpirou, true);
                throw new ConnectLifeErro(T.CasaFalhaTemporaria, false);
            }

            Dictionary<string, object> o = Json.Ler(resposta) as Dictionary<string, object>;
            if (o == null) throw new ConnectLifeErro(T.CasaRespostaEstranha, false);

            string resultado = Json.Texto(o, "resultCode");
            if (resultado == null || resultado == "0") return o;

            string codigoTexto = Json.Texto(o, "errorCode") ?? resultado;
            int codigo;
            int.TryParse(codigoTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out codigo);
            string desc = Json.Texto(o, "errorDesc") ?? Json.Texto(o, "msg") ?? "";
            Log.Write("ConnectLife: " + Caminho(url) + " erro " + codigoTexto + " " + desc);

            if (codigo == TokenInvalido && repetirToken)
            {
                lock (_trava) _access = null;
                return Chamar(url, metodo, carga, false, repetirRandStr);
            }
            if (codigo == RandStrRepetido && repetirRandStr)
                return Chamar(url, metodo, carga, repetirToken, false);
            if (codigo == TokenInvalido) throw new ConnectLifeErro(T.CasaSessaoExpirou, true);
            throw new ConnectLifeErro(string.Format(T.CasaErroDaNuvem, codigoTexto, desc), false);
        }

        private Dictionary<string, object> Sistema()
        {
            string ts = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds)
                .ToString(CultureInfo.InvariantCulture);
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["timeStamp"] = ts;
            d["version"] = "8.1";
            d["languageId"] = "1";
            d["timezone"] = "UTC";
            d["randStr"] = Hex(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N") + ts)));
            d["appId"] = ClientId;
            d["sourceId"] = SourceId;
            d["platformId"] = 5L;
            return d;
        }

        private static void Assinar(HttpWebRequest req, string metodo, string url, string corpo)
        {
            DateTime agora = DateTime.UtcNow;
            agora = new DateTime(agora.Year, agora.Month, agora.Day, agora.Hour, agora.Minute, agora.Second, DateTimeKind.Utc);
            string data = agora.ToString("r", CultureInfo.InvariantCulture);
            string assinar = ClientId + "\n" + metodo + " " + Caminho(url) + "\ndate: " + data +
                "\nhi-params-encrypt: " + ClientId + "\n";
            string assinatura;
            using (HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes(ClientSecret)))
                assinatura = Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(assinar)));

            req.Date = agora;
            req.Headers["hi-params-encrypt"] = ClientId;
            req.Headers["Authorization"] = "Signature signature=\"" + assinatura + "\", keyId=\"" + ClientId +
                "\",algorithm=\"hmac-sha256\", headers=\"@request-target date hi-params-encrypt\"";
            string digest = string.IsNullOrEmpty(corpo) ? DigestVazio
                : Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(corpo)));
            req.Headers["Digest"] = "SHA-256=" + digest;
        }

        /// <summary>O caminho com a consulta, sem esquema e host: e o que entra na assinatura.</summary>
        internal static string Caminho(string url)
        {
            int i = url.IndexOf("://", StringComparison.Ordinal);
            if (i < 0) return url;
            int barra = url.IndexOf('/', i + 3);
            return barra < 0 ? "" : url.Substring(barra);
        }

        private static HttpWebRequest Pedido(string url, string metodo)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = metodo;
            req.UserAgent = "MhiagosControl";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.Accept = "application/json";
            return req;
        }

        private static string Enviar(HttpWebRequest req, string corpo, out int status)
        {
            try
            {
                if (corpo != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(corpo);
                    req.ContentLength = b.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)r.StatusCode;
                    return Ler(r);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null)
                {
                    Log.Write("ConnectLife: sem resposta de " + req.RequestUri.Host + ": " + ex.Status);
                    throw new ConnectLifeErro(T.CasaSemInternet, false);
                }
                using (r)
                {
                    status = (int)r.StatusCode;
                    return Ler(r);
                }
            }
        }

        private static string Ler(HttpWebResponse r)
        {
            using (Stream s = r.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static string Corte(string s)
        {
            if (s == null) return "";
            return s.Length > 300 ? s.Substring(0, 300) : s;
        }

        private static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static string NovoSourceId()
        {
            string semente = Guid.NewGuid().ToString("N") + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            return "td001002000" + Hex(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(semente)));
        }
    }

    /// <summary>Falha da nuvem com texto para a pessoa. Login=true pede para entrar de novo.</summary>
    internal sealed class ConnectLifeErro : Exception
    {
        public readonly bool PedeLogin;
        public ConnectLifeErro(string mensagem, bool pedeLogin) : base(mensagem) { PedeLogin = pedeLogin; }
    }

    /// <summary>Um aparelho como a nuvem o devolve.</summary>
    internal sealed class Aparelho
    {
        public string Puid, DeviceId, Apelido, Tipo, TipoNome, Modelo, CodigoDoModelo, Comodo;
        /// <summary>O que o modelo declara (get_property_list); nulo ate chegar.</summary>
        public Dictionary<string, MetaPropriedade> Meta;
        public bool Online;
        public Dictionary<string, string> Estado = new Dictionary<string, string>();

        // 009 split, 008 de janela (o AW-12CR2RWGU00 do Thiago).
        public bool ArCondicionado { get { return Tipo == "009" || Tipo == "008"; } }
        public bool Lavadora { get { return Tipo == "003" || Tipo == "025" || Tipo == "027" || Tipo == "004"; } }

        public string Nome
        {
            get
            {
                if (!string.IsNullOrEmpty(Apelido)) return Apelido;
                if (!string.IsNullOrEmpty(Modelo)) return Modelo;
                return DeviceId;
            }
        }

        public static Aparelho De(Dictionary<string, object> d)
        {
            Aparelho a = new Aparelho();
            a.Puid = Json.Texto(d, "puid") ?? "";
            a.DeviceId = Json.Texto(d, "deviceId") ?? "";
            a.Apelido = Json.Texto(d, "deviceNickName") ?? "";
            a.Tipo = Json.Texto(d, "deviceTypeCode") ?? "";
            a.TipoNome = Json.Texto(d, "deviceTypeName") ?? "";
            a.Modelo = (Json.Texto(d, "deviceFeatureName") ?? "").Trim();
            a.CodigoDoModelo = Json.Texto(d, "deviceFeatureCode") ?? "";
            a.Comodo = Json.Texto(d, "roomName") ?? "";
            // A nuvem usa offlineState == 1 para "conectado".
            a.Online = Json.Texto(d, "offlineState") == "1";
            Dictionary<string, object> st = d.ContainsKey("statusList") ? d["statusList"] as Dictionary<string, object> : null;
            if (st != null)
                foreach (KeyValuePair<string, object> kv in st)
                    a.Estado[kv.Key] = kv.Value is string ? (string)kv.Value : Json.Escrever(kv.Value);
            return a;
        }

        /// <summary>
        /// Nome comercial de modelos conhecidos, por tipo-feature. A nuvem so manda
        /// o deviceFeatureName interno, que para o ar do Thiago e uma descricao em
        /// chines ("301单冷..."); o modelo da etiqueta veio dele.
        /// </summary>
        private static readonly Dictionary<string, string> NomesComerciais = new Dictionary<string, string>
        {
            { "008-301", "Hisense AW-12CR2RWGU00" }
        };

        /// <summary>O modelo para mostrar: o comercial, o da nuvem ou, se for chines, o tipo em portugues.</summary>
        public string ModeloLegivel
        {
            get
            {
                string n;
                if (NomesComerciais.TryGetValue(Tipo + "-" + CodigoDoModelo, out n)) return n;
                foreach (char ch in Modelo ?? "")
                    if (ch >= '\u2E80' && ch <= '\u9FFF' || ch >= '\uFF00' && ch <= '\uFFEF') return T.CasaTipo(Tipo, TipoNome);
                return Modelo;
            }
        }

        public bool Tem(string chave) { return Estado.ContainsKey(chave); }

        public int Int(string chave, int padrao)
        {
            return (int)Math.Round(Num(chave, padrao), MidpointRounding.AwayFromZero);
        }

        public double Num(string chave, double padrao)
        {
            string s;
            double v;
            if (Estado.TryGetValue(chave, out s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return padrao;
        }

        public string Primeiro(params string[] chaves)
        {
            foreach (string c in chaves) if (Estado.ContainsKey(c)) return c;
            return null;
        }
    }

    /// <summary>
    /// O acesso guardado no Gerenciador de Credenciais do Windows: so o refresh
    /// token e o identificador do cliente, que cabem folgados no limite do cofre.
    /// O access token vive so na memoria e e renovado ao abrir.
    /// </summary>
    internal static class CofreConnectLife
    {
        private const string Alvo = "MhiagosControl/ConnectLife";

        public static bool Ler(out string refresh, out string sourceId)
        {
            refresh = null; sourceId = null;
            IntPtr p;
            if (!CredRead(Alvo, 1, 0, out p)) return false;
            try
            {
                CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return false;
                string texto = Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
                Dictionary<string, object> o = Json.Ler(texto) as Dictionary<string, object>;
                if (o == null) return false;
                refresh = Json.Texto(o, "refresh_token");
                sourceId = Json.Texto(o, "source_id");
                return !string.IsNullOrEmpty(refresh);
            }
            catch (Exception ex) { Log.Error("ler o acesso da ConnectLife", ex); return false; }
            finally { CredFree(p); }
        }

        public static bool Gravar(string refresh, string sourceId)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["refresh_token"] = refresh;
            o["source_id"] = sourceId;
            byte[] blob = Encoding.Unicode.GetBytes(Json.Escrever(o));
            IntPtr mem = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, mem, blob.Length);
                CREDENTIAL c = new CREDENTIAL();
                c.Type = 1;                       // CRED_TYPE_GENERIC
                c.TargetName = Alvo;
                c.CredentialBlobSize = (uint)blob.Length;
                c.CredentialBlob = mem;
                c.Persist = 2;                    // CRED_PERSIST_LOCAL_MACHINE
                c.UserName = "ConnectLife";
                if (CredWrite(ref c, 0)) return true;
                Log.Write("ConnectLife: CredWrite falhou " + Marshal.GetLastWin32Error());
                return false;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        public static void Apagar()
        {
            if (!CredDelete(Alvo, 1, 0) && Marshal.GetLastWin32Error() != 1168)
                Log.Write("ConnectLife: CredDelete falhou " + Marshal.GetLastWin32Error());
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }

    /// <summary>
    /// JSON minimo, para nao trazer biblioteca: o projeto compila com o csc do
    /// Windows e sem referencias alem do Framework basico.
    /// Objetos viram Dictionary, listas List, numeros double ou long.
    /// </summary>
    internal static class Json
    {
        public static object Ler(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0;
            try
            {
                object v = Valor(s, ref i);
                return v;
            }
            catch (Exception ex)
            {
                Log.Write("JSON invalido: " + ex.Message);
                return null;
            }
        }

        /// <summary>O campo como texto: numero sem expoente, booleano em minusculas.</summary>
        public static string Texto(Dictionary<string, object> o, string chave)
        {
            object v;
            if (o == null || !o.TryGetValue(chave, out v) || v == null) return null;
            if (v is string) return (string)v;
            return Escrever(v);
        }

        private static object Valor(string s, ref int i)
        {
            Espacos(s, ref i);
            if (i >= s.Length) throw new FormatException("fim inesperado");
            char c = s[i];
            if (c == '{')
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++;
                Espacos(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Espacos(s, ref i);
                    string k = Cadeia(s, ref i);
                    Espacos(s, ref i);
                    if (s[i] != ':') throw new FormatException("esperava :");
                    i++;
                    d[k] = Valor(s, ref i);
                    Espacos(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("esperava , ou }");
                }
            }
            if (c == '[')
            {
                List<object> l = new List<object>();
                i++;
                Espacos(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Valor(s, ref i));
                    Espacos(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("esperava , ou ]");
                }
            }
            if (c == '"') return Cadeia(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }

            int ini = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string n = s.Substring(ini, i - ini);
            long l2;
            if (n.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                long.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out l2)) return l2;
            return double.Parse(n, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Espacos(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string Cadeia(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("esperava texto");
            i++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        public static string Escrever(object v)
        {
            StringBuilder sb = new StringBuilder();
            Escrever(sb, v);
            return sb.ToString();
        }

        private static void Escrever(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Cadeia(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double)
            {
                double d = (double)v;
                sb.Append(d == Math.Floor(d) && Math.Abs(d) < 1e15
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            System.Collections.IDictionary d2 = v as System.Collections.IDictionary;
            if (d2 != null)
            {
                sb.Append('{');
                bool primeiro = true;
                foreach (System.Collections.DictionaryEntry kv in d2)
                {
                    if (!primeiro) sb.Append(',');
                    primeiro = false;
                    Cadeia(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Escrever(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            System.Collections.IEnumerable l = v as System.Collections.IEnumerable;
            if (l != null)
            {
                sb.Append('[');
                bool primeiro = true;
                foreach (object x in l)
                {
                    if (!primeiro) sb.Append(',');
                    primeiro = false;
                    Escrever(sb, x);
                }
                sb.Append(']');
                return;
            }
            Cadeia(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void Cadeia(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
