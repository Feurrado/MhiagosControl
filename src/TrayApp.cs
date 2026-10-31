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
    public partial class TrayContext : ApplicationContext, ISettingsData, IPanelStatusData, IReadingStatusData, IControleDoWidget
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

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

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
        private ReadingSnapshot _snapshot = ReadingSnapshot.Empty;

        private List<SensorEntry> _cache = new List<SensorEntry>();
        private ToolStripMenuItem _miPause, _miAutostart, _miProfiles, _miWidget;
        private bool _sensorsOk = false;
        private bool _openWhenReady = false;
        private SettingsForm _settingsForm;
        private readonly WindowOpenGate _settingsGate = new WindowOpenGate();

        /// <summary>Pagina que a proxima abertura da configuracao deve mostrar; nulo e a de sempre.</summary>
        private string _paginaPedida;

        private ConfigDoWidget _cfgWidget = new ConfigDoWidget();
        private WidgetDeBateria _widget;

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
            Motion.PrenderNaInterface();
            try { Motion.Diagnostico = System.IO.File.Exists(System.IO.Path.Combine(Paths.DataDir, "animacao-debug")); } catch { }
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

            _iconNormal = Assets.TrayIcon;

            _icon = new NotifyIcon();
            _icon.Icon = _iconNormal;
            _icon.Text = T.TrayStarting;
            _icon.Visible = true;
            _icon.Click += new EventHandler(OnIconClick);
            _icon.DoubleClick += new EventHandler(OnConfig);
            _icon.BalloonTipClicked += delegate { ExecutarAcaoDoBalao(); };
            _icon.BalloonTipClosed += delegate { _acaoDoBalao = null; };
            _cfgWidget = ConfigDoWidget.Carregar();
            // o widget da casa separado (2.29.0 de teste) virou parte do de bateria
            if (CasaNoWidget.Migrar(_cfgWidget)) _cfgWidget.Salvar();
            CasaNoWidget.Controle = this;
            BuildMenu();

            IniciarVigiaDeAtivacao();
            Perifericos.Iniciar(_stop);

            // A abertura das fontes NAO acontece aqui. Ela e agendada para
            // depois que Application.Run assumir: veja AbrirSensores.
            _marshal.BeginInvoke(new MethodInvoker(AbrirSensores));

            // O widget nao depende dos sensores: a bateria tem leitores proprios
            if (_cfgWidget.Ativo) _marshal.BeginInvoke(new MethodInvoker(MostrarWidget));
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
                // O instalador acende o segundo evento para fechar o app do
                // jeito normal. Antes ele matava o processo, e o historico de
                // metricas ainda nao gravado - ate cinco minutos - sumia a cada
                // atualizacao, deixando um buraco na curva.
                EventWaitHandle saida = null;
                try { saida = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitName); }
                catch (Exception ex) { Log.Error("evento de encerramento", ex); }
                WaitHandle[] sinais = saida != null
                    ? new WaitHandle[] { _activationSignal, _stop, saida }
                    : new WaitHandle[] { _activationSignal, _stop };
                try
                {
                    while (!IsStopping)
                    {
                        int qual;
                        try { qual = WaitHandle.WaitAny(sinais); }
                        catch (ObjectDisposedException) { return; }
                        if (IsStopping) return;
                        if (qual == 2)
                        {
                            Log.Write("encerramento pedido pelo instalador");
                            Marshal(delegate { Sair(false); });
                            return;
                        }
                        if (qual != 0) return;
                        Marshal(delegate { PedirConfiguracao(); });
                    }
                }
                finally { if (saida != null) saida.Dispose(); }
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
                    NotificarComAcao(T.AppName, T.AdoptFailed(erro), ConferirMotor);
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
            else Log.Error("inicializacao dos sensores", falha);

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

            // Aberto pelo executavel (e nao pelo logon do Windows): mostra a janela
            if (_activationSignal != null && !Program.IniciadoComOWindows()) _openWhenReady = true;

            Microsoft.Win32.SystemEvents.SessionEnded += new Microsoft.Win32.SessionEndedEventHandler(OnSessionEnded);

            IniciarVigiaDeJogo();

            Publicar();   // antes de a thread comecar, senao o primeiro ciclo nao tem perfil
            _panelKeepalive.Start();
            _worker = new Thread(new ThreadStart(WorkerLoop));
            _worker.IsBackground = true;
            _worker.Name = "PanelUpdate";
            _worker.Start();
            Interlocked.CompareExchange(ref _runtimeState,
                (int)RuntimeState.Running, (int)RuntimeState.Starting);

            // Os avisos vem DEPOIS de o mostrador andar. Antes eles abriam no
            // meio do arranque e o worker so comecava quando alguem respondia:
            // no logon automatico o dialogo podia ficar atras de outras janelas
            // e o painel, apagado, esperando por ele.
            _marshal.BeginInvoke(new SensoresProntosHandler(AvisosDoArranque), falha);

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
    }

    public static class Program
    {
        private const string MutexName = "Local\\MhiagosControl_SingleInstance";
        private const string ActivationName = "Local\\MhiagosControl_Activate";

        /// <summary>Evento que o instalador acende para fechar o app sem matar o processo.</summary>
        public const string ExitName = "Local\\MhiagosControl_Exit";

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
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);

        /// <summary>
        /// O app subiu junto com o Windows (tarefa de logon) ou a pessoa o abriu? Quem abre pelo
        /// executavel espera ver a janela; no logon ela ficaria na frente de tudo sem ninguem
        /// ter pedido. A tarefa agendada nao deixa marca, entao vale o relogio: dentro de dois
        /// minutos do inicio do Explorer desta sessao, e logon.
        /// </summary>
        internal static bool IniciadoComOWindows()
        {
            try
            {
                Process eu = Process.GetCurrentProcess();
                DateTime logon = DateTime.MaxValue;
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    try { if (p.SessionId == eu.SessionId && p.StartTime < logon) logon = p.StartTime; }
                    catch { }
                }
                return logon != DateTime.MaxValue && (eu.StartTime - logon).TotalSeconds < 120;
            }
            catch { return false; }
        }

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
                    // quem acabou de ser aberto pela pessoa tem o primeiro plano: passa a permissao adiante
                    try { AllowSetForegroundWindow(-1); } catch { }
                    try { activation.Set(); }
                    catch (Exception ex) { Log.Error("sinalizar instancia existente", ex); }
                    return;
                }

                // precisa vir antes de qualquer janela: define o modo escuro do
                // processo, do qual dependem as barras de rolagem nativas
                TaskbarIdentity.Initialize();
                Theme.InitProcess();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Excecao sem tratamento: na thread da interface, registra e segue (o app nao
                // cai por um clique); em thread de fundo o .NET encerra o processo, mas o
                // motivo fica no log em vez de so no Visualizador de Eventos.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Error("excecao na interface (ignorada)", e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null) Log.Error("excecao sem tratamento" + (e.IsTerminating ? " (o app vai fechar)" : ""), ex);
                };

                try
                {
                    TrayContext contexto = new TrayContext(new Sensors(), new HidPanel(), activation);
                    contexto.IniciarCasa();
                    Application.Run(contexto);
                }
                catch (Exception ex)
                {
                    Log.Error("falha fatal", ex);
                    ErroInesperado.Mostrar(ex);
                }
            }
        }
    }
}
