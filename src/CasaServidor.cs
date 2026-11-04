using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// O servidor da casa (pasta servidor/ do repositorio, em Docker na rede
    /// local): le a ConnectLife 24 h com login proprio, guarda historico do ar
    /// e ciclos da lavadora e roda os agendamentos com o PC desligado.
    ///
    /// O PC continua falando direto com a nuvem; com o servidor configurado ele
    /// tambem sincroniza o historico nos dois sentidos (ao abrir e a cada 10 min)
    /// e entrega o timer de desligar e o retorno das cenas para o servidor
    /// disparar. Endereco no casa.ini; a chave da API no Gerenciador de
    /// Credenciais.
    /// </summary>
    internal static class ServidorCasa
    {
        private const string AlvoDoCofre = "MhiagosControl/CasaServidor";
        private static readonly object _trava = new object();
        private static Timer _ciclo;
        private static bool _sincronizando;
        private static string _chave;
        private static string _estado = "";
        private static DateTime _ultimaSinc = DateTime.MinValue;

        /// <summary>Sincronizou, falhou ou mudou de configuracao. Sai de qualquer thread.</summary>
        public static event EventHandler Mudou;

        /// <summary>Testes: nao fala com o servidor.</summary>
        internal static bool Desligado;

        public static string Endereco { get { return (ServicoCasa.Pref.Servidor ?? "").Trim().TrimEnd('/'); } }

        public static bool Configurado
        {
            get { return !Desligado && Endereco.Length > 0 && !string.IsNullOrEmpty(Chave); }
        }

        private static string Chave
        {
            get
            {
                lock (_trava)
                {
                    if (_chave == null) _chave = Cofre.Ler(AlvoDoCofre) ?? "";
                    return _chave;
                }
            }
        }

        /// <summary>O texto da ultima sincronizacao (ou da falha), para a aba Ajustes.</summary>
        public static string Estado { get { lock (_trava) return _estado; } }
        public static DateTime UltimaSinc { get { lock (_trava) return _ultimaSinc; } }

        public static void Iniciar()
        {
            lock (_trava)
            {
                if (_ciclo != null || Desligado) return;
                _ciclo = new Timer(delegate { Sincronizar(); }, null, 5000, 10 * 60 * 1000);
            }
        }

        /// <summary>Grava endereco e chave (chave vazia mantem a guardada) e sincroniza.</summary>
        public static void Configurar(string endereco, string chave)
        {
            ServicoCasa.Pref.Servidor = (endereco ?? "").Trim().TrimEnd('/');
            ServicoCasa.Pref.Salvar();
            if (!string.IsNullOrEmpty(chave))
            {
                if (!Cofre.Gravar(AlvoDoCofre, "CasaServidor", chave.Trim())) Log.Write("casa: chave do servidor nao gravada no cofre");
                lock (_trava) _chave = chave.Trim();
            }
            if (Endereco.Length == 0) Definir("", false);
            Iniciar();
            Sincronizar();
        }

        /// <summary>Esquece o servidor: o PC volta a ser o unico a ler e a disparar timers.</summary>
        public static void Esquecer()
        {
            ServicoCasa.Pref.Servidor = "";
            ServicoCasa.Pref.ServidorDesligarId = 0;
            ServicoCasa.Pref.ServidorRetornoId = 0;
            ServicoCasa.Pref.Salvar();
            Cofre.Apagar(AlvoDoCofre);
            lock (_trava) _chave = "";
            Definir("", false);
        }

        private static void Definir(string estado, bool sincronizou)
        {
            lock (_trava)
            {
                _estado = estado;
                if (sincronizou) _ultimaSinc = DateTime.Now;
            }
            EventHandler h = Mudou;
            if (h == null) return;
            foreach (EventHandler um in h.GetInvocationList())
            {
                try { um(null, EventArgs.Empty); }
                catch (Exception ex) { Log.Error("casa: aviso do servidor", ex); }
            }
        }

        // ---------------- sincronizacao ----------------

        /// <summary>Sincroniza no fundo, se nao ha outra em andamento.</summary>
        public static void Sincronizar()
        {
            if (!Configurado) return;
            lock (_trava)
            {
                if (_sincronizando) return;
                _sincronizando = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { SincronizarAgora(); }
                catch (Exception ex)
                {
                    Log.Write("casa: sincronizar com o servidor: " + ex.Message);
                    Definir(T.ServidorFalhou(ex.Message), false);
                }
                finally { lock (_trava) _sincronizando = false; }
            });
        }

        private static void SincronizarAgora()
        {
            PreferenciasCasa p = ServicoCasa.Pref;

            Dictionary<string, object> saude = Pedir("GET", "/api/saude", null);
            bool logado = Json.Texto(saude, "logado") == "true";

            // Do servidor para o PC: o que ele leu desde a ultima vez (com folga).
            long desdeAr = Math.Max(0, p.ServidorArAte - 600);
            Dictionary<string, object> ar = Pedir("GET", "/api/historico/ar?desde=" + desdeAr.ToString(CultureInfo.InvariantCulture), null);
            int recebidas = HistoricoDoAr.Mesclar(Amostras(ar));
            long desdeCiclos = Math.Max(0, p.ServidorCiclosAte - 600);
            Dictionary<string, object> ci = Pedir("GET", "/api/historico/ciclos?desde=" + desdeCiclos.ToString(CultureInfo.InvariantCulture), null);
            int ciclosRecebidos = CiclosDaLavadora.Mesclar(Ciclos(ci));

            // Do PC para o servidor: o que so o PC leu (servidor parado). Repetido nao duplica la.
            long enviadoAte = p.ServidorEnviadoAte;
            long agoraPc = Seg(DateTime.Now);
            List<object> amostras = new List<object>();
            foreach (KeyValuePair<string, AmostraAr> kv in HistoricoDoAr.Desde(Epoca(enviadoAte)))
            {
                Dictionary<string, object> a = new Dictionary<string, object>();
                a["puid"] = kv.Key;
                a["t"] = Seg(kv.Value.Quando);
                if (!float.IsNaN(kv.Value.Ambiente)) a["amb"] = (double)kv.Value.Ambiente;
                if (!float.IsNaN(kv.Value.Alvo)) a["alvo"] = (double)kv.Value.Alvo;
                a["ligado"] = kv.Value.Ligado ? 1L : 0L;
                a["modo"] = (long)kv.Value.Modo;
                amostras.Add(a);
            }
            List<object> ciclos = new List<object>();
            foreach (KeyValuePair<string, CiclosDaLavadora.Ciclo> kv in CiclosDaLavadora.TerminadosDesde(Epoca(enviadoAte)))
            {
                Dictionary<string, object> c = new Dictionary<string, object>();
                c["puid"] = kv.Key;
                c["inicio"] = Seg(kv.Value.Inicio);
                c["fim"] = Seg(kv.Value.Fim);
                c["programa"] = (long)kv.Value.Programa;
                c["agua"] = kv.Value.Agua;
                c["energia"] = kv.Value.Energia;
                c["concluido"] = kv.Value.Concluido ? 1L : 0L;
                ciclos.Add(c);
            }
            if (amostras.Count > 0 || ciclos.Count > 0)
            {
                Dictionary<string, object> carga = new Dictionary<string, object>();
                carga["amostras"] = amostras;
                carga["ciclos"] = ciclos;
                Pedir("POST", "/api/historico/importar", carga);
            }

            long agoraAr, agoraCi;
            long.TryParse(Json.Texto(ar, "agora") ?? "0", out agoraAr);
            long.TryParse(Json.Texto(ci, "agora") ?? "0", out agoraCi);
            p.ServidorArAte = agoraAr;
            p.ServidorCiclosAte = agoraCi;
            p.ServidorEnviadoAte = agoraPc;
            p.Salvar();
            if (recebidas > 0 || ciclosRecebidos > 0)
                Log.Write("casa: servidor mandou " + recebidas + " amostras e " + ciclosRecebidos + " ciclos");
            Definir(logado ? T.ServidorSincronizado(DateTime.Now) : T.ServidorSemConta, true);
        }

        private static List<KeyValuePair<string, AmostraAr>> Amostras(Dictionary<string, object> r)
        {
            List<KeyValuePair<string, AmostraAr>> l = new List<KeyValuePair<string, AmostraAr>>();
            List<object> lista = r != null && r.ContainsKey("amostras") ? r["amostras"] as List<object> : null;
            if (lista == null) return l;
            foreach (object o in lista)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                AmostraAr a = new AmostraAr();
                a.Quando = Epoca(Long(d, "t"));
                a.Ambiente = Float(d, "amb");
                a.Alvo = Float(d, "alvo");
                a.Ligado = Long(d, "ligado") != 0;
                a.Modo = (int)Long(d, "modo");
                l.Add(new KeyValuePair<string, AmostraAr>(Json.Texto(d, "puid") ?? "", a));
            }
            return l;
        }

        private static List<KeyValuePair<string, CiclosDaLavadora.Ciclo>> Ciclos(Dictionary<string, object> r)
        {
            List<KeyValuePair<string, CiclosDaLavadora.Ciclo>> l = new List<KeyValuePair<string, CiclosDaLavadora.Ciclo>>();
            List<object> lista = r != null && r.ContainsKey("ciclos") ? r["ciclos"] as List<object> : null;
            if (lista == null) return l;
            foreach (object o in lista)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                CiclosDaLavadora.Ciclo c = new CiclosDaLavadora.Ciclo();
                c.Inicio = Epoca(Long(d, "inicio"));
                c.Fim = Epoca(Long(d, "fim"));
                c.Programa = (int)Long(d, "programa");
                float agua = Float(d, "agua"), energia = Float(d, "energia");
                c.Agua = float.IsNaN(agua) ? 0 : agua;
                c.Energia = float.IsNaN(energia) ? 0 : energia;
                c.Concluido = Long(d, "concluido") != 0;
                l.Add(new KeyValuePair<string, CiclosDaLavadora.Ciclo>(Json.Texto(d, "puid") ?? "", c));
            }
            return l;
        }

        // ---------------- agendamentos ----------------

        /// <summary>Entrega um comando para o servidor disparar na hora. Devolve o id, ou 0 se nao deu.</summary>
        public static long Agendar(string puid, DateTime quandoUtc, Dictionary<string, string> props, string nome)
        {
            if (!Configurado) return 0;
            try
            {
                Dictionary<string, object> carga = new Dictionary<string, object>();
                carga["puid"] = puid;
                carga["quando"] = Seg(quandoUtc);
                Dictionary<string, object> pp = new Dictionary<string, object>();
                foreach (KeyValuePair<string, string> kv in props) pp[kv.Key] = kv.Value;
                carga["props"] = pp;
                carga["nome"] = nome;
                return Long(Pedir("POST", "/api/agendamentos", carga), "id");
            }
            catch (Exception ex)
            {
                Log.Write("casa: agendar no servidor: " + ex.Message);
                return 0;
            }
        }

        public static void Cancelar(long id)
        {
            if (id <= 0 || !Configurado) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Pedir("DELETE", "/api/agendamentos/" + id.ToString(CultureInfo.InvariantCulture), null); }
                catch (Exception ex) { Log.Write("casa: cancelar agendamento no servidor: " + ex.Message); }
            });
        }

        /// <summary>
        /// O timer local venceu e o servidor tinha o mesmo agendamento. Verdadeiro:
        /// o servidor ja disparou (ou vai disparar), o PC nao manda de novo. Falso
        /// (servidor fora do ar): quem manda e o PC, e o do servidor e cancelado.
        /// </summary>
        public static bool ServidorCuida(long id)
        {
            if (id <= 0 || !Configurado) return false;
            try
            {
                // Pendente ou ja disparado, tanto faz: o laco do servidor roda a cada
                // 10 s. So importa que ele esteja no ar e com a conta.
                if (Json.Texto(Pedir("GET", "/api/saude", null), "logado") == "true") return true;
                Log.Write("casa: servidor sem a conta ConnectLife no timer, o PC dispara");
            }
            catch (Exception ex) { Log.Write("casa: servidor indisponivel no timer, o PC dispara: " + ex.Message); }
            Cancelar(id);
            return false;
        }

        // ---------------- HTTP ----------------

        /// <summary>Testa endereco e chave sem gravar nada. Nulo quando deu certo, ou o motivo.</summary>
        public static string Testar(string endereco, string chave)
        {
            try
            {
                Pedir("GET", "/api/aparelhos", null, (endereco ?? "").Trim().TrimEnd('/'),
                    string.IsNullOrEmpty(chave) ? Chave : chave.Trim());
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static Dictionary<string, object> Pedir(string metodo, string caminho, Dictionary<string, object> carga)
        {
            return Pedir(metodo, caminho, carga, Endereco, Chave);
        }

        private static Dictionary<string, object> Pedir(string metodo, string caminho, Dictionary<string, object> carga,
            string endereco, string chave)
        {
            if (endereco.IndexOf("://", StringComparison.Ordinal) < 0) endereco = "http://" + endereco;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endereco + caminho);
            req.Method = metodo;
            req.UserAgent = "MhiagosControl";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 30000;
            req.Accept = "application/json";
            req.Headers["Authorization"] = "Bearer " + chave;
            try
            {
                if (carga != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(Json.Escrever(carga));
                    req.ContentType = "application/json";
                    req.ContentLength = b.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                    return Json.Ler(sr.ReadToEnd()) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null) throw new IOException(T.ServidorSemResposta);
                using (r)
                {
                    if (r.StatusCode == HttpStatusCode.Unauthorized) throw new IOException(T.ServidorChaveErrada);
                    throw new IOException(T.ServidorErroHttp((int)r.StatusCode));
                }
            }
        }

        private static long Long(Dictionary<string, object> d, string k)
        {
            double v;
            string s = Json.Texto(d, k);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (long)v : 0;
        }

        private static float Float(Dictionary<string, object> d, string k)
        {
            double v;
            string s = Json.Texto(d, k);
            return s != null && s != "null" && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (float)v : float.NaN;
        }

        private static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal static DateTime Epoca(long s) { return Zero.AddSeconds(s).ToLocalTime(); }
        internal static long Seg(DateTime d) { return (long)(d.ToUniversalTime() - Zero).TotalSeconds; }
    }

    /// <summary>Segredo generico no Gerenciador de Credenciais (texto unico).</summary>
    internal static class Cofre
    {
        public static string Ler(string alvo)
        {
            IntPtr p;
            if (!CredRead(alvo, 1, 0, out p)) return null;
            try
            {
                CREDENTIAL c = (CREDENTIAL)System.Runtime.InteropServices.Marshal.PtrToStructure(p, typeof(CREDENTIAL));
                if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return null;
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
            }
            finally { CredFree(p); }
        }

        public static bool Gravar(string alvo, string usuario, string segredo)
        {
            byte[] blob = Encoding.Unicode.GetBytes(segredo);
            IntPtr mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(blob.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(blob, 0, mem, blob.Length);
                CREDENTIAL c = new CREDENTIAL();
                c.Type = 1;
                c.TargetName = alvo;
                c.CredentialBlobSize = (uint)blob.Length;
                c.CredentialBlob = mem;
                c.Persist = 2;
                c.UserName = usuario;
                return CredWrite(ref c, 0);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
        }

        public static void Apagar(string alvo) { CredDelete(alvo, 1, 0); }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
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

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CredReadW",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CredWriteW",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CredDeleteW",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [System.Runtime.InteropServices.DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }

    public static partial class T
    {
        public static string ServidorSecao { get { return P("Servidor da casa", "Home server"); } }
        public static string ServidorEndereco { get { return P("Endereço (ex.: 192.168.1.100:8765)", "Address (e.g. 192.168.1.100:8765)"); } }
        public static string ServidorChave { get { return P("Chave de acesso (vazio mantém a guardada)", "Access key (empty keeps the saved one)"); } }
        public static string ServidorSalvar { get { return P("Salvar e testar", "Save and test"); } }
        public static string ServidorSincronizar { get { return P("Sincronizar agora", "Sync now"); } }
        public static string ServidorPagina { get { return P("Abrir a página", "Open the page"); } }
        public static string ServidorConfigurado { get { return P("Servidor configurado.", "Server configured."); } }
        public static string ServidorEsquecer { get { return P("Esquecer", "Forget"); } }
        public static string ServidorTestando { get { return P("Testando…", "Testing…"); } }
        public static string ServidorNaoConfigurado { get { return P("Sem servidor: só este PC lê os aparelhos.", "No server: only this PC reads the devices."); } }
        public static string ServidorNota { get { return P("O servidor continua lendo o ar e a lavadora com o PC desligado, guarda o histórico e dispara o timer de desligar e o retorno das cenas. O histórico é trocado ao abrir o app e a cada 10 minutos.", "The server keeps reading the AC and washer with the PC off, stores the history and fires the off timer and scene return. History is exchanged at startup and every 10 minutes."); } }
        public static string ServidorSemConta { get { return P("Servidor no ar, mas sem a conta ConnectLife: entre pela página do servidor.", "Server is up but has no ConnectLife account: sign in on the server page."); } }
        public static string ServidorSemResposta { get { return P("o servidor não respondeu", "the server did not respond"); } }
        public static string ServidorChaveErrada { get { return P("chave de acesso recusada", "access key rejected"); } }
        public static string ServidorErroHttp(int c) { return P("o servidor respondeu " + c, "the server answered " + c); }
        public static string ServidorFalhou(string m) { return P("Falha ao sincronizar: " + m, "Sync failed: " + m); }
        public static string ServidorSincronizado(DateTime d) { return P("Sincronizado às " + d.ToString("HH:mm"), "Synced at " + d.ToString("HH:mm")); }
    }
}
