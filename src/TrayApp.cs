using System;
using System.Collections.Generic;
using System.Drawing;
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

        private NotifyIcon _icon;
        private Icon _iconNormal, _iconAlert;
        private Control _marshal;
        private Thread _worker;
        private ManualResetEvent _stop = new ManualResetEvent(false);

        private HidPanel _panel = new HidPanel();
        private Sensors _sensors = new Sensors();
        private readonly object _sensorLock = new object();

        private Config _cfg;
        private volatile bool _paused = false;
        private volatile bool _snapshotWanted = false;
        private Dictionary<string, float> _snapshot = new Dictionary<string, float>();

        private List<SensorEntry> _cache = new List<SensorEntry>();
        private MenuItem _miPause, _miAutostart, _miProfiles;
        private bool _sensorsOk = false;
        private bool _alerting = false, _alert1 = false, _alert2 = false;
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

            Profile act = _cfg.Active;
            if (_sensorsOk && (string.IsNullOrEmpty(act.Panel1Id) || string.IsNullOrEmpty(act.Panel2Id)))
            {
                PickDefaults(act);
                OnConfig(null, null);
            }

            WarnAboutOriginalTask();

            Microsoft.Win32.SystemEvents.SessionEnding += new Microsoft.Win32.SessionEndingEventHandler(OnSessionEnding);

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
            RebuildProfileMenu();
            ResetAlerts();
            Log.Write("perfil ativo: " + p.Name);
        }

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
            Profile cfg = _cfg.Active;
            SensorEntry e1, e2;

            lock (_sensorLock)
            {
                _sensors.Refresh();
                e1 = _sensors.ReadEntry(cfg.Panel1Id);
                e2 = _sensors.ReadEntry(cfg.Panel2Id);
                if (_snapshotWanted)
                    Interlocked.Exchange(ref _snapshot, _sensors.Snapshot());
            }

            PanelValue v1 = Scaling.Prepare(e1, Scaling.Effective(cfg.Divisor1, e1), cfg.Fahrenheit);
            PanelValue v2 = Scaling.Prepare(e2, Scaling.Effective(cfg.Divisor2, e2), false);

            bool ok = _panel.Send(v1.Value, v2.Value, cfg.Fahrenheit, cfg.Percent);
            if (!ok && !warnedDevice) { Log.Write("painel ausente ou envio falhou"); warnedDevice = true; }
            else if (ok && warnedDevice) { Log.Write("painel reconectado"); warnedDevice = false; }

            EvaluateAlerts(cfg, v1.Value, v2.Value);

            string text = string.Format(CultureInfo.InvariantCulture, "Mhiagos Control  {0}{1} / {2}{3}",
                v1.Value.HasValue ? v1.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Fahrenheit ? "F" : "C",
                v2.Value.HasValue ? v2.Value.Value.ToString(CultureInfo.InvariantCulture) : "--",
                cfg.Percent ? "%" : "W");
            if (_alerting) text += T.TagAlert;
            if (v1.Clamped || v2.Clamped) text += T.TagOver;
            if (!ok) text += T.TagNoDevice;
            SetTooltip(text);
        }

        /// <summary>
        /// Notifica na borda de subida e so rearma quando o valor cai abaixo do
        /// limiar - sem isso, um sensor oscilando no limite gera notificacao a
        /// cada 1,1 s.
        /// </summary>
        private void EvaluateAlerts(Profile cfg, int? p1, int? p2)
        {
            // sem leitura nao dispara alerta: mostrador apagado nao e valor baixo
            bool a1 = cfg.Alert1 > 0 && p1.HasValue && p1.Value >= cfg.Alert1;
            bool a2 = cfg.Alert2 > 0 && p2.HasValue && p2.Value >= cfg.Alert2;

            if (a1 && !_alert1) Notify(T.AlertReached(1, p1.Value, cfg.Alert1));
            if (a2 && !_alert2) Notify(T.AlertReached(2, p2.Value, cfg.Alert2));

            _alert1 = a1; _alert2 = a2;
            bool any = a1 || a2;
            if (any != _alerting)
            {
                _alerting = any;
                SetIconAlert(any);
            }
        }

        private void ResetAlerts()
        {
            _alert1 = _alert2 = false;
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
                        r = f.ShowDialog();
                    if (r == DialogResult.Retry) _cache = ReListSensors();
                }
                while (r == DialogResult.Retry);

                _snapshotWanted = false;

                if (r != DialogResult.OK) _cfg = Config.Load();   // descarta edicoes
                RebuildProfileMenu();
                ResetAlerts();
            }
            catch (Exception ex)
            {
                _snapshotWanted = false;
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
