using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// A conta ConnectLife enquanto o Mhiagos roda, com ou sem janela aberta.
    ///
    /// A janela de configuracao e modal e e destruida ao fechar; alerta, timer
    /// de desligar e historico precisam continuar com o app so na bandeja. Por
    /// isso a conversa com a nuvem mora aqui, e a pagina Casa e so uma vista.
    ///
    /// Le a nuvem a cada 60 s (30 s com a pagina a vista). Os eventos saem de
    /// threads de fundo: quem desenha marshala para a sua.
    /// </summary>
    internal static class ServicoCasa
    {
        private static readonly object _trava = new object();
        private static ConnectLifeNuvem _nuvem;
        private static List<Aparelho> _aparelhos = new List<Aparelho>();
        private static Timer _ciclo, _timerDesligar, _timerRetorno;
        private static bool _visivel, _buscando, _simulado, _iniciado;
        private static DateTime _ultimaLeitura = DateTime.MinValue;
        private static string _erro;

        /// <summary>Alguma coisa mudou: lista, erro, timer ou login. Sai de qualquer thread.</summary>
        public static event EventHandler Mudou;

        /// <summary>Aviso do Windows (titulo, texto). A bandeja liga; sem ele, so vai para o log.</summary>
        public static Action<string, string> Notificar;

        /// <summary>"Comandos sem bipe": vem da config do Mhiagos.</summary>
        public static bool SemBipe;

        /// <summary>Testes: nao le o cofre nem fala com a nuvem.</summary>
        internal static bool IgnorarCofre;

        public static PreferenciasCasa Pref = new PreferenciasCasa();

        public static bool Logado { get { lock (_trava) return _nuvem != null; } }
        public static bool Buscando { get { lock (_trava) return _buscando; } }
        public static DateTime UltimaLeitura { get { lock (_trava) return _ultimaLeitura; } }
        public static string Erro { get { lock (_trava) return _erro; } }

        public static List<Aparelho> Aparelhos
        {
            get { lock (_trava) return new List<Aparelho>(_aparelhos); }
        }

        // ---------------- ciclo de vida ----------------

        /// <summary>Le o acesso guardado e as preferencias e comeca a ler a nuvem.</summary>
        public static void Iniciar()
        {
            lock (_trava)
            {
                if (_iniciado) return;
                _iniciado = true;
            }
            Pref = PreferenciasCasa.Carregar();
            HistoricoDoAr.Carregar();
            string refresh, source;
            if (!IgnorarCofre && CofreConnectLife.Ler(out refresh, out source)) Usar(new ConnectLifeNuvem(refresh, source));
            ArmarTimerDesligar(true);
            ArmarRetorno(true);
            Reprogramar();
        }

        private static void Usar(ConnectLifeNuvem nuvem)
        {
            lock (_trava) _nuvem = nuvem;
            if (nuvem == null) return;
            nuvem.TokenMudou += delegate
            {
                if (!CofreConnectLife.Gravar(nuvem.RefreshToken, nuvem.SourceId))
                    Log.Write("ConnectLife: acesso nao gravado no cofre");
            };
        }

        /// <summary>Pagina a vista: le mais vezes, e ja le se a ultima leitura envelheceu.</summary>
        public static void PaginaVisivel(bool visivel)
        {
            lock (_trava) _visivel = visivel;
            Reprogramar();
            if (visivel && (DateTime.Now - UltimaLeitura).TotalSeconds > 10) Ler();
        }

        private static void Reprogramar()
        {
            lock (_trava)
            {
                if (_simulado || !_iniciado) return;
                int periodo = _visivel ? 30000 : 60000;
                if (_nuvem == null)
                {
                    if (_ciclo != null) { _ciclo.Dispose(); _ciclo = null; }
                    return;
                }
                if (_ciclo == null) _ciclo = new Timer(delegate { Ler(); }, null, 2000, periodo);
                else _ciclo.Change(periodo, periodo);
            }
        }

        private static void Avisar()
        {
            EventHandler h = Mudou;
            if (h == null) return;
            // Um a um: um assinante que falha (o registro de eventos, uma pagina) nao
            // pode deixar os outros - a Visao geral, o widget - sem a leitura nova.
            foreach (EventHandler um in h.GetInvocationList())
            {
                try { um(null, EventArgs.Empty); }
                catch (Exception ex) { Log.Error("casa: aviso de mudanca", ex); }
            }
        }

        // ---------------- login ----------------

        /// <summary>Troca o code por tokens e le a lista. fim recebe a falha, ou nulo. Sai do fundo.</summary>
        public static void Entrar(string code, Action<Exception> fim)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception erro = null;
                try
                {
                    ConnectLifeNuvem nova = new ConnectLifeNuvem(null, null);
                    nova.UsarCofre = false;
                    nova.TrocarCodigo(code);
                    if (!CofreConnectLife.Gravar(nova.RefreshToken, nova.SourceId))
                        Log.Write("ConnectLife: acesso nao gravado no cofre");
                    nova.UsarCofre = true;
                    Usar(nova);
                    lock (_trava) _erro = null;
                    Reprogramar();
                    LerAgora();
                }
                catch (Exception ex) { erro = ex; if (!(ex is ConnectLifeErro)) Log.Error("ConnectLife: login", ex); }
                if (fim != null) fim(erro);
                Avisar();
            });
        }

        public static void Sair()
        {
            CofreConnectLife.Apagar();
            lock (_trava)
            {
                _nuvem = null;
                _aparelhos = new List<Aparelho>();
                _erro = null;
                _ultimaLeitura = DateTime.MinValue;
                _simulado = false;
            }
            CancelarTimer();
            Reprogramar();
            Avisar();
        }

        /// <summary>Testes: aparelhos como se a nuvem tivesse respondido, sem nuvem.</summary>
        internal static void Simular(List<Aparelho> lista)
        {
            lock (_trava)
            {
                _simulado = true;
                _nuvem = new ConnectLifeNuvem("teste", "teste");
                _aparelhos = new List<Aparelho>(lista);
                _ultimaLeitura = DateTime.Now;
                _erro = null;
            }
            Avisar();
        }

        internal static void PararSimulacao()
        {
            lock (_trava) { _simulado = false; _nuvem = null; _aparelhos = new List<Aparelho>(); }
        }

        // ---------------- leitura ----------------

        /// <summary>Le no fundo, se nao ha leitura em andamento.</summary>
        public static void Ler()
        {
            lock (_trava)
            {
                if (_nuvem == null || _buscando || _simulado) return;
                _buscando = true;
            }
            Avisar();
            ThreadPool.QueueUserWorkItem(delegate { LerAgora(); });
        }

        private static void LerAgora()
        {
            ConnectLifeNuvem nuvem;
            lock (_trava) { nuvem = _nuvem; _buscando = true; }
            if (nuvem == null) { lock (_trava) _buscando = false; return; }
            try
            {
                List<Aparelho> lista = nuvem.ListarAparelhos();
                ComMeta(nuvem, lista);
                lock (_trava)
                {
                    if (nuvem != _nuvem) return;   // saiu da conta no meio
                    _aparelhos = lista;
                    _ultimaLeitura = DateTime.Now;
                    _erro = null;
                }
                foreach (Aparelho a in lista)
                {
                    if (a.Lavadora) { CiclosDaLavadora.Conferir(a, Pref); Diagnostico.Atualizar(nuvem, a); continue; }
                    if (!a.ArCondicionado) continue;
                    HistoricoDoAr.Registrar(a, DateTime.Now);
                    Diagnostico.Atualizar(nuvem, a);
                    Alertas.Conferir(a, Pref);
                }
                ConferirTimer();
            }
            catch (ConnectLifeErro ex)
            {
                lock (_trava)
                {
                    if (ex.PedeLogin && nuvem == _nuvem)
                    {
                        CofreConnectLife.Apagar();
                        _nuvem = null;
                        _aparelhos = new List<Aparelho>();
                    }
                    _erro = ex.Message;
                }
                if (ex.PedeLogin) Reprogramar();
            }
            catch (Exception ex)
            {
                Log.Error("ConnectLife: leitura", ex);
                lock (_trava) _erro = T.CasaFalhaInesperada;
            }
            finally
            {
                lock (_trava) _buscando = false;
                Avisar();
            }
        }

        private static readonly Dictionary<string, Dictionary<string, MetaPropriedade>> _metas =
            new Dictionary<string, Dictionary<string, MetaPropriedade>>();

        /// <summary>Junta a cada ar o que o modelo declara (uma consulta por modelo e sessao).</summary>
        private static void ComMeta(ConnectLifeNuvem nuvem, List<Aparelho> lista)
        {
            foreach (Aparelho a in lista)
            {
                if (!a.ArCondicionado) continue;
                string chave = a.Tipo + "/" + a.CodigoDoModelo;
                Dictionary<string, MetaPropriedade> m;
                lock (_metas) _metas.TryGetValue(chave, out m);
                if (m == null)
                {
                    try { m = nuvem.Propriedades(a.Tipo, a.CodigoDoModelo); }
                    catch (Exception ex) { Log.Write("ConnectLife: propriedades de " + chave + " indisponiveis: " + ex.Message); }
                    if (m != null && m.Count > 0) lock (_metas) _metas[chave] = m;
                }
                a.Meta = m;
            }
        }

        public static Aparelho Achar(string puid)
        {
            lock (_trava)
                foreach (Aparelho a in _aparelhos) if (a.Puid == puid) return a;
            return null;
        }

        // ---------------- comandos ----------------

        private static bool Grava(Aparelho a, string chave)
        {
            if (a == null || a.Meta == null) return a != null && a.Tem(chave);
            MetaPropriedade m;
            return a.Meta.TryGetValue(chave, out m) && m.Grava;
        }

        /// <summary>
        /// Propriedades que o aparelho so aceita num comando sozinhas. Testado no
        /// ar de janela do Thiago (008/301) em 26/10/2026: t_sleep=1 junto de
        /// t_power/t_temp/t_fan_speed era aceito pela nuvem e ignorado pelo
        /// aparelho; sozinho, pegava em 5 s.
        /// </summary>
        internal static readonly string[] SoSozinhas = { "t_sleep" };

        /// <summary>O comando dividido: as demais juntas primeiro, cada isolada depois.</summary>
        internal static List<Dictionary<string, string>> Partes(Dictionary<string, string> props)
        {
            List<Dictionary<string, string>> l = new List<Dictionary<string, string>>();
            Dictionary<string, string> juntas = new Dictionary<string, string>();
            List<Dictionary<string, string>> isoladas = new List<Dictionary<string, string>>();
            foreach (KeyValuePair<string, string> kv in props)
            {
                if (Array.IndexOf(SoSozinhas, kv.Key) >= 0 && props.Count > 1)
                {
                    Dictionary<string, string> s = new Dictionary<string, string>();
                    s[kv.Key] = kv.Value;
                    isoladas.Add(s);
                }
                else juntas[kv.Key] = kv.Value;
            }
            if (juntas.Count > 0) l.Add(juntas);
            l.AddRange(isoladas);
            return l;
        }

        /// <summary>
        /// Manda propriedades JUNTAS, num comando so (menos as SoSozinhas, que vao depois). Com "sem bipe" e o modelo
        /// aceitando t_beep, ele vai junto (silencia so aquele comando); se a
        /// nuvem recusar o par, repete sem. fim sai do fundo com a falha ou nulo.
        /// </summary>
        public static void Enviar(string puid, Dictionary<string, string> props, Action<Exception> fim)
        {
            ConnectLifeNuvem nuvem;
            bool simulado;
            lock (_trava) { nuvem = _nuvem; simulado = _simulado; }
            if (nuvem == null || simulado) { if (fim != null) fim(null); return; }
            Aparelho a = Achar(puid);
            bool semBipe = SemBipe && a != null && a.ArCondicionado && Grava(a, "t_beep");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception erro = null;
                try
                {
                    List<Dictionary<string, string>> partes = Partes(props);
                    for (int i = 0; i < partes.Count; i++)
                    {
                        // Um respiro entre partes: o aparelho processa um comando por vez.
                        if (i > 0) Thread.Sleep(1500);
                        Dictionary<string, string> parte = partes[i];
                        if (!semBipe) { nuvem.Definir(puid, parte); continue; }
                        Dictionary<string, string> comBipe = new Dictionary<string, string>(parte);
                        comBipe["t_beep"] = "0";
                        try { nuvem.Definir(puid, comBipe); }
                        catch (ConnectLifeErro e)
                        {
                            if (e.PedeLogin) throw;
                            Log.Write("ConnectLife: comando com t_beep=0 recusado, repetindo sem: " + e.Message);
                            nuvem.Definir(puid, parte);
                        }
                    }
                    lock (_trava) _erro = null;
                }
                catch (Exception ex)
                {
                    erro = ex;
                    if (!(ex is ConnectLifeErro)) Log.Error("ConnectLife: comando", ex);
                    lock (_trava) _erro = ex is ConnectLifeErro ? ex.Message : T.CasaFalhaInesperada;
                }
                if (fim != null) fim(erro);
                // O aparelho leva uns segundos para confirmar na nuvem.
                Thread.Sleep(erro == null ? 4000 : 0);
                LerAgora();
            });
        }

        /// <summary>
        /// Atualizar de verdade: pede a cada ar que mande o estado na hora
        /// (t_device_info, quando o modelo declara) e le em seguida.
        /// </summary>
        public static void Atualizar()
        {
            ConnectLifeNuvem nuvem;
            lock (_trava)
            {
                nuvem = _nuvem;
                if (nuvem == null || _simulado) return;
                _buscando = true;
            }
            Avisar();
            List<Aparelho> lista = Aparelhos;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool pediu = false;
                foreach (Aparelho a in lista)
                {
                    if (!a.ArCondicionado || !a.Online || !Grava(a, "t_device_info")) continue;
                    try
                    {
                        Dictionary<string, string> p = new Dictionary<string, string>();
                        p["t_device_info"] = "1";
                        nuvem.Definir(a.Puid, p);
                        pediu = true;
                    }
                    catch (Exception ex) { Log.Write("ConnectLife: t_device_info recusado: " + ex.Message); }
                }
                if (pediu) Thread.Sleep(3000);
                LerAgora();
            });
        }

        // ---------------- timer de desligar ----------------

        /// <summary>Hora local em que o ar sera desligado, ou nulo.</summary>
        public static DateTime? DesligarEm
        {
            get { lock (_trava) return Pref.DesligarEmUtc > 0 ? (DateTime?)new DateTime(Pref.DesligarEmUtc, DateTimeKind.Utc).ToLocalTime() : null; }
        }

        public static string DesligarPuid { get { lock (_trava) return Pref.DesligarPuid; } }

        public static void DefinirTimer(string puid, int minutos)
        {
            lock (_trava)
            {
                Pref.DesligarEmUtc = DateTime.UtcNow.AddMinutes(minutos).Ticks;
                Pref.DesligarPuid = puid;
            }
            Pref.Salvar();
            Log.Write("casa: timer de desligar em " + minutos + " min");
            CancelarRetorno();
            ArmarTimerDesligar(false);
            Avisar();
        }

        public static void CancelarTimer()
        {
            lock (_trava)
            {
                if (Pref.DesligarEmUtc == 0) return;
                Pref.DesligarEmUtc = 0;
                Pref.DesligarPuid = "";
                if (_timerDesligar != null) { _timerDesligar.Dispose(); _timerDesligar = null; }
            }
            Pref.Salvar();
            Avisar();
        }

        /// <summary>
        /// Arma o disparo no horario exato. Ao abrir o app: horario vencido ha
        /// mais de 15 min e descartado - desligar o ar horas depois de quem o
        /// pediu ja ter ido embora seria surpresa, nao timer.
        /// </summary>
        private static void ArmarTimerDesligar(bool abrindo)
        {
            long alvo;
            lock (_trava) alvo = Pref.DesligarEmUtc;
            if (alvo == 0) return;
            TimeSpan falta = new DateTime(alvo, DateTimeKind.Utc) - DateTime.UtcNow;
            if (abrindo && falta < TimeSpan.FromMinutes(-15))
            {
                Log.Write("casa: timer de desligar vencido ha " + (int)-falta.TotalMinutes + " min, descartado");
                CancelarTimer();
                return;
            }
            long ms = Math.Max(1000, (long)falta.TotalMilliseconds);
            lock (_trava)
            {
                if (_timerDesligar != null) _timerDesligar.Dispose();
                _timerDesligar = new Timer(delegate { ConferirTimer(); }, null, ms, Timeout.Infinite);
            }
        }

        private static void ConferirTimer()
        {
            long alvo;
            string puid;
            lock (_trava) { alvo = Pref.DesligarEmUtc; puid = Pref.DesligarPuid; }
            if (alvo == 0 || DateTime.UtcNow.Ticks < alvo || !Logado) return;
            CancelarTimer();
            Aparelho a = Achar(puid);
            string nome = a != null ? a.Nome : T.CasaAr;
            Dictionary<string, string> p = new Dictionary<string, string>();
            p["t_power"] = "0";
            Enviar(puid, p, delegate(Exception ex)
            {
                if (ex == null) AvisoWindows(T.CasaTimerTitulo(nome), T.CasaTimerDesligou);
                else AvisoWindows(T.CasaTimerTitulo(nome), T.CasaTimerFalhou(ex.Message));
            });
        }

        // ---------------- retorno das cenas ----------------

        /// <summary>Hora local em que o ar volta ao padrao depois de uma cena, ou nulo.</summary>
        public static DateTime? RetornoEm
        {
            get { lock (_trava) return Pref.RetornoEmUtc > 0 ? (DateTime?)new DateTime(Pref.RetornoEmUtc, DateTimeKind.Utc).ToLocalTime() : null; }
        }

        public static string RetornoPuid { get { lock (_trava) return Pref.RetornoPuid; } }

        /// <summary>A cena rodou por tempo: volta ao padrao (Pref.Retorno*) daqui a tantos minutos.</summary>
        public static void AgendarRetorno(string puid, int minutos)
        {
            lock (_trava)
            {
                Pref.RetornoEmUtc = DateTime.UtcNow.AddMinutes(minutos).Ticks;
                Pref.RetornoPuid = puid;
            }
            Pref.Salvar();
            Log.Write("casa: retorno ao padrao em " + minutos + " min");
            ArmarRetorno(false);
            Avisar();
        }

        public static void CancelarRetorno()
        {
            lock (_trava)
            {
                if (Pref.RetornoEmUtc == 0) return;
                Pref.RetornoEmUtc = 0;
                Pref.RetornoPuid = "";
                if (_timerRetorno != null) { _timerRetorno.Dispose(); _timerRetorno = null; }
            }
            Pref.Salvar();
            Avisar();
        }

        /// <summary>Como o timer de desligar: vencido ha mais de 15 min ao abrir, descarta.</summary>
        private static void ArmarRetorno(bool abrindo)
        {
            long alvo;
            lock (_trava) alvo = Pref.RetornoEmUtc;
            if (alvo == 0) return;
            TimeSpan falta = new DateTime(alvo, DateTimeKind.Utc) - DateTime.UtcNow;
            if (abrindo && falta < TimeSpan.FromMinutes(-15))
            {
                Log.Write("casa: retorno vencido ha " + (int)-falta.TotalMinutes + " min, descartado");
                CancelarRetorno();
                return;
            }
            long ms = Math.Max(1000, (long)falta.TotalMilliseconds);
            lock (_trava)
            {
                if (_timerRetorno != null) _timerRetorno.Dispose();
                _timerRetorno = new Timer(delegate { ConferirRetorno(); }, null, ms, Timeout.Infinite);
            }
        }

        /// <summary>O padrao de retorno como cena: liga, modo, temperatura, ventilador e sem noturno.</summary>
        internal static CenaCasa PadraoDeRetorno(PreferenciasCasa p)
        {
            CenaCasa c = new CenaCasa();
            c.Nome = T.CasaRetornoPadrao;
            c.Modo = p.RetornoModo; c.Temp = p.RetornoTemp; c.Vento = p.RetornoVento; c.Noturno = 0;
            return c;
        }

        private static void ConferirRetorno()
        {
            long alvo;
            string puid;
            lock (_trava) { alvo = Pref.RetornoEmUtc; puid = Pref.RetornoPuid; }
            if (alvo == 0 || DateTime.UtcNow.Ticks < alvo || !Logado) return;
            CancelarRetorno();
            Aparelho a = Achar(puid);
            string nome = a != null ? a.Nome : T.CasaAr;
            Dictionary<string, int> cmd = PadraoDeRetorno(Pref).Comando(a);
            if (cmd == null) { AvisoWindows(T.CasaRetornoTitulo(nome), T.CasaTimerFalhou(T.CasaCenaIndisponivel)); return; }
            Dictionary<string, string> p = new Dictionary<string, string>();
            foreach (KeyValuePair<string, int> kv in cmd) p[kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
            Enviar(puid, p, delegate(Exception ex)
            {
                if (ex == null) AvisoWindows(T.CasaRetornoTitulo(nome), T.CasaRetornoFeito);
                else AvisoWindows(T.CasaRetornoTitulo(nome), T.CasaTimerFalhou(ex.Message));
            });
        }

        internal static void AvisoWindows(string titulo, string texto)
        {
            Log.Write("casa: aviso: " + titulo + " - " + texto);
            Action<string, string> n = Notificar;
            if (n != null)
                try { n(titulo, texto); }
                catch (Exception ex) { Log.Error("casa: notificacao", ex); }
        }
    }

    /// <summary>Preferencias da pagina Casa, em casa.ini ao lado da config.</summary>
    internal sealed class PreferenciasCasa
    {
        public bool AlertaFalha = true, AlertaDesconectado = true, AlertaQuente = true, AlertaLavadoraFim = true;
        public int LimiteQuente = 28;
        /// <summary>Potencia por modo, em W. Padrao, nao medido: o modelo nao mede consumo.</summary>
        public int PotenciaFrio = 1100, PotenciaEco = 1100, PotenciaSeco = 600, PotenciaVentilar = 60;
        public double Tarifa = 0.85;
        /// <summary>Aba da pagina Casa: 0 Painel, 1 Ajustes.</summary>
        public int AbaCasa;
        /// <summary>Retorno ao padrao depois de uma cena por tempo, e o padrao (24 °C, Frio, ventilador baixo).</summary>
        public long RetornoEmUtc;
        public string RetornoPuid = "";
        public int RetornoModo = 2, RetornoTemp = 24, RetornoVento = 2;
        /// <summary>Cartao de consumo do ar aberto (comeca fechado).</summary>
        public bool ConsumoArAberto;
        /// <summary>Secao de consumo e historico da lavadora aberta (comeca fechada).</summary>
        public bool ConsumoLavadoraAberto;
        /// <summary>Clima la fora: cidade escolhida (nome e coordenadas) e a ultima leitura guardada.</summary>
        public string ClimaCidade = "";
        public double ClimaLat = double.NaN, ClimaLon = double.NaN, ClimaTemp = double.NaN;
        public int ClimaCodigo = -1;
        public bool ClimaDia = true;
        public long ClimaEmUtc;
        public long DesligarEmUtc;
        public string DesligarPuid = "";
        /// <summary>Cenas do ar, na ordem dos botoes. Sem nenhuma gravada, as de fabrica.</summary>
        public List<CenaCasa> Cenas = CenaCasa.Padrao();

        internal static string Arquivo
        {
            get { return _arquivo ?? Path.Combine(Paths.DataDir, "casa.ini"); }
            set { _arquivo = value; }
        }
        private static string _arquivo;

        public int Potencia(int modo, string tipo)
        {
            switch (modo)
            {
                case 0: return PotenciaVentilar;
                case 3: return PotenciaSeco;
                case 5: return PotenciaEco;
                default: return PotenciaFrio;
            }
        }

        public static PreferenciasCasa Carregar()
        {
            PreferenciasCasa p = new PreferenciasCasa();
            try
            {
                if (!File.Exists(Arquivo)) return p;
                bool lidas = false;
                foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
                {
                    int eq = linha.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = linha.Substring(0, eq).Trim().ToLowerInvariant(), v = linha.Substring(eq + 1).Trim();
                    int i; long l; double d;
                    bool ehInt = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i);
                    switch (k)
                    {
                        case "alertafalha": p.AlertaFalha = v == "1"; break;
                        case "alertadesconectado": p.AlertaDesconectado = v == "1"; break;
                        case "alertaquente": p.AlertaQuente = v == "1"; break;
                        case "alertalavadorafim": p.AlertaLavadoraFim = v == "1"; break;
                        case "limitequente": if (ehInt) p.LimiteQuente = Math.Max(16, Math.Min(45, i)); break;
                        case "potenciafrio": if (ehInt) p.PotenciaFrio = Math.Max(0, i); break;
                        case "potenciaeco": if (ehInt) p.PotenciaEco = Math.Max(0, i); break;
                        case "potenciaseco": if (ehInt) p.PotenciaSeco = Math.Max(0, i); break;
                        case "potenciaventilar": if (ehInt) p.PotenciaVentilar = Math.Max(0, i); break;
                        case "tarifa": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) p.Tarifa = d; break;
                        case "desligarem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.DesligarEmUtc = l; break;
                        case "desligarpuid": p.DesligarPuid = v; break;
                        case "abacasa": if (ehInt) p.AbaCasa = i == 1 ? 1 : 0; break;
                        case "retornoem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.RetornoEmUtc = l; break;
                        case "retornopuid": p.RetornoPuid = v; break;
                        case "retornomodo": if (ehInt) p.RetornoModo = i; break;
                        case "retornotemp": if (ehInt) p.RetornoTemp = Math.Max(16, Math.Min(32, i)); break;
                        case "retornovento": if (ehInt) p.RetornoVento = i; break;
                        case "consumoaraberto": p.ConsumoArAberto = v == "1"; break;
                        case "consumolavadoraaberto": p.ConsumoLavadoraAberto = v == "1"; break;
                        case "climacidade": p.ClimaCidade = v; break;
                        case "climalat": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaLat = d; break;
                        case "climalon": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaLon = d; break;
                        case "climatemp": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) p.ClimaTemp = d; break;
                        case "climacodigo": if (ehInt) p.ClimaCodigo = i; break;
                        case "climadia": p.ClimaDia = v != "0"; break;
                        case "climaem": if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) p.ClimaEmUtc = l; break;
                        case "cena": { CenaCasa cena = CenaCasa.DeLinha(v); if (cena != null) { if (!lidas) { p.Cenas.Clear(); lidas = true; } if (p.Cenas.Count < CenaCasa.MaxCenas) p.Cenas.Add(cena); } break; }
                    }
                }
            }
            catch (Exception ex) { Log.Error("casa: ler casa.ini", ex); }
            return p;
        }

        public void Salvar()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("alertafalha=" + (AlertaFalha ? "1" : "0"));
            sb.AppendLine("alertadesconectado=" + (AlertaDesconectado ? "1" : "0"));
            sb.AppendLine("alertaquente=" + (AlertaQuente ? "1" : "0"));
            sb.AppendLine("alertalavadorafim=" + (AlertaLavadoraFim ? "1" : "0"));
            sb.AppendLine("limitequente=" + LimiteQuente.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciafrio=" + PotenciaFrio.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaeco=" + PotenciaEco.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaseco=" + PotenciaSeco.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("potenciaventilar=" + PotenciaVentilar.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("tarifa=" + Tarifa.ToString("0.####", CultureInfo.InvariantCulture));
            sb.AppendLine("desligarem=" + DesligarEmUtc.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("desligarpuid=" + (DesligarPuid ?? ""));
            sb.AppendLine("abacasa=" + AbaCasa.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornoem=" + RetornoEmUtc.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornopuid=" + (RetornoPuid ?? ""));
            sb.AppendLine("retornomodo=" + RetornoModo.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornotemp=" + RetornoTemp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("retornovento=" + RetornoVento.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("consumoaraberto=" + (ConsumoArAberto ? "1" : "0"));
            sb.AppendLine("consumolavadoraaberto=" + (ConsumoLavadoraAberto ? "1" : "0"));
            sb.AppendLine("climacidade=" + (ClimaCidade ?? ""));
            sb.AppendLine("climalat=" + (double.IsNaN(ClimaLat) ? "" : ClimaLat.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climalon=" + (double.IsNaN(ClimaLon) ? "" : ClimaLon.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climatemp=" + (double.IsNaN(ClimaTemp) ? "" : ClimaTemp.ToString("R", CultureInfo.InvariantCulture)));
            sb.AppendLine("climacodigo=" + ClimaCodigo.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("climadia=" + (ClimaDia ? "1" : "0"));
            sb.AppendLine("climaem=" + ClimaEmUtc.ToString(CultureInfo.InvariantCulture));
            foreach (CenaCasa cena in Cenas) sb.AppendLine("cena=" + cena.Linha());
            try { GravacaoAtomica.Escrever(Arquivo, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("casa: gravar casa.ini", ex); }
        }
    }

    /// <summary>
    /// Alertas no Windows, sem repetir: cada um dispara na passagem para o
    /// estado ruim e so rearma quando o estado volta ao normal.
    /// </summary>
    internal static class Alertas
    {
        private static readonly object _trava = new object();
        private static readonly HashSet<string> _falhasAvisadas = new HashSet<string>();
        private static readonly Dictionary<string, DateTime> _offlineDesde = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> _offlineAvisado = new HashSet<string>();
        private static readonly HashSet<string> _quenteAvisado = new HashSet<string>();
        public static readonly TimeSpan TempoDesconectado = TimeSpan.FromMinutes(5);

        internal static void Zerar()
        {
            lock (_trava) { _falhasAvisadas.Clear(); _offlineDesde.Clear(); _offlineAvisado.Clear(); _quenteAvisado.Clear(); }
        }

        public static void Conferir(Aparelho a, PreferenciasCasa p) { Conferir(a, p, DateTime.Now); }

        internal static void Conferir(Aparelho a, PreferenciasCasa p, DateTime agora)
        {
            List<string[]> avisos = new List<string[]>();
            lock (_trava)
            {
                // falhas do autodiagnostico (f_e_*)
                foreach (KeyValuePair<string, string> kv in a.Estado)
                {
                    if (!kv.Key.StartsWith("f_e_", StringComparison.Ordinal)) continue;
                    string chave = a.Puid + "|" + kv.Key;
                    bool ativa = a.Int(kv.Key, 0) != 0;
                    if (!ativa) { _falhasAvisadas.Remove(chave); continue; }
                    if (_falhasAvisadas.Contains(chave)) continue;
                    _falhasAvisadas.Add(chave);
                    if (!p.AlertaFalha) continue;
                    FalhaOficial f = Diagnostico.Falha(a.Puid, kv.Key);
                    string texto = kv.Key == "f_e_push" ? string.Format(T.CasaAlertaCodigo, a.Int(kv.Key, 0))
                        : T.CasaFalha(kv.Key, null);
                    if (f != null && f.Orientacao.Length > 0) texto += ". " + f.Orientacao;
                    avisos.Add(new[] { T.CasaAlertaFalhaTitulo(a.Nome), texto });
                }

                // desconectado da nuvem por mais de alguns minutos
                if (a.Online) { _offlineDesde.Remove(a.Puid); _offlineAvisado.Remove(a.Puid); }
                else
                {
                    DateTime desde;
                    if (!_offlineDesde.TryGetValue(a.Puid, out desde)) { _offlineDesde[a.Puid] = agora; desde = agora; }
                    if (agora - desde >= TempoDesconectado && !_offlineAvisado.Contains(a.Puid))
                    {
                        _offlineAvisado.Add(a.Puid);
                        if (p.AlertaDesconectado)
                            avisos.Add(new[] { T.CasaAlertaOfflineTitulo(a.Nome), T.CasaAlertaOfflineTexto((int)TempoDesconectado.TotalMinutes) });
                    }
                }

                // ambiente quente com o ar desligado; rearma 1 grau abaixo ou ao ligar
                if (a.Online && a.Tem("f_temp_in"))
                {
                    double amb = a.Num("f_temp_in", 0);
                    bool desligado = a.Int("t_power", 0) == 0;
                    bool fahrenheit = a.Int("t_temp_type", 0) == 1;
                    double limite = fahrenheit ? p.LimiteQuente * 9.0 / 5 + 32 : p.LimiteQuente;
                    if (desligado && amb > limite)
                    {
                        if (!_quenteAvisado.Contains(a.Puid))
                        {
                            _quenteAvisado.Add(a.Puid);
                            if (p.AlertaQuente)
                                avisos.Add(new[] { T.CasaAlertaQuenteTitulo(a.Nome),
                                    T.CasaAlertaQuenteTexto(amb.ToString("0.#") + (fahrenheit ? "°F" : "°C")) });
                        }
                    }
                    else if (!desligado || amb <= limite - 1) _quenteAvisado.Remove(a.Puid);
                }
            }
            foreach (string[] av in avisos) ServicoCasa.AvisoWindows(av[0], av[1]);
        }
    }

    /// <summary>Uma falha da lista oficial do autodiagnostico (basic/self_check/info).</summary>
    internal sealed class FalhaOficial
    {
        public string Chave, ValorDeFalha, Nome, Categoria, Orientacao;
    }

    /// <summary>
    /// A lista oficial de falhas do modelo, consultada uma vez por hora por
    /// aparelho. O estado de cada falha agora vem das f_e_* da leitura comum.
    /// </summary>
    internal static class Diagnostico
    {
        private static readonly object _trava = new object();
        private static readonly Dictionary<string, List<FalhaOficial>> _listas = new Dictionary<string, List<FalhaOficial>>();
        private static readonly Dictionary<string, DateTime> _quando = new Dictionary<string, DateTime>();

        public static List<FalhaOficial> Lista(string puid)
        {
            lock (_trava)
            {
                List<FalhaOficial> l;
                return _listas.TryGetValue(puid, out l) ? new List<FalhaOficial>(l) : null;
            }
        }

        public static FalhaOficial Falha(string puid, string chave)
        {
            List<FalhaOficial> l = Lista(puid);
            if (l != null) foreach (FalhaOficial f in l) if (f.Chave == chave) return f;
            return null;
        }

        public static void Atualizar(ConnectLifeNuvem nuvem, Aparelho a)
        {
            lock (_trava)
            {
                DateTime q;
                if (_quando.TryGetValue(a.Puid, out q) && (DateTime.Now - q).TotalHours < 1) return;
                _quando[a.Puid] = DateTime.Now;
            }
            try
            {
                List<FalhaOficial> l = Interpretar(nuvem.AutoDiagnostico(a.Puid));
                lock (_trava) _listas[a.Puid] = l;
            }
            catch (Exception ex) { Log.Write("ConnectLife: autodiagnostico indisponivel: " + ex.Message); }
        }

        internal static void Definir(string puid, List<FalhaOficial> l) { lock (_trava) _listas[puid] = l; }

        /// <summary>Alguma falha ativa agora (f_e_* acusando, error_code diferente de 0 ou item da lista oficial)?</summary>
        public static bool TemFalha(Aparelho a)
        {
            if (a == null) return false;
            foreach (KeyValuePair<string, string> kv in a.Estado)
                if (kv.Key.StartsWith("f_e_", StringComparison.Ordinal) && a.Int(kv.Key, 0) != 0) return true;
            if (a.Int("error_code", 0) != 0) return true;
            List<FalhaOficial> lista = Lista(a.Puid);
            if (lista != null)
                foreach (FalhaOficial f in lista) if (Ativa(a, f)) return true;
            return false;
        }

        /// <summary>A falha da lista oficial esta acusando: o estado tem o valor de falha.</summary>
        internal static bool Ativa(Aparelho a, FalhaOficial f)
        {
            string v;
            return a.Estado.TryGetValue(f.Chave, out v) && v == f.ValorDeFalha;
        }

        /// <summary>
        /// Lista que e um dicionario de codigos (a da lavadora: todos os itens com
        /// statusKey error_code, cada um com um statusValue): mostra so o codigo atual.
        /// </summary>
        private static bool PorCodigo(List<FalhaOficial> l) { return ChaveDeCodigo(l) != null; }

        /// <summary>A chave que se repete em 3 ou mais itens (error_code na lavadora), ou nulo.</summary>
        private static string ChaveDeCodigo(List<FalhaOficial> l)
        {
            if (l == null) return null;
            Dictionary<string, int> n = new Dictionary<string, int>();
            foreach (FalhaOficial f in l) { int k; n.TryGetValue(f.Chave, out k); n[f.Chave] = k + 1; }
            foreach (KeyValuePair<string, int> kv in n) if (kv.Value >= 3) return kv.Key;
            return null;
        }

        /// <summary>O diagnostico em texto: cada falha da lista oficial e o estado dela agora.</summary>
        public static string Texto(Aparelho a)
        {
            if (a == null) return "";
            List<string> l = new List<string>();
            List<FalhaOficial> lista = Lista(a.Puid);
            if (lista == null || lista.Count == 0)
            {
                // Sem a lista oficial ainda: as f_e_* que o aparelho publica.
                foreach (KeyValuePair<string, string> kv in a.Estado)
                    if (kv.Key.StartsWith("f_e_", StringComparison.Ordinal) && kv.Key != "f_e_push")
                        l.Add((a.Int(kv.Key, 0) != 0 ? "\u25CF  " + T.CasaFalhaAtiva : "\u25CB  " + T.CasaFalhaOk) + "  ·  " + T.CasaFalha(kv.Key, null));                // Lavadora (e quem mais publicar error_code) sem a lista oficial ainda.
                if (a.Tem("error_code"))
                    l.Add(a.Int("error_code", 0) == 0 ? "\u25CB  " + T.CasaSemErro : "\u25CF  " + string.Format(T.CasaErroCodigo, a.Int("error_code", 0)));
            }
            else if (PorCodigo(lista))
            {
                string chave = ChaveDeCodigo(lista);
                int atual = a.Int(chave, 0);
                FalhaOficial achada = null;
                int codigos = 0;
                foreach (FalhaOficial f in lista)
                {
                    if (f.Chave != chave) continue;
                    codigos++;
                    if (Ativa(a, f)) achada = f;
                }
                if (atual == 0) l.Add("\u25CB  " + T.CasaSemErro);
                else
                {
                    l.Add("\u25CF  " + (achada != null ? achada.Nome + (achada.Categoria.Length > 0 ? " (" + achada.Categoria + ")" : "") : string.Format(T.CasaErroCodigo, atual)));
                    if (achada != null && achada.Orientacao.Length > 0) l.Add("      " + achada.Orientacao);
                }
                // Os demais itens (sabao, amaciante) so aparecem quando acusam.
                foreach (FalhaOficial f in lista)
                {
                    if (f.Chave == chave || !Ativa(a, f)) continue;
                    l.Add("\u25CF  " + f.Nome + (f.Orientacao.Length > 0 ? "\n      " + f.Orientacao : ""));
                }
                l.Add(T.CasaCodigosConhecidos(codigos));
            }
            else
                foreach (FalhaOficial f in lista)
                {
                    bool ativa = Ativa(a, f);
                    string s = (ativa ? "\u25CF  " + T.CasaFalhaAtiva : "\u25CB  " + T.CasaFalhaOk) + "  ·  " + f.Nome;
                    if (f.Categoria.Length > 0) s += " (" + f.Categoria + ")";
                    if (ativa && f.Orientacao.Length > 0) s += "\n      " + f.Orientacao;
                    l.Add(s);
                }
            if (a.Int("f_e_push", 0) != 0) l.Add("\u25CF  " + string.Format(T.CasaAlertaCodigo, a.Int("f_e_push", 0)));
            DateTime ult = ServicoCasa.UltimaLeitura;
            l.Add("");
            l.Add(T.CasaUltimaLeitura(ult == DateTime.MinValue ? "--" : ult.ToString("HH:mm:ss")) + "  ·  " +
                (a.Online ? T.CasaOnline : T.CasaOffline));
            if (lista != null && lista.Count > 0) l.Add(T.CasaDiagnosticoFonte);
            return string.Join("\n", l.ToArray());
        }

        internal static List<FalhaOficial> Interpretar(Dictionary<string, object> r)
        {
            List<FalhaOficial> l = new List<FalhaOficial>();
            Dictionary<string, object> d = r != null && r.ContainsKey("data") ? r["data"] as Dictionary<string, object> : null;
            List<object> itens = d != null && d.ContainsKey("selfCheckList") ? d["selfCheckList"] as List<object> : null;
            if (itens == null) return l;
            foreach (object o in itens)
            {
                Dictionary<string, object> i = o as Dictionary<string, object>;
                if (i == null) continue;
                FalhaOficial f = new FalhaOficial();
                f.Chave = Json.Texto(i, "statusKey") ?? "";
                f.ValorDeFalha = Json.Texto(i, "statusValue") ?? "1";
                // Os textos oficiais vem em ingles; os conhecidos sao traduzidos.
                string falha = Json.Texto(i, "fault") ?? "";
                f.Nome = falha.Length > 0 ? T.CasaFalhaOficial(f.Chave, falha) :
                    (f.Chave == "error_code" ? string.Format(T.CasaErroCodigo, f.ValorDeFalha) : T.CasaFalhaOficial(f.Chave, f.Chave));
                f.Categoria = T.CasaCategoria(Json.Texto(i, "content") ?? "");
                f.Orientacao = T.CasaOrientacao(Json.Texto(i, "guideKey") ?? "", Json.Texto(i, "guide") ?? "");
                if (f.Chave.Length > 0) l.Add(f);
            }
            return l;
        }
    }

    /// <summary>Uma leitura guardada do ar.</summary>
    internal struct AmostraAr
    {
        public DateTime Quando;
        public float Ambiente, Alvo;
        public bool Ligado;
        public int Modo;
    }

    /// <summary>
    /// Historico do ar: uma amostra por leitura real (no maximo uma a cada 50 s),
    /// 35 dias, em casa-historico.txt. Sem leitura nao ha amostra: o grafico
    /// mostra a lacuna, nao inventa linha.
    /// </summary>
    internal static class HistoricoDoAr
    {
        private static readonly object _trava = new object();
        private static readonly Dictionary<string, List<AmostraAr>> _series = new Dictionary<string, List<AmostraAr>>();
        public const int Dias = 35;
        /// <summary>Intervalo maximo entre amostras que ainda conta como tempo continuo.</summary>
        public static readonly TimeSpan Continuo = TimeSpan.FromMinutes(3);

        internal static string Arquivo
        {
            get { return _arquivo ?? Path.Combine(Paths.DataDir, "casa-historico.txt"); }
            set { _arquivo = value; }
        }
        private static string _arquivo;

        internal static void Limpar() { lock (_trava) _series.Clear(); }

        public static void Carregar()
        {
            lock (_trava)
            {
                _series.Clear();
                try
                {
                    if (!File.Exists(Arquivo)) return;
                    DateTime corte = DateTime.Now.AddDays(-Dias);
                    int descartadas = 0;
                    foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
                    {
                        string[] c = linha.Split(';');
                        if (c.Length < 6) continue;
                        long s; float amb, alvo; int modo;
                        if (!long.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out s)) continue;
                        DateTime q = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime();
                        if (q < corte) { descartadas++; continue; }
                        float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amb);
                        float.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out alvo);
                        int.TryParse(c[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out modo);
                        AmostraAr a = new AmostraAr();
                        a.Quando = q; a.Ambiente = amb; a.Alvo = alvo; a.Ligado = c[4] == "1"; a.Modo = modo;
                        Serie(c[0]).Add(a);
                    }
                    if (descartadas > 0) Regravar();
                }
                catch (Exception ex) { Log.Error("casa: ler historico", ex); }
            }
        }

        private static List<AmostraAr> Serie(string puid)
        {
            List<AmostraAr> l;
            if (!_series.TryGetValue(puid, out l)) { l = new List<AmostraAr>(); _series[puid] = l; }
            return l;
        }

        /// <summary>Registra a leitura, se o aparelho esta conectado e passou tempo desde a ultima.</summary>
        public static void Registrar(Aparelho a, DateTime agora)
        {
            if (!a.Online || !a.Tem("t_power")) return;
            AmostraAr s = new AmostraAr();
            s.Quando = agora;
            s.Ambiente = a.Tem("f_temp_in") ? (float)a.Num("f_temp_in", 0) : float.NaN;
            s.Alvo = a.Tem("t_temp") ? (float)a.Num("t_temp", 0) : float.NaN;
            s.Ligado = a.Int("t_power", 0) != 0;
            s.Modo = a.Int("t_work_mode", -1);
            lock (_trava)
            {
                List<AmostraAr> l = Serie(a.Puid);
                if (l.Count > 0 && (agora - l[l.Count - 1].Quando).TotalSeconds < 50) return;
                l.Add(s);
                try
                {
                    File.AppendAllText(Arquivo, Linha(a.Puid, s), new UTF8Encoding(false));
                }
                catch (Exception ex) { Log.Error("casa: gravar historico", ex); }
            }
        }

        internal static void Acrescentar(string puid, AmostraAr s) { lock (_trava) Serie(puid).Add(s); }

        private static string Linha(string puid, AmostraAr s)
        {
            long seg = (long)(s.Quando.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return puid + ";" + seg.ToString(CultureInfo.InvariantCulture) + ";" +
                s.Ambiente.ToString("0.##", CultureInfo.InvariantCulture) + ";" +
                s.Alvo.ToString("0.##", CultureInfo.InvariantCulture) + ";" + (s.Ligado ? "1" : "0") + ";" +
                s.Modo.ToString(CultureInfo.InvariantCulture) + "\n";
        }

        private static void Regravar()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, List<AmostraAr>> kv in _series)
                foreach (AmostraAr s in kv.Value) sb.Append(Linha(kv.Key, s));
            try { GravacaoAtomica.Escrever(Arquivo, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Error("casa: regravar historico", ex); }
        }

        public static List<AmostraAr> Amostras(string puid, DateTime desde)
        {
            lock (_trava)
            {
                List<AmostraAr> r = new List<AmostraAr>();
                List<AmostraAr> l;
                if (_series.TryGetValue(puid, out l))
                    foreach (AmostraAr s in l) if (s.Quando >= desde) r.Add(s);
                return r;
            }
        }

        /// <summary>
        /// Tempo ligado e energia ESTIMADA por dia, entre duas datas. Cada amostra
        /// vale ate a seguinte, limitado a 3 min: lacuna maior e tempo sem leitura,
        /// e nao entra na conta.
        /// </summary>
        public static SortedDictionary<DateTime, double[]> PorDia(string puid, DateTime de, DateTime ate, PreferenciasCasa p, string tipo)
        {
            SortedDictionary<DateTime, double[]> d = new SortedDictionary<DateTime, double[]>();
            for (DateTime dia = de.Date; dia <= ate.Date; dia = dia.AddDays(1)) d[dia] = new double[2];   // [horas, kWh]
            List<AmostraAr> l = Amostras(puid, de.Date);
            for (int i = 0; i < l.Count; i++)
            {
                AmostraAr s = l[i];
                if (!s.Ligado || s.Quando > ate) continue;
                DateTime fim = i + 1 < l.Count ? l[i + 1].Quando : s.Quando.Add(TimeSpan.FromMinutes(1));
                if (fim - s.Quando > Continuo) fim = s.Quando + Continuo;
                if (fim > ate) fim = ate;
                // Divide no meio da noite, para o trecho cair no dia certo.
                DateTime ini = s.Quando;
                while (ini < fim)
                {
                    DateTime corte = ini.Date.AddDays(1) < fim ? ini.Date.AddDays(1) : fim;
                    double h = (corte - ini).TotalHours;
                    double[] v;
                    if (d.TryGetValue(ini.Date, out v))
                    {
                        v[0] += h;
                        v[1] += h * p.Potencia(s.Modo, tipo) / 1000.0;
                    }
                    ini = corte;
                }
            }
            return d;
        }
    }
}
