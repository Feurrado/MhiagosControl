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
        /// <summary>
        /// Ultima leitura feita direto na nuvem. Com o servidor como fonte o login do
        /// PC ficaria sem uso; uma leitura direta a cada HorasEntreDiretas mantem a
        /// reserva valendo (o refresh token gira e vence se ninguem renova).
        /// </summary>
        private static DateTime _ultimaDireta = DateTime.MinValue;
        internal const int HorasEntreDiretas = 6;
        private static bool _forcarLeitura;
        private static string _erro;

        /// <summary>
        /// Primeira recusa de login de uma sequencia. A conta so e esquecida se a
        /// recusa se repete uma leitura depois (com 1 min ou mais entre as duas):
        /// apagar na primeira derrubava o login por um soluco da nuvem.
        /// </summary>
        private static DateTime _primeiraRecusa = DateTime.MinValue;
        internal const int SegundosEntreRecusas = 60;

        /// <summary>O cofre ja avisou que nao grava; o aviso do Windows sai uma vez por execucao.</summary>
        private static bool _avisouCofre;

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
            ServidorCasa.Iniciar();
        }

        private static void Usar(ConnectLifeNuvem nuvem)
        {
            lock (_trava) _nuvem = nuvem;
            if (nuvem == null) return;
            nuvem.TokenMudou += delegate
            {
                if (CofreConnectLife.Gravar(nuvem.RefreshToken, nuvem.SourceId)) return;
                // Sem isso o token novo so existe na memoria: o proximo arranque
                // usaria o velho. O erro fica no estado da pagina e sai um aviso.
                lock (_trava) _erro = T.CasaAcessoNaoGravado;
                bool avisar;
                lock (_trava) { avisar = !_avisouCofre; _avisouCofre = true; }
                if (avisar) AvisoWindows(T.CasaAvisoTitulo, T.CasaAcessoNaoGravado);
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
                if (_ciclo == null) _ciclo = new Timer(delegate { Protegido("leitura agendada", Ler); }, null, 2000, periodo);
                else _ciclo.Change(periodo, periodo);
            }
        }

        /// <summary>
        /// Corpo de timer e de tarefa do pool. Uma excecao solta numa thread
        /// dessas termina o processo, e com ele o mostrador do cooler.
        /// </summary>
        /// <summary>Tarefa no pool, protegida como os timers.</summary>
        internal static void NoFundo(string contexto, Action acao)
        {
            ThreadPool.QueueUserWorkItem(delegate { Protegido(contexto, acao); });
        }

        internal static void Protegido(string contexto, Action acao)
        {
            try { acao(); }
            catch (Exception ex) { Log.Error("casa: " + contexto, ex); }
        }

        /// <summary>
        /// Recusa de login: so na segunda seguida, com pelo menos
        /// SegundosEntreRecusas entre as duas, a conta e esquecida.
        /// </summary>
        internal static bool EsquecerConta(ref DateTime primeira, DateTime agoraUtc)
        {
            if (primeira == DateTime.MinValue) { primeira = agoraUtc; return false; }
            if ((agoraUtc - primeira).TotalSeconds < SegundosEntreRecusas) return false;
            primeira = DateTime.MinValue;
            return true;
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
            NoFundo("login", delegate
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
                    lock (_trava) { _erro = null; _primeiraRecusa = DateTime.MinValue; }
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
                _primeiraRecusa = DateTime.MinValue;
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
            NoFundo("leitura", delegate { LerAgora(); });
        }

        private static void LerAgora()
        {
            ConnectLifeNuvem nuvem;
            lock (_trava) { nuvem = _nuvem; _buscando = true; }
            if (nuvem == null) { lock (_trava) _buscando = false; return; }
            try
            {
                List<Aparelho> lista = ListarPelaFonte(nuvem);
                ComMeta(nuvem, lista);
                lock (_trava)
                {
                    if (nuvem != _nuvem) return;   // saiu da conta no meio
                    _aparelhos = lista;
                    _ultimaLeitura = DateTime.Now;
                    _erro = null;
                    _primeiraRecusa = DateTime.MinValue;
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
                bool esqueceu = false;
                lock (_trava)
                {
                    if (nuvem != _nuvem) return;   // saiu da conta no meio
                    if (ex.PedeLogin && EsquecerConta(ref _primeiraRecusa, DateTime.UtcNow))
                    {
                        CofreConnectLife.Apagar();
                        _nuvem = null;
                        _aparelhos = new List<Aparelho>();
                        esqueceu = true;
                    }
                    _erro = ex.Message;
                }
                if (esqueceu) { Log.Write("ConnectLife: acesso recusado duas vezes, conta esquecida"); Reprogramar(); }
                else if (ex.PedeLogin) Log.Write("ConnectLife: acesso recusado, nova tentativa na proxima leitura");
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

        /// <summary>
        /// A lista pelo servidor da casa, quando ele e a fonte; senao (ou se ele
        /// falhar) direto da nuvem, como sempre foi.
        /// </summary>
        private static List<Aparelho> ListarPelaFonte(ConnectLifeNuvem nuvem)
        {
            bool forcar, diretaVencida;
            lock (_trava)
            {
                forcar = _forcarLeitura; _forcarLeitura = false;
                diretaVencida = (DateTime.UtcNow - _ultimaDireta).TotalHours >= HorasEntreDiretas;
            }
            if (!diretaVencida && ServidorCasa.Fonte)
            {
                try { return ServidorCasa.Aparelhos(forcar); }
                catch (Exception ex) { ServidorCasa.FonteFalhou(ex); }
            }
            List<Aparelho> lista = nuvem.ListarAparelhos();
            lock (_trava) _ultimaDireta = DateTime.UtcNow;
            return lista;
        }

        /// <summary>Um comando pelo servidor da casa, quando ele e a fonte. Falso: mande direto.</summary>
        private static bool DefinirPeloServidor(string puid, Dictionary<string, string> props, bool semBipe)
        {
            if (!ServidorCasa.Fonte) return false;
            try { ServidorCasa.Comando(puid, props, semBipe); return true; }
            catch (Exception ex) { ServidorCasa.FonteFalhou(ex); return false; }
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
            NoFundo("comando", delegate
            {
                Exception erro = null;
                try
                {
                    // Pelo servidor vai tudo num pedido so: ele divide em partes e cuida do bipe.
                    List<Dictionary<string, string>> partes = DefinirPeloServidor(puid, props, semBipe)
                        ? new List<Dictionary<string, string>>() : Partes(props);
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
            NoFundo("atualizar", delegate
            {
                bool pediu = false;
                foreach (Aparelho a in lista)
                {
                    if (!a.ArCondicionado || !a.Online || !Grava(a, "t_device_info")) continue;
                    try
                    {
                        Dictionary<string, string> p = new Dictionary<string, string>();
                        p["t_device_info"] = "1";
                        if (!DefinirPeloServidor(a.Puid, p, false)) nuvem.Definir(a.Puid, p);
                        pediu = true;
                    }
                    catch (Exception ex) { Log.Write("ConnectLife: t_device_info recusado: " + ex.Message); }
                }
                if (pediu) Thread.Sleep(3000);
                lock (_trava) _forcarLeitura = true;   // pelo servidor: pede a ele uma leitura nova
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
            EntregarAoServidor(true);
        }

        /// <summary>
        /// Repassa o timer de desligar (ou o retorno da cena) ao servidor da casa,
        /// para disparar com o PC desligado. O timer local continua armado: ao
        /// vencer, pergunta ao servidor o que ele fez (ServidorCasa.EstadoDoAgendamento).
        /// </summary>
        private static void EntregarAoServidor(bool desligar)
        {
            if (!ServidorCasa.Configurado) return;
            long alvo;
            string puid;
            Dictionary<string, string> props = new Dictionary<string, string>();
            lock (_trava)
            {
                alvo = desligar ? Pref.DesligarEmUtc : Pref.RetornoEmUtc;
                puid = desligar ? Pref.DesligarPuid : Pref.RetornoPuid;
            }
            if (alvo == 0) return;
            if (desligar) props["t_power"] = "0";
            else
            {
                Dictionary<string, int> cmd = PadraoDeRetorno(Pref).Comando(Achar(puid));
                if (cmd == null) return;
                foreach (KeyValuePair<string, int> kv in cmd) props[kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
            }
            NoFundo("entregar ao servidor", delegate
            {
                long id = ServidorCasa.Agendar(puid, new DateTime(alvo, DateTimeKind.Utc), props, desligar ? "desligar" : "retorno");
                if (id == 0) return;
                bool vale;
                lock (_trava)
                {
                    // Cancelado ou trocado enquanto o pedido ia: o do servidor nao vale mais.
                    vale = (desligar ? Pref.DesligarEmUtc : Pref.RetornoEmUtc) == alvo;
                    if (vale)
                    {
                        if (desligar) Pref.ServidorDesligarId = id; else Pref.ServidorRetornoId = id;
                    }
                }
                if (vale) Pref.Salvar(); else ServidorCasa.Cancelar(id);
            });
        }

        public static void CancelarTimer()
        {
            lock (_trava)
            {
                if (Pref.DesligarEmUtc == 0) return;
                Pref.DesligarEmUtc = 0;
                Pref.DesligarPuid = "";
                ServidorCasa.Cancelar(Pref.ServidorDesligarId);
                Pref.ServidorDesligarId = 0;
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
            ArmarDesligarEm(Math.Max(1000, (long)falta.TotalMilliseconds));
        }

        private static void ArmarDesligarEm(long ms)
        {
            lock (_trava)
            {
                if (_timerDesligar != null) _timerDesligar.Dispose();
                _timerDesligar = new Timer(delegate { Protegido("timer de desligar", ConferirTimer); }, null, ms, Timeout.Infinite);
            }
        }

        // ---------------- disparo dos agendamentos ----------------

        /// <summary>Atraso maximo para ainda mandar: o mesmo dos 15 min do arranque e do servidor.</summary>
        internal const int LimiteDeAtrasoMin = 15;

        /// <summary>Com o servidor ainda tentando, o PC espera ate este atraso antes de assumir.</summary>
        internal const int EsperaPeloServidorMin = 2;

        internal enum Decisao { Esperar, ServidorFez, Cancelado, PcManda, Descartar }

        /// <summary>
        /// O que fazer com um agendamento vencido, dado o atraso e o que o
        /// servidor diz dele (nulo: sem servidor). Atrasado demais nao vai: o
        /// PC suspenso a noite toda nao pode desligar ou mexer no ar de manha,
        /// horas depois de quem pediu - a mesma regra do arranque.
        /// </summary>
        internal static Decisao DecidirAgendamento(long alvoTicks, DateTime agoraUtc, string noServidor)
        {
            double atrasoMin = (agoraUtc.Ticks - alvoTicks) / (double)TimeSpan.TicksPerMinute;
            if (noServidor == ServidorCasa.Feito) return Decisao.ServidorFez;
            if (noServidor == ServidorCasa.Cancelado) return Decisao.Cancelado;
            if (noServidor == ServidorCasa.Pendente && atrasoMin <= EsperaPeloServidorMin) return Decisao.Esperar;
            return atrasoMin > LimiteDeAtrasoMin ? Decisao.Descartar : Decisao.PcManda;
        }

        /// <summary>
        /// Assume um agendamento vencido: so um chamador (o relogio ou a leitura)
        /// passa, e o do servidor sai de cena se ele nao o cumpriu.
        /// </summary>
        private static bool Assumir(bool desligar, long alvo, long noServidor, Decisao d)
        {
            lock (_trava)
            {
                if ((desligar ? Pref.DesligarEmUtc : Pref.RetornoEmUtc) != alvo) return false;
                if (desligar)
                {
                    Pref.DesligarEmUtc = 0; Pref.DesligarPuid = ""; Pref.ServidorDesligarId = 0;
                    if (_timerDesligar != null) { _timerDesligar.Dispose(); _timerDesligar = null; }
                }
                else
                {
                    Pref.RetornoEmUtc = 0; Pref.RetornoPuid = ""; Pref.ServidorRetornoId = 0;
                    if (_timerRetorno != null) { _timerRetorno.Dispose(); _timerRetorno = null; }
                }
            }
            Pref.Salvar();
            Avisar();
            // Pendente ou falho la, quem decide agora e o PC: o do servidor nao pode disparar depois.
            if (noServidor > 0 && d != Decisao.ServidorFez && d != Decisao.Cancelado) ServidorCasa.Cancelar(noServidor);
            return true;
        }

        private static void ConferirTimer()
        {
            long alvo, noServidor;
            string puid;
            lock (_trava) { alvo = Pref.DesligarEmUtc; puid = Pref.DesligarPuid; noServidor = Pref.ServidorDesligarId; }
            if (alvo == 0 || DateTime.UtcNow.Ticks < alvo || !Logado) return;
            Decisao d = DecidirAgendamento(alvo, DateTime.UtcNow, noServidor > 0 ? ServidorCasa.EstadoDoAgendamento(noServidor) : null);
            if (d == Decisao.Esperar) { ArmarDesligarEm(30000); return; }
            if (!Assumir(true, alvo, noServidor, d)) return;

            Aparelho a = Achar(puid);
            string nome = a != null ? a.Nome : T.CasaAr;
            switch (d)
            {
                case Decisao.ServidorFez: AvisoWindows(T.CasaTimerTitulo(nome), T.CasaTimerDesligou); return;
                case Decisao.Cancelado: return;
                case Decisao.Descartar:
                    Log.Write("casa: timer de desligar vencido ha mais de " + LimiteDeAtrasoMin + " min, descartado");
                    AvisoWindows(T.CasaTimerTitulo(nome), T.CasaAgendamentoAtrasado);
                    return;
            }
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
            EntregarAoServidor(false);
        }

        public static void CancelarRetorno()
        {
            lock (_trava)
            {
                if (Pref.RetornoEmUtc == 0) return;
                Pref.RetornoEmUtc = 0;
                Pref.RetornoPuid = "";
                ServidorCasa.Cancelar(Pref.ServidorRetornoId);
                Pref.ServidorRetornoId = 0;
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
            ArmarRetornoEm(Math.Max(1000, (long)falta.TotalMilliseconds));
        }

        private static void ArmarRetornoEm(long ms)
        {
            lock (_trava)
            {
                if (_timerRetorno != null) _timerRetorno.Dispose();
                _timerRetorno = new Timer(delegate { Protegido("retorno da cena", ConferirRetorno); }, null, ms, Timeout.Infinite);
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
            long alvo, noServidor;
            string puid;
            lock (_trava) { alvo = Pref.RetornoEmUtc; puid = Pref.RetornoPuid; noServidor = Pref.ServidorRetornoId; }
            if (alvo == 0 || DateTime.UtcNow.Ticks < alvo || !Logado) return;
            Decisao d = DecidirAgendamento(alvo, DateTime.UtcNow, noServidor > 0 ? ServidorCasa.EstadoDoAgendamento(noServidor) : null);
            if (d == Decisao.Esperar) { ArmarRetornoEm(30000); return; }
            if (!Assumir(false, alvo, noServidor, d)) return;

            Aparelho a = Achar(puid);
            string nome = a != null ? a.Nome : T.CasaAr;
            switch (d)
            {
                case Decisao.ServidorFez: AvisoWindows(T.CasaRetornoTitulo(nome), T.CasaRetornoFeito); return;
                case Decisao.Cancelado: return;
                case Decisao.Descartar:
                    Log.Write("casa: retorno vencido ha mais de " + LimiteDeAtrasoMin + " min, descartado");
                    AvisoWindows(T.CasaRetornoTitulo(nome), T.CasaAgendamentoAtrasado);
                    return;
            }
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

        // ---------------- vindo do servidor da casa ----------------

        /// <summary>
        /// O servidor tem um timer de desligar (ou retorno de cena) que o PC nao
        /// conhece: foi criado pelo painel web. O PC passa a mostrar e a vigiar.
        /// Mesmo horario (diferenca de segundos) e o que o proprio PC entregou.
        /// </summary>
        internal static void AdotarDoServidor(bool desligar, string puid, DateTime quandoUtc, long id)
        {
            lock (_trava)
            {
                long local = desligar ? Pref.DesligarEmUtc : Pref.RetornoEmUtc;
                long ticks = Math.Abs(local - quandoUtc.Ticks) < TimeSpan.TicksPerSecond * 2 ? local : quandoUtc.Ticks;
                if (desligar) { Pref.DesligarEmUtc = ticks; Pref.DesligarPuid = puid; Pref.ServidorDesligarId = id; }
                else { Pref.RetornoEmUtc = ticks; Pref.RetornoPuid = puid; Pref.ServidorRetornoId = id; }
            }
            Pref.Salvar();
            if (desligar) ArmarTimerDesligar(false); else ArmarRetorno(false);
            Avisar();
        }

        /// <summary>O agendamento sumiu do servidor antes da hora: cancelado pelo painel web.</summary>
        internal static void SoltarDoServidor(bool desligar)
        {
            lock (_trava)
            {
                if (desligar) Pref.ServidorDesligarId = 0; else Pref.ServidorRetornoId = 0;
            }
            if (desligar) CancelarTimer(); else CancelarRetorno();
        }

        /// <summary>Preferencias trocadas pelo servidor: quem mostra redesenha.</summary>
        internal static void AvisarMudanca() { Avisar(); }

        internal static void AvisoWindows(string titulo, string texto)
        {
            Log.Write("casa: aviso: " + titulo + " - " + texto);
            Action<string, string> n = Notificar;
            if (n != null)
                try { n(titulo, texto); }
                catch (Exception ex) { Log.Error("casa: notificacao", ex); }
        }
    }
}
