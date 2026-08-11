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
    public class TrayContext : ApplicationContext
    {
        private const int PeriodMs = 1100;   // cadencia do firmware original: ~1105 ms

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private NotifyIcon _icon;
        private Icon _iconNormal, _iconAlert;
        private Control _marshal;
        private Thread _worker;
        private ManualResetEvent _stop = new ManualResetEvent(false);

        private HidPanel _panel = new HidPanel();
        private Sensors _sensors = new Sensors();
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
        private bool _alerting = false;

        /// <summary>
        /// Travas de alerta, uma por limiar. Sao quatro e nao duas porque o
        /// limiar superior e o inferior de um mesmo mostrador disparam e
        /// rearmam em momentos diferentes.
        /// </summary>
        private bool _hi1 = false, _hi2 = false, _lo1 = false, _lo2 = false;
        private bool _iconIsAlert = false;

        public TrayContext()
        {
            Log.Write("=== Mhiagos Control iniciando ===");
            _cfg = Config.Load();
            Sensors.ShowAll = _cfg.ShowAllSensors;

            // antes de qualquer texto de tela: o menu da bandeja e montado logo
            // abaixo e a tela de carregamento pode aparecer a qualquer momento
            T.Language = string.IsNullOrEmpty(_cfg.Language) ? T.Detect() : _cfg.Language;

            _marshal = new Control();
            GC.KeepAlive(_marshal.Handle);   // cria o handle na thread de UI

            Autostart.RemoveLegacyTask();

            _iconNormal = Assets.TrayIcon;
            _iconAlert = MakeAlertIcon(_iconNormal);

            _icon = new NotifyIcon();
            _icon.Icon = _iconNormal;
            _icon.Text = T.TrayStarting;
            _icon.Visible = true;
            _icon.Click += new EventHandler(OnIconClick);
            _icon.DoubleClick += new EventHandler(OnConfig);
            BuildMenu();

            // A abertura das fontes NAO acontece aqui. Ela e agendada para
            // depois que Application.Run assumir: veja AbrirSensores.
            _iniciando = true;
            _marshal.BeginInvoke(new MethodInvoker(AbrirSensores));
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

            Thread init = new Thread(delegate()
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

                try { _marshal.BeginInvoke(new SensoresProntosHandler(SensoresProntos), falha); }
                catch (Exception ex) { Log.Error("retorno da inicializacao", ex); }
            });
            init.IsBackground = true;
            init.Name = "SensorInit";
            init.Start();
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
            _iniciando = false;
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

            // Antes de qualquer caminho que possa abrir a janela de
            // configuracao: e la que a grade de metricas se monta e passa a
            // acompanhar leituras.
            MetricHistory.Carregar();
            MetricHistory.Seguir(_cfg.MetricIds);

            Profile act = _cfg.Active;
            if (_sensorsOk && (string.IsNullOrEmpty(act.Panel1Id) || string.IsNullOrEmpty(act.Panel2Id)))
            {
                PickDefaults(act);
                OnConfig(null, null);
            }

            WarnAboutOriginalTask();

            Microsoft.Win32.SystemEvents.SessionEnding += new Microsoft.Win32.SessionEndingEventHandler(OnSessionEnding);

            Publicar();   // antes de a thread comecar, senao o primeiro ciclo nao tem perfil
            _worker = new Thread(new ThreadStart(WorkerLoop));
            _worker.IsBackground = true;
            _worker.Name = "PanelUpdate";
            _worker.Start();
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
            if (_iniciando) MostrarSplash();
        }

        private volatile bool _iniciando = false;
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
            foreach (Profile p in _cfg.Profiles)
            {
                MenuItem mi = new MenuItem(p.Name, new EventHandler(OnPickProfile));
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

            _cfg.ActiveName = p.Name;
            _cfg.Save();
            Publicar();
            RebuildProfileMenu();
            ResetAlerts();
            Log.Write("perfil ativo: " + p.Name);
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

        /// <summary>
        /// Variante de alerta: o mesmo icone com um ponto vermelho no canto.
        ///
        /// Recolorir o icone inteiro o tornaria irreconhecivel na bandeja; um
        /// distintivo preserva a identidade e comunica o estado.
        ///
        /// Icon.FromHandle nao assume a posse do handle, entao Dispose nao o
        /// libera - clonamos e destruimos o handle original explicitamente.
        /// </summary>
        private static Icon MakeAlertIcon(Icon source)
        {
            if (source == null) return null;
            try
            {
                int s = source.Width;
                using (Bitmap bmp = new Bitmap(s, s))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.DrawIcon(source, new Rectangle(0, 0, s, s));

                        int d = Math.Max(5, s / 2 - 1);
                        Rectangle dot = new Rectangle(s - d, s - d, d - 1, d - 1);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 60, 50)))
                            g.FillEllipse(b, dot);
                        using (Pen p = new Pen(Color.FromArgb(240, 255, 255, 255), 1f))
                            g.DrawEllipse(p, dot);
                    }
                    IntPtr h = bmp.GetHicon();
                    try
                    {
                        using (Icon tmp = Icon.FromHandle(h))
                            return (Icon)tmp.Clone();
                    }
                    finally { DestroyIcon(h); }
                }
            }
            catch (Exception ex) { Log.Error("criacao do icone de alerta", ex); return source; }
        }

        // ---------------- thread de atualizacao ----------------

        private void WorkerLoop()
        {
            bool warnedDevice = false;
            while (!_stop.WaitOne(0))
            {
                DateTime started = DateTime.UtcNow;
                if (!_paused)
                {
                    try { UpdateOnce(ref warnedDevice); }
                    catch (Exception ex)
                    {
                        Log.Error("ciclo de atualizacao", ex);
                        SetTooltip("Mhiagos Control - erro: " + ex.Message);
                    }
                }
                double spent = (DateTime.UtcNow - started).TotalMilliseconds;
                int wait = (int)Math.Max(50, PeriodMs - spent);
                if (_stop.WaitOne(wait)) break;
            }
            Log.Write("thread de atualizacao encerrada");
        }

        private void UpdateOnce(ref bool warnedDevice)
        {
            Profile vigiar = _live;
            if (vigiar == null) return;   // ainda nao publicado

            // O que vai ao mostrador pode nao ser o perfil ativo: o rodizio
            // gira o quadro. Os alertas, porem, seguem SEMPRE o perfil ativo -
            // limiares que mudassem a cada giro travariam e destravariam
            // sozinhos, e um alerta que aparece conforme a hora do relogio nao
            // e alerta. Girar e sobre o mostrador, nao sobre a vigilancia.
            Profile cfg = Rodar(vigiar);
            bool mesmo = ReferenceEquals(cfg, vigiar) || cfg.Name == vigiar.Name;

            SensorEntry e1, e2, g1, g2;
            lock (_sensorLock)
            {
                // Ciclo de amostra: a leitura se abre para os grupos dos cartoes
                // antes do Refresh, senao os sensores fora do foco viriam com o
                // valor do ultimo ciclo em que foram lidos - historico de mentira.
                bool amostrar = MetricHistory.HoraDeAmostrar();
                if (amostrar) _sensors.Focar(Foco(true));

                _sensors.Refresh();
                e1 = _sensors.ReadEntry(cfg.Panel1Id);
                e2 = _sensors.ReadEntry(cfg.Panel2Id);
                g1 = mesmo ? e1 : _sensors.ReadEntry(vigiar.Panel1Id);
                g2 = mesmo ? e2 : _sensors.ReadEntry(vigiar.Panel2Id);
                if (_snapshotWanted)
                    Interlocked.Exchange(ref _snapshot, _sensors.Snapshot());

                if (amostrar)
                {
                    MetricHistory.Amostrar(Ler);
                    _sensors.Focar(Foco(false));
                }
            }

            MetricHistory.SalvarSeVencido();   // fora do lock: escreve em disco

            PanelValue v1 = Scaling.Prepare(e1, Scaling.Effective(cfg.Divisor1, e1), cfg.Fahrenheit);
            PanelValue v2 = Scaling.Prepare(e2, Scaling.Effective(cfg.Divisor2, e2), false);

            PanelValue a1 = mesmo ? v1 : Scaling.Prepare(g1, Scaling.Effective(vigiar.Divisor1, g1), vigiar.Fahrenheit);
            PanelValue a2 = mesmo ? v2 : Scaling.Prepare(g2, Scaling.Effective(vigiar.Divisor2, g2), false);

            // Apagar e sobre o mostrador, nao sobre a vigilancia: o quadro sai
            // em branco, mas os alertas continuam sendo avaliados com os
            // valores reais logo abaixo. Uma CPU nao esfria porque o dono saiu.
            bool ocioso = Ocioso();
            bool ok = ocioso
                ? _panel.Send(null, null, cfg.Fahrenheit, cfg.Percent)
                : _panel.Send(v1.Value, v2.Value, cfg.Fahrenheit, cfg.Percent);

            if (!ok && !warnedDevice) { Log.Write("painel ausente ou envio falhou"); warnedDevice = true; }
            else if (ok && warnedDevice) { Log.Write("painel reconectado"); warnedDevice = false; }

            EvaluateAlerts(vigiar, a1.Value, a2.Value);

            string text = string.Format(CultureInfo.InvariantCulture, "Mhiagos Control  {0}{1} / {2}{3}",
                v1.Value.HasValue ? v1.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Fahrenheit ? "F" : "C",
                v2.Value.HasValue ? v2.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Percent ? "%" : "W");
            if (!mesmo) text += "  " + cfg.Name;
            if (_alerting) text += T.TagAlert;
            if (ocioso) text += T.TagIdle;
            if (v1.Clamped || v2.Clamped) text += T.TagOver;
            if (!ok) text += T.TagNoDevice;
            SetTooltip(text);
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
        /// deriva: o ciclo dura 1,1 s mais o que a varredura do hardware levar,
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

        /// <summary>
        /// Notifica na borda de entrada e so rearma quando o valor volta para a
        /// faixa boa - sem isso, um sensor oscilando no limite gera notificacao
        /// a cada 1,1 s.
        /// </summary>
        private void EvaluateAlerts(Profile cfg, int? p1, int? p2)
        {
            bool hi1 = Acima(p1, cfg.Alert1), lo1 = Abaixo(p1, cfg.Alert1Low);
            bool hi2 = Acima(p2, cfg.Alert2), lo2 = Abaixo(p2, cfg.Alert2Low);

            if (hi1 && !_hi1) Notify(T.AlertReached(1, p1.Value, cfg.Alert1));
            if (hi2 && !_hi2) Notify(T.AlertReached(2, p2.Value, cfg.Alert2));
            if (lo1 && !_lo1) Notify(T.AlertDropped(1, p1.Value, cfg.Alert1Low));
            if (lo2 && !_lo2) Notify(T.AlertDropped(2, p2.Value, cfg.Alert2Low));

            _hi1 = hi1; _hi2 = hi2; _lo1 = lo1; _lo2 = lo2;
            bool any = hi1 || hi2 || lo1 || lo2;
            if (any != _alerting)
            {
                _alerting = any;
                SetIconAlert(any);
            }
        }

        // Zero desliga. Sem leitura nao dispara nada: mostrador apagado nao e
        // valor baixo - seria o alerta inferior gritando toda vez que o sensor
        // sumisse por um ciclo.
        private static bool Acima(int? v, int limiar) { return limiar > 0 && v.HasValue && v.Value >= limiar; }
        private static bool Abaixo(int? v, int limiar) { return limiar > 0 && v.HasValue && v.Value <= limiar; }

        private void ResetAlerts()
        {
            _hi1 = _hi2 = _lo1 = _lo2 = false;
            if (_alerting) { _alerting = false; SetIconAlert(false); }
        }

        private void Notify(string message)
        {
            Log.Write("ALERTA: " + message);
            Marshal(delegate
            {
                try
                {
                    _icon.BalloonTipTitle = T.AppName;
                    _icon.BalloonTipText = message;
                    _icon.BalloonTipIcon = ToolTipIcon.Warning;
                    _icon.ShowBalloonTip(5000);
                }
                catch { }
            });
        }

        private void SetIconAlert(bool alert)
        {
            if (_iconIsAlert == alert) return;
            _iconIsAlert = alert;
            Marshal(delegate { try { _icon.Icon = alert ? _iconAlert : _iconNormal; } catch { } });
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
                    using (SettingsForm f = new SettingsForm(_cfg, _cache, GetSnapshot, ReListSensors))
                    {
                        // Aplicar dentro da janela ja vale no mostrador: sem
                        // isto o perfil so mudaria ao fechar, o que faria o
                        // botao parecer sem efeito.
                        f.Applied += delegate { Publicar(); ResetAlerts(); };
                        r = f.ShowDialog();
                    }
                    if (r == DialogResult.Retry) _cache = ReListSensors();
                }
                while (r == DialogResult.Retry);

                _snapshotWanted = false;
                _janelaAberta = false;   // antes de Publicar, que reaplica o foco

                if (r != DialogResult.OK) _cfg = Config.Load();   // descarta edicoes
                MetricHistory.Seguir(_cfg.MetricIds);
                Publicar();
                RebuildProfileMenu();
                ResetAlerts();
            }
            catch (Exception ex)
            {
                _snapshotWanted = false;
                _janelaAberta = false;
                AjustarFoco();
                Log.Error("janela de configuracao", ex);
                MessageBox.Show(ex.Message, T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Dictionary<string, float> GetSnapshot()
        {
            return Interlocked.CompareExchange(ref _snapshot, null, null);
        }

        /// <summary>Remonta a lista de sensores sob o lock, para a janela de configuracao.</summary>
        private List<SensorEntry> ReListSensors()
        {
            lock (_sensorLock)
            {
                _cache = _sensors.List();
                return _cache;
            }
        }

        private void OnPause(object sender, EventArgs e)
        {
            _paused = !_paused;
            _miPause.Text = _paused ? T.TrayResume : T.TrayPause;
            Log.Write(_paused ? "pausado" : "retomado");
            if (_paused) { ResetAlerts(); _icon.Text = T.TrayPaused; }
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

        private bool _shutdown = false;
        private void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            try { Microsoft.Win32.SystemEvents.SessionEnding -= new Microsoft.Win32.SessionEndingEventHandler(OnSessionEnding); } catch { }

            _stop.Set();
            if (_worker != null && _worker.IsAlive && !_worker.Join(3000))
                Log.Write("thread de atualizacao nao encerrou a tempo");

            // Depois de a thread parar: gravar com ela viva copiaria uma serie
            // no meio de um balde sendo fechado.
            try { MetricHistory.Salvar(); } catch (Exception ex) { Log.Error("gravacao do historico", ex); }

            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            if (_iconNormal != null) _iconNormal.Dispose();
            if (_iconAlert != null) _iconAlert.Dispose();

            try { _panel.Close(); } catch (Exception ex) { Log.Error("fechamento do painel", ex); }
            lock (_sensorLock)
            {
                try { _sensors.Dispose(); } catch (Exception ex) { Log.Error("fechamento dos sensores", ex); }
            }
            if (_marshal != null) _marshal.Dispose();

            Log.Write("=== encerrado ===");
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Duas instancias disputariam o painel a cada 1,1 s e o resultado pisca.
            // O idioma do Windows vale ate a configuracao ser lida: a checagem
            // de instancia unica acontece antes disso e ja fala com o usuario.
            T.Language = T.Detect();

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\MhiagosControl_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(T.AlreadyRunning,
                        T.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // precisa vir antes de qualquer janela: define o modo escuro do
                // processo, do qual dependem as barras de rolagem nativas
                Theme.InitProcess();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try { Application.Run(new TrayContext()); }
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
