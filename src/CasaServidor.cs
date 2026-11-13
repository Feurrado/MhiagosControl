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

        /// <summary>Endereco interno (rede local): a reserva quando o externo nao responde.</summary>
        public static string Endereco { get { return (ServicoCasa.Pref.Servidor ?? "").Trim().TrimEnd('/'); } }

        /// <summary>Endereco externo (https pela internet): tentado primeiro quando existe.</summary>
        public static string EnderecoExterno { get { return (ServicoCasa.Pref.ServidorExterno ?? "").Trim().TrimEnd('/'); } }

        /// <summary>O endereco para abrir a pagina no navegador: o externo, se houver.</summary>
        public static string EnderecoDaPagina { get { return EnderecoExterno.Length > 0 ? EnderecoExterno : Endereco; } }

        public static bool Configurado
        {
            get { return !Desligado && (Endereco.Length > 0 || EnderecoExterno.Length > 0) && !string.IsNullOrEmpty(Chave); }
        }

        // ---------------- fonte da Casa ----------------

        /// <summary>Formato de /api/aparelhos que este app entende (docs\CONTRATO-CASA.md).</summary>
        internal const int ContratoMinimo = 1;
        /// <summary>Depois de uma falha como fonte, a nuvem direta fica por este tempo.</summary>
        internal const int FonteReservaSeg = 120;
        /// <summary>Leitura do servidor mais velha que isto nao serve: ele esta sem falar com a nuvem.</summary>
        internal const int FonteVelhaSeg = 300;
        private static DateTime _fonteFalhouEm = DateTime.MinValue;

        /// <summary>
        /// O PC le e comanda a Casa pelo servidor (uma sessao so na nuvem, uma regra
        /// so). Falso sem servidor, com servidorfonte=0, ou nos minutos seguintes a
        /// uma falha dele: ai vale a nuvem direta.
        /// </summary>
        public static bool Fonte
        {
            get
            {
                if (!Configurado || !ServicoCasa.Pref.ServidorFonte) return false;
                lock (_trava) return (DateTime.UtcNow - _fonteFalhouEm).TotalSeconds >= FonteReservaSeg;
            }
        }

        internal static void FonteFalhou(Exception ex)
        {
            bool primeira;
            lock (_trava)
            {
                primeira = (DateTime.UtcNow - _fonteFalhouEm).TotalSeconds >= FonteReservaSeg * 2;
                _fonteFalhouEm = DateTime.UtcNow;
            }
            if (primeira) Log.Write("casa: servidor indisponivel como fonte (" + ex.Message + "), usando a nuvem direto");
        }

        /// <summary>Os aparelhos como o servidor leu. forcar pede a ele uma leitura nova antes.</summary>
        public static List<Aparelho> Aparelhos(bool forcar)
        {
            return AparelhosDe(forcar ? Pedir("POST", "/api/aparelhos/ler", new Dictionary<string, object>())
                                      : Pedir("GET", "/api/aparelhos", null));
        }

        /// <summary>A resposta de /api/aparelhos virando a lista do app. Lanca se nao serve como fonte.</summary>
        internal static List<Aparelho> AparelhosDe(Dictionary<string, object> r)
        {
            if (r == null || Long(r, "contrato") < ContratoMinimo) throw new IOException(T.ServidorAntigo);
            if (!Bool(r, "logado", false)) throw new IOException(T.ServidorSemConta);
            long agora = Long(r, "agora"), lida = Long(r, "ultimaLeitura");
            if (lida <= 0 || agora - lida > FonteVelhaSeg) throw new IOException(T.ServidorLeituraVelha);
            List<object> lista = r.ContainsKey("lista") ? r["lista"] as List<object> : null;
            if (lista == null) throw new IOException(T.ServidorAntigo);
            List<Aparelho> l = new List<Aparelho>();
            foreach (object o in lista)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                Aparelho a = new Aparelho();
                a.Puid = Json.Texto(d, "puid") ?? "";
                a.DeviceId = Json.Texto(d, "deviceId") ?? "";
                a.Apelido = Json.Texto(d, "apelido") ?? "";
                a.Tipo = Json.Texto(d, "tipo") ?? "";
                a.TipoNome = Json.Texto(d, "tipoNome") ?? "";
                a.Modelo = (Json.Texto(d, "modelo") ?? "").Trim();
                a.CodigoDoModelo = Json.Texto(d, "codigoDoModelo") ?? "";
                a.Comodo = Json.Texto(d, "comodo") ?? "";
                a.Online = Bool(d, "online", false);
                Dictionary<string, object> st = d.ContainsKey("estado") ? d["estado"] as Dictionary<string, object> : null;
                if (st != null)
                    foreach (KeyValuePair<string, object> kv in st)
                        a.Estado[kv.Key] = kv.Value is string ? (string)kv.Value : Json.Escrever(kv.Value);
                if (a.Puid.Length > 0) l.Add(a);
            }
            return l;
        }

        /// <summary>Manda as propriedades pelo servidor; ele divide em partes e cuida do bipe. Lanca na falha.</summary>
        public static void Comando(string puid, Dictionary<string, string> props, bool semBipe)
        {
            Pedir("POST", "/api/aparelhos/" + Uri.EscapeDataString(puid) + "/comando", CargaDoComando(props, semBipe));
        }

        internal static Dictionary<string, object> CargaDoComando(Dictionary<string, string> props, bool semBipe)
        {
            Dictionary<string, object> p = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> kv in props) p[kv.Key] = kv.Value;
            Dictionary<string, object> carga = new Dictionary<string, object>();
            carga["props"] = p;
            carga["semBipe"] = semBipe;
            // Quem cancela o retorno da cena e o PC, pelo id do agendamento: aqui nunca e "manual".
            carga["manual"] = false;
            return carga;
        }

        // Depois de uma falha do externo, o interno vai primeiro por este tempo (sem esperar
        // o limite de tempo do externo a cada pedido).
        private const int ReservaSeg = 15;
        private const int LimiteDoPrimeiroMs = 5000;
        private static DateTime _externoFalhouEm = DateTime.MinValue;

        /// <summary>A chave para a pagina do proprio servidor (JanelaWeb); nunca vai para log nem para outra origem.</summary>
        internal static string ChaveDaPagina { get { return Chave; } }

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

        /// <summary>Grava os enderecos e a chave (chave vazia mantem a guardada) e sincroniza.</summary>
        public static void Configurar(string externo, string interno, string chave)
        {
            ServicoCasa.Pref.ServidorExterno = (externo ?? "").Trim().TrimEnd('/');
            ServicoCasa.Pref.Servidor = (interno ?? "").Trim().TrimEnd('/');
            ServicoCasa.Pref.Salvar();
            if (!string.IsNullOrEmpty(chave))
            {
                if (!Cofre.Gravar(AlvoDoCofre, chave.Trim())) Log.Write("casa: chave do servidor nao gravada no cofre");
                lock (_trava) _chave = chave.Trim();
            }
            if (Endereco.Length == 0 && EnderecoExterno.Length == 0) Definir("", false);
            Iniciar();
            Sincronizar();
        }

        /// <summary>Esquece o servidor: o PC volta a ser o unico a ler e a disparar timers.</summary>
        public static void Esquecer()
        {
            ServicoCasa.Pref.Servidor = "";
            ServicoCasa.Pref.ServidorExterno = "";
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

        /// <summary>
        /// Testa cada endereco preenchido (externo e interno) com a mesma chave. Nulo quando todos
        /// responderam; senao o motivo, dizendo qual falhou.
        /// </summary>
        public static string TestarEnderecos(string externo, string interno, string chave)
        {
            bool algum;
            return TestarEnderecos(externo, interno, chave, out algum);
        }

        /// <summary>Como acima; algumOk diz se pelo menos um dos enderecos preenchidos respondeu (basta para salvar: o outro e reserva).</summary>
        public static string TestarEnderecos(string externo, string interno, string chave, out bool algumOk)
        {
            algumOk = false;
            List<string> erros = new List<string>();
            string e = (externo ?? "").Trim(), i = (interno ?? "").Trim();
            if (e.Length > 0) { string r = Testar(e, chave); if (r != null) erros.Add(T.ServidorEnderecoExterno + ": " + r); else algumOk = true; }
            if (i.Length > 0) { string r = Testar(i, chave); if (r != null) erros.Add(T.ServidorEnderecoInterno + ": " + r); else algumOk = true; }
            return erros.Count == 0 ? null : string.Join(" | ", erros.ToArray());
        }

        /// <summary>
        /// Com os dois enderecos, tenta o externo e, se ele nao responder (sem rede, sem resposta,
        /// tempo esgotado), o interno. Resposta de erro do servidor (401, 404...) nao troca de
        /// endereco: ele respondeu. Um pedido que ja enviou corpo (POST) nao e repetido no outro
        /// endereco, para nao duplicar o que o servidor pode ter feito.
        /// </summary>
        private static Dictionary<string, object> Pedir(string metodo, string caminho, Dictionary<string, object> carga)
        {
            string externo = EnderecoExterno, interno = Endereco, chave = Chave;
            if (externo.Length == 0) return Pedir(metodo, caminho, carga, interno, chave);
            if (interno.Length == 0) return Pedir(metodo, caminho, carga, externo, chave);
            bool internoPrimeiro;
            lock (_trava) internoPrimeiro = (DateTime.UtcNow - _externoFalhouEm).TotalSeconds < ReservaSeg;
            string primeiro = internoPrimeiro ? interno : externo, segundo = internoPrimeiro ? externo : interno;
            try
            {
                Dictionary<string, object> r = Pedir(metodo, caminho, carga, primeiro, chave, LimiteDoPrimeiroMs);
                if (!internoPrimeiro) lock (_trava) _externoFalhouEm = DateTime.MinValue;
                return r;
            }
            catch (ServidorSemRespostaErro ex)
            {
                if (ex.CorpoEnviado) throw;
                if (!internoPrimeiro) lock (_trava) _externoFalhouEm = DateTime.UtcNow;
                Log.Write("casa: " + (internoPrimeiro ? "interno" : "externo") + " sem resposta, tentando o outro endereco");
            }
            try
            {
                Dictionary<string, object> r = Pedir(metodo, caminho, carga, segundo, chave);
                if (internoPrimeiro) lock (_trava) _externoFalhouEm = DateTime.MinValue;
                return r;
            }
            catch (ServidorSemRespostaErro)
            {
                if (internoPrimeiro) lock (_trava) _externoFalhouEm = DateTime.UtcNow;
                throw;
            }
        }

        private static Dictionary<string, object> Pedir(string metodo, string caminho, Dictionary<string, object> carga,
            string endereco, string chave)
        {
            return Pedir(metodo, caminho, carga, endereco, chave, 15000);
        }

        private static Dictionary<string, object> Pedir(string metodo, string caminho, Dictionary<string, object> carga,
            string endereco, string chave, int limiteMs)
        {
            if (endereco.IndexOf("://", StringComparison.Ordinal) < 0) endereco = "http://" + endereco;
            // So http(s): o endereco vem do casa.ini, gravavel sem elevacao, e um "file:"
            // viraria leitura e escrita de arquivo pelo app elevado (FileWebRequest).
            Uri seguro;
            if (!Navegador.Seguro(endereco, out seguro)) throw new IOException(T.ServidorEnderecoInvalido);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endereco + caminho);
            req.Method = metodo;
            req.UserAgent = "MhiagosControl";
            req.Timeout = limiteMs;
            req.ReadWriteTimeout = 30000;
            req.Accept = "application/json";
            req.Headers["Authorization"] = "Bearer " + chave;
            bool corpoEnviado = false;
            try
            {
                if (carga != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(Json.Escrever(carga));
                    req.ContentType = "application/json";
                    req.ContentLength = b.Length;
                    using (Stream s = req.GetRequestStream()) { s.Write(b, 0, b.Length); corpoEnviado = true; }
                }
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                    return Json.Ler(sr.ReadToEnd()) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r == null) throw new ServidorSemRespostaErro(corpoEnviado);
                using (r)
                {
                    if (r.StatusCode == HttpStatusCode.Unauthorized) throw new ServidorHttpErro(401, T.ServidorChaveErrada);
                    // O proxy (Funnel) ou o limite de tentativas respondeu no lugar do servidor: o pedido nao
                    // foi processado, entao o outro endereco pode tentar (como se nao houvesse resposta).
                    int codigo = (int)r.StatusCode;
                    if (codigo == 429 || codigo == 502 || codigo == 503 || codigo == 504)
                        throw new ServidorSemRespostaErro(false, T.ServidorErroHttp(codigo));
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

    /// <summary>Nenhum endereco respondeu (sem rede, recusou, tempo esgotado). CorpoEnviado: um POST ja foi escrito.</summary>
    internal sealed class ServidorSemRespostaErro : IOException
    {
        public readonly bool CorpoEnviado;
        public ServidorSemRespostaErro(bool corpoEnviado) : base(T.ServidorSemResposta) { CorpoEnviado = corpoEnviado; }
        public ServidorSemRespostaErro(bool corpoEnviado, string mensagem) : base(mensagem) { CorpoEnviado = corpoEnviado; }
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
        public static string ServidorEnderecoExterno { get { return P("Endereço externo (https://home01.suarede.ts.net)", "External address (https://home01.yournet.ts.net)"); } }
        public static string ServidorEnderecoInterno { get { return P("Endereço interno (192.168.1.100:8765)", "Internal address (192.168.1.100:8765)"); } }
        public static string ServidorChave { get { return P("Chave de acesso (vazio mantém a guardada)", "Access key (empty keeps the saved one)"); } }
        public static string ServidorSalvar { get { return P("Salvar e testar", "Save and test"); } }
        public static string ServidorSincronizar { get { return P("Sincronizar agora", "Sync now"); } }
        public static string ServidorPagina { get { return P("Abrir a página", "Open the page"); } }
        public static string ServidorConfigurado { get { return P("Servidor configurado.", "Server configured."); } }
        public static string ServidorEsquecer { get { return P("Esquecer", "Forget"); } }
        public static string ServidorTestando { get { return P("Testando…", "Testing…"); } }
        public static string ServidorNaoConfigurado { get { return P("Sem servidor: só este PC lê os aparelhos.", "No server: only this PC reads the devices."); } }
        public static string ServidorNota { get { return P("O servidor continua lendo o ar e a lavadora com o PC desligado, guarda o histórico e dispara o timer de desligar e o retorno das cenas. Histórico, cenas, ajustes e timers são trocados com ele a cada minuto, então o que mudar no painel web aparece aqui. O endereço externo é tentado primeiro; se não responder, o app usa o interno. Deixe um dos dois vazio para usar só o outro.", "The server keeps reading the AC and washer with the PC off, stores the history and fires the off timer and scene return. History, scenes, settings and timers are exchanged every minute, so changes made on the web panel show up here. The external address is tried first; if it does not answer, the app uses the internal one. Leave one empty to use only the other."); } }
        public static string ServidorSemConta { get { return P("Servidor no ar, mas sem a conta ConnectLife: entre pela página do servidor.", "Server is up but has no ConnectLife account: sign in on the server page."); } }
        public static string ServidorAntigo { get { return P("servidor antigo: atualize o servidor da casa", "old server: update the home server"); } }
        public static string ServidorLeituraVelha { get { return P("o servidor está sem ler a nuvem", "the server is not reading the cloud"); } }
        public static string ServidorSemResposta { get { return P("o servidor não respondeu", "the server did not respond"); } }
        public static string ServidorChaveErrada { get { return P("chave de acesso recusada", "access key rejected"); } }
        public static string ServidorErroHttp(int c) { return P("o servidor respondeu " + c, "the server answered " + c); }
        public static string ServidorEnderecoInvalido { get { return P("endereço inválido: use http:// ou https:// com o IP ou nome do servidor", "invalid address: use http:// or https:// with the server's IP or name"); } }
        public static string ServidorSalvoComAviso(string m) { return P("salvo, mas um endereço não respondeu: " + m, "saved, but one address did not answer: " + m); }
        public static string ServidorFalhou(string m) { return P("Falha ao sincronizar: " + m, "Sync failed: " + m); }
        public static string ServidorSincronizado(DateTime d) { return P("Sincronizado às " + d.ToString("HH:mm"), "Synced at " + d.ToString("HH:mm")); }
    }
}
