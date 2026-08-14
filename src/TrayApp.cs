using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Aplicativo de bandeja.
    ///
    /// A leitura de sensores roda em thread propria: percorrer o hardware
    /// (MSR, SMART, NVMe) leva dezenas a centenas de milissegundos, e fazer
    /// isso na thread de UI travaria a bomba de mensagens a cada ciclo.
    /// </summary>
    public class TrayContext : ApplicationContext, ISettingsData
    {
        // Hardware continua em 1 Hz. FPS e frametime usam um caminho proprio
        // a 4 Hz: fluido no LCD sem fazer o numero oscilar depressa demais.
        private const int FullPeriodMs = 1000;
        internal const int FastPeriodMs = 250;
        private const int ShutdownTimeoutMs = 3000;

        private enum RuntimeState { Starting, Running, Stopping, Stopped }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr handle, int command);

        private const int SwRestore = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private NotifyIcon _icon;
        private Icon _iconNormal;
        private Control _marshal;
        private Thread _sensorInit;
        private Thread _worker;
        private Thread _activationWatcher;
        private readonly EventWaitHandle _activationSignal;
        private ManualResetEvent _stop = new ManualResetEvent(false);
        private int _runtimeState = (int)RuntimeState.Starting;

        private IPanelDevice _panel;
        private PanelCycle _panelCycle;
        private PanelKeepalive _panelKeepalive;
        private ISensorService _sensors;
        private IFastSensorService _fastSensors;
        private SensorCycle _sensorCycle;
        private readonly object _sensorLock = new object();

        private Config _cfg;

        /// <summary>
        /// Copia do perfil ativo, para a thread de atualizacao.
        ///
        /// Ela NAO pode ler _cfg: Config.Active percorre a lista de perfis com
        /// foreach, e a janela de configuracao adiciona e remove dessa mesma
        /// lista. Criar ou excluir perfil no momento errado lancava
        /// InvalidOperationException na thread de fundo - capturada, mas o ciclo
        /// se perdia. Pior, SaveToProfile escrevia nos campos que a thread
        /// estava lendo, e o mostrador podia exibir um perfil meio aplicado.
        ///
        /// Quem edita publica um clone novo; a thread so le a referencia, o que
        /// e atomico. Ninguem escreve no objeto que ela esta lendo.
        /// </summary>
        private volatile Profile _live;

        /// <summary>Ociosidade que apaga o mostrador, em milissegundos; 0 desliga.</summary>
        private volatile int _idleBlankMs = 0;

        /// <summary>Estado corrente do apagamento, so para nao repetir o log.</summary>
        private bool _apagado = false;

        /// <summary>
        /// Perfis do rodizio, ja clonados. Null quando nao ha rodizio.
        ///
        /// Publicado inteiro de uma vez, como _live: a thread le a referencia
        /// e passa a trabalhar no vetor novo, e o antigo morre sozinho. Trocar
        /// item a item exigiria travar os dois lados.
        /// </summary>
        private volatile Profile[] _rotation;

        /// <summary>Periodo do rodizio em milissegundos; 0 desliga.</summary>
        private volatile int _rotateMs = 0;

        // so a thread de atualizacao toca nestes dois
        private int _rotIndex = 0;
        private long _rotTicks = 0;

        private volatile bool _paused = false;
        private volatile bool _snapshotWanted = false;
        private Dictionary<string, float> _snapshot = new Dictionary<string, float>();

        private List<SensorEntry> _cache = new List<SensorEntry>();
        private MenuItem _miPause, _miAutostart, _miProfiles;
        private bool _sensorsOk = false;
        private bool _openWhenReady = false;
        private SettingsForm _settingsForm;
        private readonly WindowOpenGate _settingsGate = new WindowOpenGate();

        public TrayContext() : this(new Sensors(), new HidPanel(), null) { }

        /// <summary>
        /// Ponto de composicao da aplicacao. Mantido interno para a producao
        /// continuar abrindo apenas as fontes reais, e disponivel aos testes para
        /// injetar sensores e painel sem hardware fisico.
        /// </summary>
        internal TrayContext(ISensorService sensors, IPanelDevice panel)
            : this(sensors, panel, null) { }

        internal TrayContext(ISensorService sensors, IPanelDevice panel,
                             EventWaitHandle activationSignal)
        {
            if (sensors == null) throw new ArgumentNullException("sensors");
            if (panel == null) throw new ArgumentNullException("panel");
            _sensors = sensors;
            _fastSensors = sensors as IFastSensorService;
            _panel = panel;
            _activationSignal = activationSignal;
            _sensorCycle = new SensorCycle(sensors);
            _panelCycle = new PanelCycle(panel);
            _panelKeepalive = new PanelKeepalive(panel, OnPanelConnectionChanged);
            Log.Write("=== Mhiagos Control iniciando ===");
            _cfg = Config.Load();
            _sensors.ShowAll = _cfg.ShowAllSensors;

            // antes de qualquer texto de tela: o menu da bandeja e montado logo
            // abaixo e a tela de carregamento pode aparecer a qualquer momento
            T.Language = string.IsNullOrEmpty(_cfg.Language) ? T.Detect() : _cfg.Language;

            _marshal = new Control();
            GC.KeepAlive(_marshal.Handle);   // cria o handle na thread de UI

            Autostart.RemoveLegacyTask();

            _iconNormal = Assets.TrayIcon;

            _icon = new NotifyIcon();
            _icon.Icon = _iconNormal;
            _icon.Text = T.TrayStarting;
            _icon.Visible = true;
            _icon.Click += new EventHandler(OnIconClick);
            _icon.DoubleClick += new EventHandler(OnConfig);
            BuildMenu();

            IniciarVigiaDeAtivacao();

            // A abertura das fontes NAO acontece aqui. Ela e agendada para
            // depois que Application.Run assumir: veja AbrirSensores.
            _marshal.BeginInvoke(new MethodInvoker(AbrirSensores));
        }

        /// <summary>
        /// A segunda execucao nao abre interface propria. Ela acende este evento
        /// nomeado e termina; a instancia que possui a bandeja recebe o sinal e
        /// executa exatamente a mesma acao do duplo clique no icone.
        /// </summary>
        private void IniciarVigiaDeAtivacao()
        {
            if (_activationSignal == null) return;

            _activationWatcher = new Thread(new ThreadStart(delegate
            {
                WaitHandle[] sinais = { _activationSignal, _stop };
                while (!IsStopping)
                {
                    int qual;
                    try { qual = WaitHandle.WaitAny(sinais); }
                    catch (ObjectDisposedException) { return; }
                    if (qual != 0 || IsStopping) return;
                    Marshal(delegate { PedirConfiguracao(); });
                }
            }));
            _activationWatcher.IsBackground = true;
            _activationWatcher.Name = "InstanceActivation";
            _activationWatcher.Start();
        }

        /// <summary>
        /// Abre as fontes de sensores sem bloquear a thread de interface.
        ///
        /// Antes isso rodava dentro do construtor, com Application.DoEvents num
        /// laco de espera. Bombear na mao nunca devolve a thread ao estado
        /// ocioso: passados alguns segundos o Windows considera a janela travada,
        /// troca o cursor pelo de ocupado e passa a engolir os cliques - a tela
        /// de carregamento nao fechava porque ninguem recebia o clique.
        ///
        /// Agora o laco de mensagens de verdade ja esta rodando quando este
        /// metodo comeca; o trabalho pesado vai para outra thread e o retorno
        /// volta para a de interface por BeginInvoke.
        /// </summary>
        private void AbrirSensores()
        {
            ConferirMotor();

            // qualificado: System.Threading tambem tem um Timer
            _demora = new System.Windows.Forms.Timer();
            _demora.Interval = 3000;
            _demora.Tick += delegate
            {
                _demora.Stop();
                StatusInicial(T.LoadingDriver);
            };
            _demora.Start();

            _sensorInit = new Thread(delegate()
            {
                Exception falha = null;
                try
                {
                    lock (_sensorLock)
                    {
                        _sensors.Open();
                        _cache = _sensors.List();
                    }
                }
                catch (Exception ex) { falha = ex; }

                if (IsStopping) return;
                try { _marshal.BeginInvoke(new SensoresProntosHandler(SensoresProntos), falha); }
                catch (Exception ex) { Log.Error("retorno da inicializacao", ex); }
            });
            _sensorInit.IsBackground = true;
            _sensorInit.Name = "SensorInit";
            _sensorInit.Start();
        }

        /// <summary>
        /// Oferece adotar a biblioteca do HWiNFO da instalacao de fabrica.
        ///
        /// Acontece ANTES de abrir as fontes, e nao depois de falhar: assim a
        /// copia entra a tempo de ser usada nesta mesma execucao, sem reabrir
        /// nada nem pedir reinicio.
        ///
        /// So pergunta quando ha o que oferecer. Se a biblioteca nao existe em
        /// lugar nenhum, calar aqui e proposital - o aviso fica na aba Sobre,
        /// onde informa sem cobrar nada a cada arranque.
        /// </summary>
        private void ConferirMotor()
        {
            try
            {
                if (HwInfo.Installed) return;
                string origem = HwInfo.OriginalInstallCopy();
                if (origem == null) return;

                DialogResult r = MessageBox.Show(
                    T.AdoptEngineQuestion(origem, HwInfo.EnginePath),
                    T.AdoptEngineTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { Log.Write("copia da biblioteca recusada pelo usuario"); return; }

                string erro;
                if (!HwInfo.Adotar(origem, out erro))
                    MessageBox.Show(T.AdoptFailed(erro), T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Log.Error("conferencia do motor de sensores", ex); }
        }

        private delegate void SensoresProntosHandler(Exception falha);
        private System.Windows.Forms.Timer _demora;

        /// <summary>Continuacao do arranque, ja de volta na thread de interface.</summary>
        private void SensoresProntos(Exception falha)
        {
            // A thread de abertura pode terminar depois de o usuario mandar sair.
            // Nesse caso ela nao pode iniciar o worker nem reconstruir a bandeja
            // que o encerramento ja desmontou.
            if (IsStopping) return;

            if (_demora != null) { _demora.Stop(); _demora.Dispose(); _demora = null; }
            FecharSplash();

            if (falha == null)
            {
                _sensorsOk = true;
                Log.Write("sensores abertos: " + _cache.Count + " disponiveis");
            }
            else
            {
                Log.Error("inicializacao dos sensores", falha);
                MessageBox.Show(T.SensorInitFailed(falha.Message, Log.Path),
                    T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Recuperacoes e migracoes podem usar nomes portaveis. Resolve-los
            // somente depois de as fontes abrirem evita gravar um ID da fonte
            // reserva numa maquina que normalmente usa HWiNFO.
            if (_sensorsOk && SensorSemantics.ResolveConfiguration(_cfg, _cache))
            {
                string erro;
                if (!_cfg.Save(out erro)) Log.Write("sensores recuperados nao persistiram: " + erro);
            }

            // Antes de qualquer caminho que possa abrir a janela de
            // configuracao: e la que a grade de metricas se monta e passa a
            // acompanhar leituras.
            MetricHistory.Carregar();
            MetricHistory.Seguir(IdsAcompanhados());

            Profile act = _cfg.Active;
            if (_sensorsOk && (string.IsNullOrEmpty(act.Panel1Id) || string.IsNullOrEmpty(act.Panel2Id)))
            {
                PickDefaults(act);
                _openWhenReady = true;
            }

            WarnAboutOriginalTask();

            Microsoft.Win32.SystemEvents.SessionEnding += new Microsoft.Win32.SessionEndingEventHandler(OnSessionEnding);

            IniciarVigiaDeJogo();

            Publicar();   // antes de a thread comecar, senao o primeiro ciclo nao tem perfil
            _panelKeepalive.Start();
            _worker = new Thread(new ThreadStart(WorkerLoop));
            _worker.IsBackground = true;
            _worker.Name = "PanelUpdate";
            _worker.Start();
            Interlocked.CompareExchange(ref _runtimeState,
                (int)RuntimeState.Running, (int)RuntimeState.Starting);

            if (_openWhenReady)
            {
                _openWhenReady = false;
                _marshal.BeginInvoke(new MethodInvoker(PedirConfiguracao));
            }
        }

        /// <summary>
        /// Clique no icone. Durante a inicializacao ele mostra o andamento -
        /// e o momento em que o usuario se pergunta se o programa abriu. Fora
        /// dela nao faz nada: quem abre a configuracao e o duplo clique.
        /// </summary>
        private void OnIconClick(object sender, EventArgs e)
        {
            MouseEventArgs m = e as MouseEventArgs;
            if (m != null && m.Button != MouseButtons.Left) return;   // direito e o menu
            if (IsStarting) MostrarSplash();
        }

        private bool IsStarting { get { return Volatile.Read(ref _runtimeState) == (int)RuntimeState.Starting; } }
        private bool IsStopping { get { return Volatile.Read(ref _runtimeState) >= (int)RuntimeState.Stopping; } }
        private string _statusInicial = null;   // T.OpeningSources, ja com idioma resolvido

        private void StatusInicial(string texto)
        {
            _statusInicial = texto;
            Splash.SetStatus(texto);
        }

        /// <summary>
        /// Mostra a tela de carregamento a pedido. Fecha-la nao cancela nada -
        /// a inicializacao roda noutra thread e nem sabe que ela existe.
        /// </summary>
        private void MostrarSplash()
        {
            Splash.Show(_statusInicial ?? T.OpeningSources);
        }

        private void FecharSplash()
        {
            Splash.Close();
        }

        // ---------------- menu ----------------

        private void BuildMenu()
        {
            ContextMenu menu = new ContextMenu();

            _miProfiles = new MenuItem(T.TrayProfiles);
            menu.MenuItems.Add(_miProfiles);
            RebuildProfileMenu();

            menu.MenuItems.Add(new MenuItem(T.TrayConfigure, new EventHandler(OnConfig)));
            menu.MenuItems.Add("-");

            _miPause = new MenuItem(T.TrayPause, new EventHandler(OnPause));
            menu.MenuItems.Add(_miPause);

            _miAutostart = new MenuItem(T.TrayAutostart, new EventHandler(OnToggleAutostart));
            _miAutostart.Checked = Autostart.IsEnabled();
            menu.MenuItems.Add(_miAutostart);

            menu.MenuItems.Add(new MenuItem(T.TrayOpenData, new EventHandler(OnOpenData)));
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(new MenuItem(T.TrayExit, new EventHandler(OnExit)));

            _icon.ContextMenu = menu;
        }

        private void RebuildProfileMenu()
        {
            _miProfiles.MenuItems.Clear();
            string padrao = _cfg.DefaultProfile.Name;
            foreach (Profile p in _cfg.Profiles)
            {
                string texto = p.Name + (string.Equals(p.Name, padrao, StringComparison.Ordinal)
                    ? "  ·  " + T.DefaultBadge : "");
                MenuItem mi = new MenuItem(texto, new EventHandler(OnPickProfile));
                mi.Tag = p;
                mi.Checked = (p.Name == _cfg.ActiveName);
                _miProfiles.MenuItems.Add(mi);
            }
        }

        private void OnPickProfile(object sender, EventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;
            Profile p = mi.Tag as Profile;
            if (p == null) return;

            string erro;
            bool salvo = ProfileActivation.TryActivate(_cfg, p.Name, out erro);
            if (!salvo)
            {
                RebuildProfileMenu();
                MessageBox.Show(T.SaveFailed(erro), T.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Publicar();
            RebuildProfileMenu();
            Log.Write("perfil ativo: " + p.Name);
        }

        // ---------------- perfil por jogo ----------------

        private System.Windows.Forms.Timer _relogioDoJogo;
        private string _jogoVisto;            // o executavel do ciclo anterior
        private bool _perfilDeJogoAplicado;

        /// <summary>
        /// Vigia qual jogo esta em primeiro plano e troca o perfil conforme o
        /// mapa.
        ///
        /// Um segundo e de sobra: abrir um jogo leva dezenas deles, e um
        /// intervalo mais curto so gastaria ciclos comparando duas strings
        /// iguais. Na THREAD DA INTERFACE de proposito - trocar de perfil mexe no
        /// menu da bandeja e na configuracao em disco, e faze-lo a partir da
        /// thread do mostrador seria corrida com a janela aberta.
        /// </summary>
        private void IniciarVigiaDeJogo()
        {
            _relogioDoJogo = new System.Windows.Forms.Timer();
            _relogioDoJogo.Interval = 1000;
            _relogioDoJogo.Tick += new EventHandler(OnVigiaDeJogo);
            _relogioDoJogo.Start();
        }

        private void OnVigiaDeJogo(object sender, EventArgs e)
        {
            try
            {
                string agora = _cfg.GameProfiles ? Rtss.JogoAtual : null;
                if (string.Equals(agora, _jogoVisto, StringComparison.OrdinalIgnoreCase))
                {
                    // Se a gravação do retorno falhou, tenta novamente enquanto
                    // continuarmos fora de um jogo vinculado.
                    string vinculo = !string.IsNullOrEmpty(agora)
                        ? _cfg.PerfilDoJogo(agora) : null;
                    if (_perfilDeJogoAplicado && string.IsNullOrEmpty(vinculo))
                        VoltarAoPadrao("retentativa apos jogo");
                    return;
                }

                string antes = _jogoVisto;
                _jogoVisto = agora;

                if (!string.IsNullOrEmpty(agora))
                {
                    string alvo = _cfg.PerfilDoJogo(agora);
                    if (string.IsNullOrEmpty(alvo) || !_cfg.NameExists(alvo))
                    {
                        if (_perfilDeJogoAplicado)
                            VoltarAoPadrao("jogo sem vinculo " + agora);
                        return;
                    }

                    if (alvo == _cfg.ActiveName) _perfilDeJogoAplicado = true;
                    else if (TrocarPorJogo(alvo, "jogo " + agora))
                        _perfilDeJogoAplicado = true;
                    return;
                }

                // O jogo fechou: o destino é explícito e estável, nunca o perfil
                // incidental que estava ativo antes de o jogo abrir.
                if (_perfilDeJogoAplicado)
                    VoltarAoPadrao("fim de " + (antes ?? "jogo"));
            }
            catch (Exception ex) { Log.Error("vigia de perfil por jogo", ex); }
        }

        private void VoltarAoPadrao(string motivo)
        {
            string padrao = _cfg.DefaultProfile.Name;
            bool concluido = string.Equals(padrao, _cfg.ActiveName, StringComparison.Ordinal) ||
                             TrocarPorJogo(padrao, motivo);
            if (concluido) _perfilDeJogoAplicado = false;
        }

        private bool TrocarPorJogo(string nome, string motivo)
        {
            string erro;
            if (!ProfileActivation.TryActivate(_cfg, nome, out erro))
            {
                Log.Write("perfil por jogo nao persistiu: " + erro);
                return false;
            }
            Publicar();
            RebuildProfileMenu();

            // Registrado SEMPRE. O aplicativo trocou o que esta na peca sem
            // ninguem pedir; quem for procurar por que o mostrador mudou tem de
            // achar a resposta escrita.
            Log.Write("perfil por jogo: " + nome + " (" + motivo + ")");
            return true;
        }

        /// <summary>
        /// Entrega a thread de atualizacao uma copia do perfil ativo.
        ///
        /// Chamar sempre que a configuracao mudar. E o unico ponto em que a
        /// thread de fundo passa a enxergar edicoes - o que tambem quer dizer
        /// que esquecer de chamar aqui deixa o mostrador desatualizado, e nao
        /// corrompido.
        /// </summary>
        private void Publicar()
        {
            try
            {
                _live = _cfg.Active.Clone();
                _idleBlankMs = Math.Max(0, _cfg.IdleBlankMinutes) * 60000;
                _rotateMs = Math.Max(0, _cfg.RotateSeconds) * 1000;

                List<Profile> roda = _cfg.Rotation;
                if (roda.Count < 2 || _rotateMs <= 0) _rotation = null;
                else
                {
                    Profile[] copia = new Profile[roda.Count];
                    for (int i = 0; i < roda.Count; i++) copia[i] = roda[i].Clone();
                    _rotation = copia;
                }

                AjustarFoco();
            }
            catch (Exception ex) { Log.Error("publicacao do perfil ativo", ex); }
        }

        /// <summary>
        /// Diz a camada de sensores de quais grupos o mostrador depende.
        ///
        /// Medido nesta maquina: o ciclo com os 19 grupos custa 54 ms e o
        /// mesmo ciclo com os 2 grupos que vao ao mostrador custa 3,6 ms,
        /// porque o caro na biblioteca do HWiNFO e preparar cada grupo, e nao
        /// ler dele. O conjunto e o perfil ativo mais todos os do rodizio.
        ///
        /// Com a janela de configuracao aberta o foco sai: ali o seletor
        /// precisa da lista inteira, e um ciclo de 54 ms enquanto alguem mexe
        /// nos controles nao incomoda ninguem.
        /// </summary>
        private void AjustarFoco()
        {
            try
            {
                lock (_sensorLock) { _sensors.Focar(Foco(false)); }
            }
            catch (Exception ex) { Log.Error("foco da leitura de sensores", ex); }
        }

        /// <summary>
        /// Conjunto de identificadores que a leitura precisa cobrir.
        ///
        /// Nulo quer dizer "leia tudo", que e o caso com a janela aberta.
        ///
        /// Os cartoes da aba Metricas so entram quando o historico vai tirar uma
        /// amostra. Deixa-los no foco o tempo todo seria voltar ao ciclo
        /// completo para sempre - a grade padrao toca em processador, video,
        /// memoria, placa-mae e disco, ou seja, quase todos os grupos - e o
        /// atalho existe justamente porque o caro na biblioteca e preparar cada
        /// grupo. Uma vez a cada balde a leitura se abre, tira a amostra e volta
        /// a fechar.
        /// </summary>
        /// <summary>
        /// Tudo que precisa de historico: os cartoes da aba Metricas MAIS os
        /// blocos da tela de bordo.
        ///
        /// Os segundos nao sao escolhidos por ninguem - saem de uma regra fixa -
        /// e por isso era facil esquece-los aqui. Foi o que aconteceu: os blocos
        /// abriam sem curva, porque a serie deles nunca chegou a ser gravada.
        /// A gravacao acontece no ciclo do mostrador, com a janela fechada, entao
        /// nao adianta a janela saber quais sao; quem tem de saber e isto.
        /// </summary>
        private List<string> IdsAcompanhados()
        {
            List<string> ids = new List<string>(_cfg.MetricIds);

            foreach (SensorEntry s in MetricPicker.Destaques(_cache))
                if (s != null && !string.IsNullOrEmpty(s.Id) && !ids.Contains(s.Id))
                    ids.Add(s.Id);

            // E as duas leituras que vao para a peca: a tela de bordo desenha a
            // curva delas ao lado do mostrador, e sem historico as duas maiores
            // areas de grafico da tela ficariam em branco.
            Profile a = _cfg.Active;
            if (a != null)
                foreach (string id in new string[] { a.Panel1Id, a.Panel2Id })
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);

            return ids;
        }

        private List<string> Foco(bool comMetricas)
        {
            if (_janelaAberta) return null;

            List<string> ids = new List<string>();
            Profile a = _live;
            if (a != null) { ids.Add(a.Panel1Id); ids.Add(a.Panel2Id); }

            Profile[] roda = _rotation;
            if (roda != null)
                foreach (Profile p in roda) { ids.Add(p.Panel1Id); ids.Add(p.Panel2Id); }

            if (comMetricas) ids.AddRange(MetricHistory.Seguidos);
            return ids;
        }

        private volatile bool _janelaAberta = false;

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            bool target = !_miAutostart.Checked;
            bool ok = target ? Autostart.Enable() : Autostart.Disable();
            if (ok) _miAutostart.Checked = target;
            else MessageBox.Show(T.AutostartFailed + Log.Path,
                    T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>A tarefa do software de fabrica disputaria o painel a cada logon.</summary>
        private void WarnAboutOriginalTask()
        {
            try
            {
                if (!Autostart.OriginalAppTaskEnabled()) return;
                DialogResult r = MessageBox.Show(T.OriginalTaskWarning,
                    T.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) Autostart.DisableOriginalAppTask();
            }
            catch (Exception ex) { Log.Error("verificacao da tarefa original", ex); }
        }

        private void PickDefaults(Profile p)
        {
            foreach (SensorEntry s in _cache)
                if (s.Type == SensorType.Temperature && s.Label.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
                { p.Panel1Id = s.Id; break; }

            foreach (SensorEntry s in _cache)
                if (s.Type == SensorType.Load && s.Label.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                { p.Panel2Id = s.Id; break; }
        }

        // ---------------- thread de atualizacao ----------------

        private void WorkerLoop()
        {
            Stopwatch clock = Stopwatch.StartNew();
            long nextFull = 0;
            long nextFast = 0;
            while (!_stop.WaitOne(0))
            {
                long now = clock.ElapsedMilliseconds;
                bool fast = PrecisaDoCaminhoRapido();
                bool full = now >= nextFull;
                if (!_paused)
                {
                    try
                    {
                        if (full)
                        {
                            UpdateOnce(true);
                            nextFull = ProximoInstante(nextFull, clock.ElapsedMilliseconds, FullPeriodMs);
                            nextFast = clock.ElapsedMilliseconds + FastPeriodMs;
                        }
                        else if (fast && now >= nextFast)
                        {
                            UpdateOnce(false);
                            nextFast = ProximoInstante(nextFast, clock.ElapsedMilliseconds, FastPeriodMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ciclo de atualizacao", ex);
                        SetTooltip("Mhiagos Control - erro: " + ex.Message);
                    }
                }
                else
                {
                    if (full) nextFull = ProximoInstante(nextFull, now, FullPeriodMs);
                    nextFast = now + FastPeriodMs;
                }

                now = clock.ElapsedMilliseconds;
                long target = fast ? Math.Min(nextFull, nextFast) : nextFull;
                long wait = target - now;
                if (_stop.WaitOne((int)Math.Max(1, wait))) break;
            }
            Log.Write("thread de atualizacao encerrada");
        }

        internal static long ProximoInstante(long previous, long now, int period)
        {
            long next = previous + period;
            if (next <= now) next += ((now - next) / period + 1) * period;
            return next;
        }

        internal static bool PerfilUsaRtss(Profile profile)
        {
            return profile != null &&
                (EhRtssVivo(profile.Panel1Id) || EhRtssVivo(profile.Panel2Id));
        }

        private static bool EhRtssVivo(string id)
        {
            return string.Equals(id, Rtss.Prefixo + "fps", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(id, Rtss.Prefixo + "frametime", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EhRtss(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                   id.StartsWith(Rtss.Prefixo, StringComparison.OrdinalIgnoreCase);
        }

        private bool PrecisaDoCaminhoRapido()
        {
            if (_fastSensors == null) return false;
            // Com a janela aberta, cartões e prévias também recebem RTSS vivo,
            // mesmo que o perfil atualmente enviado ao LCD use só hardware.
            if (_snapshotWanted) return true;
            if (PerfilUsaRtss(_live)) return true;
            Profile[] rotation = _rotation;
            if (rotation != null)
                foreach (Profile profile in rotation)
                    if (PerfilUsaRtss(profile)) return true;
            return false;
        }

        private void UpdateOnce(bool full)
        {
            Profile vigiar = _live;
            if (vigiar == null) return;   // ainda nao publicado

            // O que vai ao mostrador pode não ser o perfil ativo: o rodízio
            // gira o quadro, enquanto a configuração ativa continua intacta.
            Profile cfg = Rodar(vigiar);
            bool mesmo = ReferenceEquals(cfg, vigiar) || cfg.Name == vigiar.Name;

            MonitorReadings readings;
            lock (_sensorLock)
            {
                if (full)
                {
                    // Ciclo de amostra: a leitura se abre para os grupos dos cartoes
                    // antes do Refresh, senao os sensores fora do foco viriam com o
                    // valor do ultimo ciclo em que foram lidos - historico de mentira.
                    bool amostrar = MetricHistory.HoraDeAmostrar();
                    if (amostrar) _sensors.Focar(Foco(true));

                    readings = _sensorCycle.Refresh(cfg);
                    if (_snapshotWanted)
                        Interlocked.Exchange(ref _snapshot, _sensors.Snapshot());

                    if (amostrar)
                    {
                        MetricHistory.Amostrar(Ler);
                        _sensors.Focar(Foco(false));
                    }
                }
                else
                {
                    _fastSensors.RefreshFast();
                    readings = _sensorCycle.Read(cfg);
                    if (_snapshotWanted) PublicarSnapshotRapido();
                }
            }

            if (full) MetricHistory.SalvarSeVencido();   // fora do lock: escreve em disco

            bool ocioso = Ocioso();
            PanelDispatch envio = _panelCycle.Prepare(cfg, readings.Display1, readings.Display2, ocioso);
            _panelKeepalive.Publish(envio.Frame);
            PanelValue v1 = envio.Panel1;
            PanelValue v2 = envio.Panel2;

            bool ok = _panelKeepalive.LastSent;

            if (!full) return;

            string text = string.Format(CultureInfo.InvariantCulture, "Mhiagos Control  {0}{1} / {2}{3}",
                v1.Value.HasValue ? v1.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Fahrenheit ? "F" : "C",
                v2.Value.HasValue ? v2.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Percent ? "%" : "W");
            if (!mesmo) text += "  " + cfg.Name;
            if (ocioso) text += T.TagIdle;
            if (v1.Clamped || v2.Clamped) text += T.TagOver;
            if (!ok) text += T.TagNoDevice;
            SetTooltip(text);
        }

        /// <summary>Mescla somente FPS/frametime no instantâneo da janela.</summary>
        private void PublicarSnapshotRapido()
        {
            Dictionary<string, float> next = new Dictionary<string, float>(GetSnapshot());
            List<string> remove = new List<string>();
            foreach (string id in next.Keys)
                if (EhRtss(id)) remove.Add(id);
            foreach (string id in remove) next.Remove(id);
            foreach (KeyValuePair<string, float> value in _fastSensors.FastSnapshot())
                next[value.Key] = value.Value;
            Interlocked.Exchange(ref _snapshot, next);
        }

        private void OnPanelConnectionChanged(bool connected)
        {
            Log.Write(connected ? "painel conectado" : "painel ausente ou envio falhou");
        }

        /// <summary>
        /// Le um sensor para o historico. Chamada ja sob _sensorLock.
        ///
        /// Um identificador que sumiu - placa trocada, disco removido, sensor
        /// que a fonte parou de publicar - devolve nulo, e o balde daquele
        /// instante fica marcado como falha. Nao e erro, e ausencia.
        /// </summary>
        private float? Ler(string id)
        {
            SensorEntry e = _sensors.ReadEntry(id);
            return e != null ? e.Value : null;
        }

        /// <summary>
        /// Perfil que vai ao mostrador neste ciclo.
        ///
        /// Quando o rodizio esta ligado ele avanca sozinho pela roda; quando
        /// nao esta, devolve o ativo e zera o indice, para que ligar o rodizio
        /// mais tarde comece do inicio em vez de saltar para o meio.
        ///
        /// Nao usa o relogio de parede para contar: DateTime.UtcNow anda com o
        /// horario de verao e com a sincronizacao de rede, e um ajuste de meia
        /// hora congelaria o mostrador nesse perfil ate a hora passar.
        /// </summary>
        private Profile Rodar(Profile ativo)
        {
            Profile[] roda = _rotation;
            int periodo = _rotateMs;
            if (roda == null || roda.Length < 2 || periodo <= 0)
            {
                _rotIndex = 0;
                _rotTicks = 0;
                return ativo;
            }

            long agora = Stopwatch.GetTimestamp();
            if (_rotTicks == 0) _rotTicks = agora;

            long periodoTicks = (long)periodo * Stopwatch.Frequency / 1000;
            _rotIndex = IndiceDoRodizio(agora - _rotTicks, periodoTicks, roda.Length);
            return roda[_rotIndex];
        }

        /// <summary>
        /// Posicao na roda a partir do tempo decorrido.
        ///
        /// Derivar do relogio em vez de somar um a cada volta e o que impede a
        /// deriva: o ciclo completo dura 1 s mais o que a varredura do hardware levar,
        /// e um contador incrementado "quando der" acumularia esse resto ate o
        /// rodizio de 20 s virar de 23. Aqui cada instante tem uma posicao so,
        /// e perder um ciclo nao desalinha nada.
        /// </summary>
        internal static int IndiceDoRodizio(long decorrido, long periodo, int tamanho)
        {
            if (tamanho <= 0) return 0;
            if (periodo <= 0 || decorrido < 0) return 0;
            return (int)((decorrido / periodo) % tamanho);
        }

        /// <summary>
        /// Ha quanto tempo ninguem toca no teclado nem no mouse.
        ///
        /// GetLastInputInfo mede a sessao inteira, e nao so este processo -
        /// e o que se quer: o computador estar em uso, e nao esta janela.
        /// Vale notar o que ele NAO ve: assistir video ou esperar uma
        /// renderizacao demorada conta como ocioso, porque ninguem digita.
        /// </summary>
        private bool Ocioso()
        {
            int limite = _idleBlankMs;
            if (limite <= 0) { Registrar(false); return false; }

            bool ocioso;
            try
            {
                LASTINPUTINFO li = new LASTINPUTINFO();
                // qualificado: esta classe tem um metodo Marshal proprio
                li.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref li)) { Registrar(false); return false; }

                // Aritmetica sem sinal de proposito: GetTickCount da a volta a
                // cada 49,7 dias, e a subtracao em uint continua certa na volta.
                uint parado = (uint)Environment.TickCount - li.dwTime;
                ocioso = parado >= (uint)limite;
            }
            catch (Exception ex) { Log.Error("leitura da ociosidade", ex); return false; }

            Registrar(ocioso);
            return ocioso;
        }

        private void Registrar(bool ocioso)
        {
            if (ocioso == _apagado) return;
            _apagado = ocioso;
            Log.Write(ocioso ? "mostrador apagado por ociosidade" : "mostrador religado");
        }

        private void SetTooltip(string text)
        {
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            Marshal(delegate { try { _icon.Text = text; } catch { } });
        }

        /// <summary>NotifyIcon so pode ser tocado na thread de UI.</summary>
        private void Marshal(MethodInvoker action)
        {
            try
            {
                if (_marshal != null && _marshal.IsHandleCreated && !_marshal.IsDisposed)
                    _marshal.BeginInvoke(action);
            }
            catch (Exception ex) { Log.Error("marshaling para a UI", ex); }
        }

        // ---------------- acoes ----------------

        private void OnConfig(object sender, EventArgs e)
        {
            PedirConfiguracao();
        }

        private void PedirConfiguracao()
        {
            if (IsStopping) return;

            // A lista so existe depois de a thread de inicializacao publicar as
            // fontes. Abrir antes montava uma janela incompleta e concorria com
            // o primeiro Refresh. O pedido fica pendente e abre a janela assim
            // que as fontes terminarem, em vez de se perder na tela de carga.
            if (IsStarting)
            {
                _openWhenReady = true;
                MostrarSplash();
                return;
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                TrazerParaFrente(_settingsForm);
                return;
            }

            // ShowDialog cria um laco de mensagens aninhado. Sem esta guarda,
            // o duplo clique seguinte reentra neste metodo antes de o primeiro
            // voltar e cria outra janela modal por cima da anterior.
            if (!_settingsGate.TryEnter())
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                    TrazerParaFrente(_settingsForm);
                return;
            }

            try
            {
                // Antes de qualquer leitura: com a janela aberta o ciclo volta a
                // ser completo, senao o seletor listaria so os sensores que ja
                // estao no mostrador.
                _janelaAberta = true;
                AjustarFoco();

                List<SensorEntry> list;
                lock (_sensorLock)
                {
                    _sensors.Refresh();
                    list = _sensors.List();
                    Interlocked.Exchange(ref _snapshot, _sensors.Snapshot());
                }
                _cache = list;
                _snapshotWanted = true;

                // A troca de idioma volta como Retry: a janela nao se
                // reetiqueta viva, ela e reconstruida. Relistar entre as duas
                // aberturas tambem traduz os nomes gerados, como as medias.
                DialogResult r;
                do
                {
                    using (SettingsForm f = new SettingsForm(_cfg, _cache, this))
                    {
                        _settingsForm = f;
                        // Aplicar dentro da janela ja vale no mostrador: sem
                        // isto o perfil so mudaria ao fechar, o que faria o
                        // botao parecer sem efeito.
                        f.Applied += delegate { Publicar(); };
                        r = f.ShowDialog();
                        _settingsForm = null;
                    }
                    if (r == DialogResult.Retry) _cache = ReListSensors();
                }
                while (r == DialogResult.Retry);

                _snapshotWanted = false;
                _janelaAberta = false;   // antes de Publicar, que reaplica o foco

                if (r != DialogResult.OK) _cfg = Config.Load();   // descarta edicoes
                MetricHistory.Seguir(IdsAcompanhados());
                Publicar();
                RebuildProfileMenu();
            }
            catch (Exception ex)
            {
                _snapshotWanted = false;
                _janelaAberta = false;
                AjustarFoco();
                Log.Error("janela de configuracao", ex);
                MessageBox.Show(ex.Message, T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _settingsForm = null;
                _settingsGate.Exit();
            }
        }

        private static void TrazerParaFrente(Form form)
        {
            if (form == null || form.IsDisposed) return;
            try
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                ShowWindowAsync(form.Handle, SwRestore);
                form.BringToFront();
                form.Activate();
                SetForegroundWindow(form.Handle);
            }
            catch (Exception ex) { Log.Error("trazer configuracao para frente", ex); }
        }

        private Dictionary<string, float> GetSnapshot()
        {
            return Interlocked.CompareExchange(ref _snapshot, null, null);
        }

        Dictionary<string, float> ISettingsData.CurrentSnapshot() { return GetSnapshot(); }

        /// <summary>Remonta a lista de sensores sob o lock, para a janela de configuracao.</summary>
        private List<SensorEntry> ReListSensors()
        {
            lock (_sensorLock)
            {
                _cache = _sensors.List();
                return _cache;
            }
        }

        List<SensorEntry> ISettingsData.RefreshSensorList() { return ReListSensors(); }

        void ISettingsData.SetShowAllSensors(bool showAll)
        {
            lock (_sensorLock) _sensors.ShowAll = showAll;
        }

        private void OnPause(object sender, EventArgs e)
        {
            _paused = !_paused;
            _panelKeepalive.Enabled = !_paused;
            _miPause.Text = _paused ? T.TrayResume : T.TrayPause;
            Log.Write(_paused ? "pausado" : "retomado");
            if (_paused) _icon.Text = T.TrayPaused;
        }

        private void OnOpenData(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", Paths.DataDir); }
            catch (Exception ex) { Log.Error("abrir pasta de dados", ex); }
        }

        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            Log.Write("sessao do Windows encerrando");
            Shutdown();
        }

        private void OnExit(object sender, EventArgs e)
        {
            Shutdown();
            Application.Exit();
        }

        private void Shutdown()
        {
            int previous = Interlocked.Exchange(ref _runtimeState, (int)RuntimeState.Stopping);
            if (previous >= (int)RuntimeState.Stopping) return;

            try { Microsoft.Win32.SystemEvents.SessionEnding -= new Microsoft.Win32.SessionEndingEventHandler(OnSessionEnding); } catch { }

            if (_relogioDoJogo != null)
            {
                _relogioDoJogo.Stop();
                _relogioDoJogo.Tick -= new EventHandler(OnVigiaDeJogo);
                _relogioDoJogo.Dispose();
                _relogioDoJogo = null;
            }
            if (_demora != null) { _demora.Stop(); _demora.Dispose(); _demora = null; }

            _stop.Set();
            _panelKeepalive.Stop();
            Stopwatch budget = Stopwatch.StartNew();
            bool workerParou = EsperarThread(_worker, "atualizacao", budget);
            bool painelParou = EsperarKeepalive(budget);
            bool aberturaParou = EsperarThread(_sensorInit, "abertura de sensores", budget);
            bool ativacaoParou = EsperarThread(_activationWatcher, "ativacao", budget);

            // Depois de a thread parar: gravar com ela viva copiaria uma serie
            // no meio de um balde sendo fechado. Pelo mesmo motivo, se uma
            // thread resistir ao timeout, nao fechamos painel ou sensores sob os
            // pes dela: o processo esta saindo e o Windows libera os handles.
            bool podeDescartarRecursos = workerParou && painelParou && aberturaParou && ativacaoParou;
            try { MetricHistory.Salvar(); } catch (Exception ex) { Log.Error("gravacao do historico", ex); }
            if (!podeDescartarRecursos)
                Log.Write("encerramento: recursos de hardware ficarao para o termino do processo");

            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            if (_iconNormal != null) _iconNormal.Dispose();

            if (podeDescartarRecursos)
            {
                try { _panel.Close(); } catch (Exception ex) { Log.Error("fechamento do painel", ex); }
                lock (_sensorLock)
                {
                    try { _sensors.Dispose(); } catch (Exception ex) { Log.Error("fechamento dos sensores", ex); }
                }
            }
            _panelKeepalive.Dispose();
            if (_marshal != null) _marshal.Dispose();
            if (podeDescartarRecursos) _stop.Dispose();

            Volatile.Write(ref _runtimeState, (int)RuntimeState.Stopped);
            Log.Write("=== encerrado ===");
        }

        /// <summary>
        /// Espera uma thread cooperativa sem permitir que um driver lento vire
        /// descarte concorrente de handles. Threads do aplicativo sao de fundo;
        /// no raro timeout, o termino do processo faz a limpeza final.
        /// </summary>
        private static bool EsperarThread(Thread thread, string nome, Stopwatch budget)
        {
            if (thread == null || !thread.IsAlive) return true;
            int remaining = ShutdownTimeoutMs - (int)budget.ElapsedMilliseconds;
            if (remaining > 0 && thread.Join(remaining)) return true;
            Log.Write("thread de " + nome + " nao encerrou a tempo");
            return false;
        }

        private bool EsperarKeepalive(Stopwatch budget)
        {
            int remaining = ShutdownTimeoutMs - (int)budget.ElapsedMilliseconds;
            if (_panelKeepalive.Wait(Math.Max(0, remaining))) return true;
            Log.Write("thread de keepalive nao encerrou a tempo");
            return false;
        }
    }

    public static class Program
    {
        private const string MutexName = "Local\\MhiagosControl_SingleInstance";
        private const string ActivationName = "Local\\MhiagosControl_Activate";

        private static bool TemArgumento(string alvo)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 1; i < args.Length; i++)
                    if (string.Equals(args[i].Trim(), alvo, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        [STAThread]
        public static void Main()
        {
            // Duas instancias disputariam o painel e o resultado piscaria.
            // O idioma do Windows vale ate a configuracao ser lida: a checagem
            // de instancia unica acontece antes disso e ja fala com o usuario.
            T.Language = T.Detect();

            // Modo de servico: ajusta o RTSS e sai, sem bandeja e sem tocar no
            // mostrador. Existe para poder ser encadeado depois do winget, na
            // mesma linha de comando - so assim o ajuste acontece com o RTSS ja
            // instalado, que e a unica hora em que ele faz sentido. Antes do
            // mutex de instancia unica de proposito: e uma tarefa curta, e ela
            // nao disputa o painel com a instancia que ja estiver rodando.
            if (TemArgumento(Rtss.ArgConfigurar))
            {
                string erro;
                bool ok = Rtss.ConfigurarInicio(out erro);
                if (!ok)
                    MessageBox.Show(T.RtssConfigFailed(erro), T.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool createdNew;
            using (EventWaitHandle activation = new EventWaitHandle(
                false, EventResetMode.AutoReset, ActivationName))
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // A instancia existente traz sua unica janela para frente.
                    // Esta segunda execucao nao cria janela nem icone proprios.
                    try { activation.Set(); }
                    catch (Exception ex) { Log.Error("sinalizar instancia existente", ex); }
                    return;
                }

                // precisa vir antes de qualquer janela: define o modo escuro do
                // processo, do qual dependem as barras de rolagem nativas
                Theme.InitProcess();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try { Application.Run(new TrayContext(new Sensors(), new HidPanel(), activation)); }
                catch (Exception ex)
                {
                    Log.Error("falha fatal", ex);
                    MessageBox.Show("Erro inesperado:\n\n" + ex.Message + "\n\nDetalhes em:\n" + Log.Path,
                        "Mhiagos Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
