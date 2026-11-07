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
    /// tambem sincroniza o historico nos dois sentidos (ao abrir e a cada minuto)
    /// e entrega o timer de desligar e o retorno das cenas para o servidor
    /// disparar. Endereco no casa.ini; a chave da API no Cofre (DPAPI).
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
                // A cada minuto: o historico vai so a diferenca, e timers e ajustes
                // mudados pelo painel web chegam logo.
                _ciclo = new Timer(delegate { ServicoCasa.Protegido("sincronizar com o servidor", Sincronizar); }, null, 5000, 60 * 1000);
            }
        }

        /// <summary>Grava endereco e chave (chave vazia mantem a guardada) e sincroniza.</summary>
        public static void Configurar(string endereco, string chave)
        {
            ServicoCasa.Pref.Servidor = (endereco ?? "").Trim().TrimEnd('/');
            ServicoCasa.Pref.Salvar();
            if (!string.IsNullOrEmpty(chave))
            {
                if (!Cofre.Gravar(AlvoDoCofre, chave.Trim())) Log.Write("casa: chave do servidor nao gravada no cofre");
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
            ServicoCasa.Pref.ServidorPrefVersao = 0;
            ServicoCasa.Pref.ServidorPrefHash = "";
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

            Agendamentos();
            Preferencias();

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

        // ---------------- o que o painel web muda ----------------

        /// <summary>Timer de desligar e retorno criados ou cancelados pelo painel web.</summary>
        private static void Agendamentos()
        {
            Dictionary<string, object> r = Pedir("GET", "/api/agendamentos", null);
            List<object> l = r.ContainsKey("agendamentos") ? r["agendamentos"] as List<object> : null;
            if (l == null) return;
            PreferenciasCasa p = ServicoCasa.Pref;
            foreach (bool desligar in new[] { true, false })
            {
                string nome = desligar ? "desligar" : "retorno";
                Dictionary<string, object> achado = null;
                foreach (object o in l)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d != null && Json.Texto(d, "nome") == nome) { achado = d; break; }
                }
                long idLocal = desligar ? p.ServidorDesligarId : p.ServidorRetornoId;
                long emLocal = desligar ? p.DesligarEmUtc : p.RetornoEmUtc;
                if (achado != null)
                {
                    long id = Long(achado, "id");
                    if (id != idLocal)
                        ServicoCasa.AdotarDoServidor(desligar, Json.Texto(achado, "puid") ?? "", Zero.AddSeconds(Long(achado, "quando")), id);
                }
                else if (idLocal != 0 && emLocal > DateTime.UtcNow.AddSeconds(30).Ticks)
                    ServicoCasa.SoltarDoServidor(desligar);
            }
        }

        /// <summary>
        /// Cenas, potencias, tarifa, retorno e alertas: os mesmos no PC e no
        /// painel web. Mudou so aqui, sobe; mudou so la, desce; nos dois, vale o
        /// do servidor. Na primeira vez, com o servidor ainda sem nada, sobe o do PC.
        /// </summary>
        private static void Preferencias()
        {
            PreferenciasCasa p = ServicoCasa.Pref;
            string hash = Resumo(Json.Escrever(Compartilhadas(p)));
            Dictionary<string, object> sv = Pedir("GET", "/api/preferencias", null);
            long versao = Long(sv, "versao");
            bool mudouAqui = hash != p.ServidorPrefHash;
            bool mudouLa = versao != p.ServidorPrefVersao;
            bool primeira = p.ServidorPrefHash.Length == 0;
            if (mudouAqui && (!mudouLa || (primeira && versao == 0)))
            {
                Dictionary<string, object> carga = new Dictionary<string, object>();
                carga["dados"] = Compartilhadas(p);
                carga["origem"] = "pc";
                p.ServidorPrefVersao = Long(Pedir("PUT", "/api/preferencias", carga), "versao");
                p.ServidorPrefHash = hash;
                return;
            }
            if (!mudouLa) return;
            Dictionary<string, object> dados = sv.ContainsKey("dados") ? sv["dados"] as Dictionary<string, object> : null;
            if (dados == null) return;
            Aplicar(p, dados);
            p.ServidorPrefVersao = versao;
            p.ServidorPrefHash = Resumo(Json.Escrever(Compartilhadas(p)));
            p.Salvar();
            Log.Write("casa: preferencias do servidor aplicadas (versao " + versao + ")");
            ServicoCasa.AvisarMudanca();
        }

        internal static Dictionary<string, object> Compartilhadas(PreferenciasCasa p)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["alertaFalha"] = p.AlertaFalha;
            d["alertaDesconectado"] = p.AlertaDesconectado;
            d["alertaQuente"] = p.AlertaQuente;
            d["alertaLavadoraFim"] = p.AlertaLavadoraFim;
            d["limiteQuente"] = (long)p.LimiteQuente;
            d["potenciaFrio"] = (long)p.PotenciaFrio;
            d["potenciaEco"] = (long)p.PotenciaEco;
            d["potenciaSeco"] = (long)p.PotenciaSeco;
            d["potenciaVentilar"] = (long)p.PotenciaVentilar;
            d["tarifa"] = p.Tarifa;
            d["retornoModo"] = (long)p.RetornoModo;
            d["retornoTemp"] = (long)p.RetornoTemp;
            d["retornoVento"] = (long)p.RetornoVento;
            List<object> cenas = new List<object>();
            foreach (CenaCasa c in p.Cenas)
            {
                Dictionary<string, object> x = new Dictionary<string, object>();
                x["nome"] = c.Nome;
                x["icone"] = c.Icone;
                x["desligar"] = c.Desligar;
                x["modo"] = (long)c.Modo;
                x["temp"] = (long)c.Temp;
                x["vento"] = (long)c.Vento;
                x["noturno"] = (long)c.Noturno;
                x["duracao"] = (long)c.Duracao;
                cenas.Add(x);
            }
            d["cenas"] = cenas;
            return d;
        }

        internal static void Aplicar(PreferenciasCasa p, Dictionary<string, object> d)
        {
            p.AlertaFalha = Bool(d, "alertaFalha", p.AlertaFalha);
            p.AlertaDesconectado = Bool(d, "alertaDesconectado", p.AlertaDesconectado);
            p.AlertaQuente = Bool(d, "alertaQuente", p.AlertaQuente);
            p.AlertaLavadoraFim = Bool(d, "alertaLavadoraFim", p.AlertaLavadoraFim);
            p.LimiteQuente = Math.Max(16, Math.Min(45, Int(d, "limiteQuente", p.LimiteQuente)));
            p.PotenciaFrio = Math.Max(0, Int(d, "potenciaFrio", p.PotenciaFrio));
            p.PotenciaEco = Math.Max(0, Int(d, "potenciaEco", p.PotenciaEco));
            p.PotenciaSeco = Math.Max(0, Int(d, "potenciaSeco", p.PotenciaSeco));
            p.PotenciaVentilar = Math.Max(0, Int(d, "potenciaVentilar", p.PotenciaVentilar));
            float tarifa = Float(d, "tarifa");
            if (!float.IsNaN(tarifa) && tarifa >= 0 && tarifa < 100) p.Tarifa = Math.Round(tarifa, 4);
            p.RetornoModo = Int(d, "retornoModo", p.RetornoModo);
            p.RetornoTemp = Math.Max(16, Math.Min(32, Int(d, "retornoTemp", p.RetornoTemp)));
            p.RetornoVento = Int(d, "retornoVento", p.RetornoVento);
            List<object> l = d.ContainsKey("cenas") ? d["cenas"] as List<object> : null;
            if (l == null) return;
            List<CenaCasa> cenas = new List<CenaCasa>();
            foreach (object o in l)
            {
                Dictionary<string, object> x = o as Dictionary<string, object>;
                string nome = x != null ? (Json.Texto(x, "nome") ?? "").Trim() : "";
                if (nome.Length == 0 || cenas.Count >= CenaCasa.MaxCenas) continue;
                CenaCasa c = new CenaCasa();
                c.Nome = nome;
                string icone = Json.Texto(x, "icone") ?? "";
                c.Icone = IconesDeCena.Existe(icone) ? icone : "floco";
                c.Desligar = Bool(x, "desligar", false);
                c.Modo = Int(x, "modo", -1);
                c.Temp = Int(x, "temp", -1);
                c.Vento = Int(x, "vento", -1);
                c.Noturno = Int(x, "noturno", -1);
                c.Duracao = Math.Max(0, Int(x, "duracao", 0));
                cenas.Add(c);
            }
            p.Cenas = cenas;
        }

        private static bool Bool(Dictionary<string, object> d, string k, bool padrao)
        {
            string s = Json.Texto(d, k);
            return s == "true" ? true : s == "false" ? false : padrao;
        }

        private static int Int(Dictionary<string, object> d, string k, int padrao)
        {
            double v;
            string s = Json.Texto(d, k);
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (int)Math.Round(v) : padrao;
        }

        private static string Resumo(string s)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                return Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(s)));
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

        // Estados de um agendamento no servidor (GET /api/agendamentos/{id}/resultado).
        public const string Pendente = "pendente", Feito = "feito", Falhou = "falhou", Descartado = "descartado",
                            Cancelado = "cancelado", Desconhecido = "desconhecido", Indisponivel = "indisponivel";

        /// <summary>
        /// O que o servidor fez com um agendamento que o PC tambem vigia. Antes
        /// bastava ele estar no ar e logado para o PC dar como feito; se a nuvem
        /// falhasse na hora, o ar ficava ligado e ninguem sabia. Indisponivel:
        /// servidor fora do ar, sem a conta ou sem responder - o PC decide.
        /// </summary>
        public static string EstadoDoAgendamento(long id)
        {
            if (id <= 0 || !Configurado) return Indisponivel;
            try
            {
                Dictionary<string, object> r = Pedir("GET", "/api/agendamentos/" +
                    id.ToString(CultureInfo.InvariantCulture) + "/resultado", null);
                return Json.Texto(r, "estado") ?? Desconhecido;
            }
            catch (ServidorHttpErro ex)
            {
                if (ex.Status != 404) { Log.Write("casa: resultado do agendamento: " + ex.Message); return Indisponivel; }
                // Servidor 1.2 (sem a rota): o jeito antigo, logado vale como feito.
                try { return Json.Texto(Pedir("GET", "/api/saude", null), "logado") == "true" ? Feito : Indisponivel; }
                catch (Exception e2) { Log.Write("casa: servidor indisponivel no timer: " + e2.Message); return Indisponivel; }
            }
            catch (Exception ex)
            {
                Log.Write("casa: servidor indisponivel no timer, o PC decide: " + ex.Message);
                return Indisponivel;
            }
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
            // So http(s): o endereco vem do casa.ini, gravavel sem elevacao, e um "file:"
            // viraria leitura e escrita de arquivo pelo app elevado (FileWebRequest).
            Uri seguro;
            if (!Navegador.Seguro(endereco, out seguro)) throw new IOException(T.ServidorEnderecoInvalido);
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
                    if (r.StatusCode == HttpStatusCode.Unauthorized) throw new ServidorHttpErro(401, T.ServidorChaveErrada);
                    throw new ServidorHttpErro((int)r.StatusCode, T.ServidorErroHttp((int)r.StatusCode));
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

    /// <summary>Resposta HTTP de erro do servidor da casa, com o codigo (404 = rota que a versao dele nao tem).</summary>
    internal sealed class ServidorHttpErro : IOException
    {
        public readonly int Status;
        public ServidorHttpErro(int status, string mensagem) : base(mensagem) { Status = status; }
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
        public static string ServidorNota { get { return P("O servidor continua lendo o ar e a lavadora com o PC desligado, guarda o histórico e dispara o timer de desligar e o retorno das cenas. Histórico, cenas, ajustes e timers são trocados com ele a cada minuto, então o que mudar no painel web aparece aqui.", "The server keeps reading the AC and washer with the PC off, stores the history and fires the off timer and scene return. History, scenes, settings and timers are exchanged every minute, so changes made on the web panel show up here."); } }
        public static string ServidorSemConta { get { return P("Servidor no ar, mas sem a conta ConnectLife: entre pela página do servidor.", "Server is up but has no ConnectLife account: sign in on the server page."); } }
        public static string ServidorSemResposta { get { return P("o servidor não respondeu", "the server did not respond"); } }
        public static string ServidorChaveErrada { get { return P("chave de acesso recusada", "access key rejected"); } }
        public static string ServidorErroHttp(int c) { return P("o servidor respondeu " + c, "the server answered " + c); }
        public static string ServidorEnderecoInvalido { get { return P("endereço inválido: use http:// ou https:// com o IP ou nome do servidor", "invalid address: use http:// or https:// with the server's IP or name"); } }
        public static string ServidorFalhou(string m) { return P("Falha ao sincronizar: " + m, "Sync failed: " + m); }
        public static string ServidorSincronizado(DateTime d) { return P("Sincronizado às " + d.ToString("HH:mm"), "Synced at " + d.ToString("HH:mm")); }
    }
}
