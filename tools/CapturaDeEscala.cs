using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace MhiagosControl
{
    /// <summary>
    /// Fotografa cada pagina da configuracao numa escala de tela simulada.
    ///
    ///   Tests.exe --captura-escala 1.5 C:\pasta
    ///
    /// Roda em processo proprio porque a escala entra nas fontes estaticas do
    /// Ui, criadas uma vez por processo. Nao e conferencia automatica: as
    /// imagens existem para alguem olhar texto cortado e sobreposicao a 125%,
    /// 150% e 200% sem trocar a escala da maquina, o que pede sair da sessao.
    /// </summary>
    public static partial class Tests
    {
        private sealed class DadosDeCaptura : ISettingsData, IReadingStatusData
        {
            public readonly List<SensorEntry> Sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, "°C", 47),
                AuditSensor("load", "CPU", SensorType.Load, "%", 23),
                AuditSensor("gpu", "GPU", SensorType.Temperature, "°C", 52),
                AuditSensor("fan", "CPU", SensorType.Fan, "RPM", 1240) };
            public Dictionary<string, float> CurrentSnapshot()
            {
                Dictionary<string, float> d = new Dictionary<string, float>();
                foreach (SensorEntry s in Sensors) d[s.Id] = s.Value.Value;
                return d;
            }
            public List<SensorEntry> RefreshSensorList() { return Sensors; }
            public void SetShowAllSensors(bool value) { }
            public ReadingState CurrentReadingState(string id) { return ReadingState.Live; }
        }

        private static void QuadrosDaAnimacao(Form form, AnimacaoDeRecolher motor, string pasta, string nome)
        {
            foreach (int pct in new int[] { 0, 20, 40, 60, 80, 100 })
            {
                // Sem DoEvents: o Timer de verdade avancaria pelo relogio e
                // desfaria o quadro pedido.
                motor.Avancar(AnimacaoDeRecolher.DuracaoMs * pct / 100);
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                    bmp.Save(Path.Combine(pasta, nome + "-" + pct.ToString("000") + ".png"), ImageFormat.Png);
                }
            }
        }

        private static void Esperar(int ms)
        {
            System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
        }

        private static void Medir(string nome, AnimacaoDeRecolher motor, MethodInvoker acao, NavBar nav = null)
        {
            if (motor == null) motor = (AnimacaoDeRecolher)nav.GetType().GetField("_anim", AuditFlags).GetValue(nav);
            List<long> marcas = new List<long>();
            System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
            EventHandler h = delegate { marcas.Add(w.ElapsedMilliseconds); };
            if (motor != null) motor.Quadro += h;
            acao();
            if (motor == null)
            {
                motor = (AnimacaoDeRecolher)nav.GetType().GetField("_anim", AuditFlags).GetValue(nav);
                motor.Quadro += h;
            }
            while (motor.Animando && w.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(0); }
            long total = w.ElapsedMilliseconds;
            motor.Quadro -= h;
            long pior = 0;
            for (int i = 1; i < marcas.Count; i++) pior = Math.Max(pior, marcas[i] - marcas[i - 1]);
            Console.WriteLine(string.Format("{0,-9} {1,3} quadros em {2,4} ms  ~{3,3} fps  maior intervalo {4} ms",
                nome, marcas.Count, total, total > 0 ? marcas.Count * 1000 / total : 0, pior));
            Esperar(300);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, int extra);
        private static void Ativar(Form f)
        {
            keybd_event(0x12, 0, 0, 0); keybd_event(0x12, 0, 2, 0);   // um Alt solto libera o primeiro plano
            SetForegroundWindow(f.Handle);
            f.Activate();
        }

        /// <summary>
        /// A janela de verdade, pelo que o DWM compoe (Mica, sombra): copia da
        /// tela, e nao DrawToBitmap. Tests.exe --captura-tela 1.0 pasta
        /// </summary>
        private static void CapturaDaTela(SettingsForm form, NavBar nav, string pasta)
        {
            form.TopMost = true; Ativar(form); Esperar(300);
            foreach (int i in new int[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                nav.SelectedIndex = i;
                Esperar(700);
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(form.Location, Point.Empty, form.Size);
                    bmp.Save(Path.Combine(pasta, "tela-" + i + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("mica: " + Mica.Disponivel);
        }

        private static void MaisCaros(Control raiz)
        {
            List<KeyValuePair<double, string>> r = new List<KeyValuePair<double, string>>();
            Queue<Control> fila = new Queue<Control>();
            fila.Enqueue(raiz);
            while (fila.Count > 0)
            {
                Control c = fila.Dequeue();
                foreach (Control f in c.Controls) fila.Enqueue(f);
                if (c == raiz || c.Width < 4 || c.Height < 4 || !c.Visible) continue;
                using (Bitmap b = new Bitmap(c.Width, c.Height))
                {
                    System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
                    for (int k = 0; k < 20; k++) c.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size));
                    r.Add(new KeyValuePair<double, string>(w.Elapsed.TotalMilliseconds / 20, c.GetType().Name + " " + c.Width + "x" + c.Height));
                }
            }
            r.Sort(delegate (KeyValuePair<double, string> a, KeyValuePair<double, string> b) { return b.Key.CompareTo(a.Key); });
            for (int i = 0; i < Math.Min(5, r.Count); i++) Console.WriteLine(string.Format("    {0,6:0.00} ms  {1}", r[i].Key, r[i].Value));
        }

        /// <summary>
        /// Custo de pintar cada pagina inteira: DrawToBitmap 40 vezes, mediana e
        /// pior caso. Tests.exe --medir-pintura 1.0 pasta
        /// </summary>
        private static void MedirPintura(SettingsForm form, NavBar nav)
        {
            for (int i = 0; i < nav.Items.Count; i++)
            {
                nav.SelectedIndex = i;
                InvokePrivate(form, "AtualizarPaginaAtiva", true);
                Application.DoEvents();
                List<double> t = new List<double>();
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                    for (int k = 0; k < 40; k++)
                    {
                        System.Diagnostics.Stopwatch w = System.Diagnostics.Stopwatch.StartNew();
                        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                        t.Add(w.Elapsed.TotalMilliseconds);
                    }
                t.Sort();
                Console.WriteLine(string.Format("{0,-16} mediana {1,6:0.0} ms  pior {2,6:0.0} ms", nav.Items[i].Text, t[t.Count / 2], t[t.Count - 1]));
                if (i == 3) MaisCaros(form);
            }
        }

        /// <summary>
        /// O menu do icone da bandeja, aberto de verdade e copiado da tela:
        /// Tests.exe --captura-menu 1.0 pasta
        /// </summary>
        private static int CapturaDoMenu(string[] args)
        {
            float zoom = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            AppDomain.CurrentDomain.SetData("MhiagosControl.Zoom", zoom);
            Directory.CreateDirectory(args[2]);
            T.Language = T.PtBr;
            ContextMenuStrip m = MenuTema.Novo();
            ToolStripMenuItem sub = MenuTema.Submenu(T.TrayProfiles);
            sub.DropDownItems.Add(MenuTema.Opcao("A", true, null));
            sub.DropDownItems.Add(MenuTema.Opcao("Jogos  ·  padrao", false, null));
            m.Items.Add(sub);
            m.Items.Add(MenuTema.Item(T.TrayConfigure, null));
            ToolStripMenuItem w = MenuTema.Item(T.TrayBatteryWidget, null); w.Checked = true;
            m.Items.Add(w);
            MenuTema.Separador(m.Items);
            m.Items.Add(MenuTema.Item(T.TrayPause, null));
            ToolStripMenuItem a = MenuTema.Item(T.TrayAutostart, null); a.Checked = true;
            m.Items.Add(a);
            m.Items.Add(MenuTema.Item(T.TrayOpenData, null));
            MenuTema.Separador(m.Items);
            m.Items.Add(MenuTema.Item(T.TrayExit, null));
            m.Show(new Point(60, 60));
            Esperar(400);
            Cursor.Position = new Point(120, 60 + 88);
            Esperar(400);
            sub.ShowDropDown();
            Esperar(400);
            using (Bitmap bmp = new Bitmap(520, 420))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(new Point(50, 50), Point.Empty, bmp.Size);
                bmp.Save(Path.Combine(args[2], "menu.png"), ImageFormat.Png);
            }
            m.Close();
            return 0;
        }

        /// <summary>
        /// O que a janela mostra no instante em que abre, antes do Shown:
        /// Tests.exe --captura-abertura 1.0 pasta [chave-da-pagina]
        /// </summary>
        private static int CapturaDaAbertura(string[] args)
        {
            Directory.CreateDirectory(args[2]);
            string root = Path.Combine(Path.GetTempPath(), "MhiagosControl-abertura-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            typeof(Log).GetField("_path", AuditFlags).SetValue(null, Path.Combine(root, "log.txt"));
            MetricHistory.Arquivo = Path.Combine(root, "history.dat");
            T.Language = T.PtBr;
            Config config = AuditConfig(root, "abertura");
            config.LastPage = args.Length > 3 ? args[3] : "baterias";
            DadosDeCaptura dados = new DadosDeCaptura();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show();
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                    bmp.Save(Path.Combine(args[2], "abertura-antes-do-shown.png"), ImageFormat.Png);
                }
                Esperar(500);
                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                    bmp.Save(Path.Combine(args[2], "abertura-depois.png"), ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }

        private static int CapturaDeEscala(string[] args)
        {
            float zoom = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            string pasta = args[2];
            bool medir = args[0] == "--medir-animacao";
            bool tela = args[0] == "--captura-tela";
            bool pintura = args[0] == "--medir-pintura";
            // Antes de qualquer toque no Ui: as fontes nascem com esta escala.
            AppDomain.CurrentDomain.SetData("MhiagosControl.Zoom", zoom);
            Directory.CreateDirectory(pasta);
            string root = Path.Combine(Path.GetTempPath(), "MhiagosControl-captura-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            typeof(Log).GetField("_path", AuditFlags).SetValue(null, Path.Combine(root, "log.txt"));
            MetricHistory.Arquivo = Path.Combine(root, "history.dat");
            // Historico sintetico (ruido, uma falha curta e uma longa) para a curva
            // dos mostradores aparecer nas capturas.
            MetricHistory.Seguir(new string[] { "cpu", "load", "gpu", "fan" });
            {
                Random rnd = new Random(7);
                DateTime ini = DateTime.UtcNow.AddMinutes(-45);
                for (int k = 0; k < 45 * 12; k++)
                {
                    if (k > 470 && k < 475) continue;       // falha curta (25 s)
                    if (k > 495 && k < 515) continue;       // falha longa
                    double a = k;
                    MetricHistory.AmostrarEm(delegate (string id) { return (float?)(45 + 8 * Math.Sin(a / 40) + rnd.NextDouble() * 4); }, ini.AddSeconds(k * 5));
                }
            }
            T.Language = args.Length > 3 ? args[3] : T.PtBr;


            Config config = AuditConfig(root, "captura");
            config.MetricIds.Clear(); config.MetricSizes.Clear();
            config.MetricIds.AddRange(new string[] { "cpu", "load", "gpu", "fan" });
            config.MetricSizes.AddRange(new int[] { 1, 1, 0, 0 });
            config.Save();
            DadosDeCaptura dados = new DadosDeCaptura();
            Size[] tamanhos = { new Size(Ui.S(1010), Ui.S(820)), new Size(Ui.S(880), Ui.S(660)) };
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = (medir || tela) ? new Point(20, 20) : new Point(-30000, -30000);
                form.Show(); ((Timer)Field(form, "_tick")).Stop();
                NavBar nav = (NavBar)Field(form, "_nav");
                if (tela) { CapturaDaTela(form, nav, pasta); form.Close(); return 0; }
                if (pintura) { MedirPintura(form, nav); form.Close(); return 0; }
                foreach (Size tamanho in tamanhos)
                {
                    form.Size = tamanho;
                    for (int i = 0; i < nav.Items.Count; i++)
                    {
                        nav.SelectedIndex = i;
                        InvokePrivate(form, "AtualizarPaginaAtiva", true);
                        Application.DoEvents();
                        string nome = string.Format("{0:000}-{1}-{2}x{3}-{4}", (int)(zoom * 100), i, tamanho.Width, tamanho.Height,
                                                    nav.Items[i].Text.Replace(' ', '_'));
                        using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                            bmp.Save(Path.Combine(pasta, nome + ".png"), ImageFormat.Png);
                        }
                    }
                }
                // Quadros das animacoes, conduzidos a mao: o painel Widget e o
                // "Mais detalhes" a 0, 25, 50, 75 e 100% do tempo.
                form.Size = tamanhos[0];
                for (int i = 0; i < nav.Items.Count; i++)
                    if (nav.Items[i].Key == "baterias") nav.SelectedIndex = i;
                Ui.AnimacoesForcadas = true;
                // A barra lateral recolhendo e abrindo de novo.
                nav.Collapsed = true;
                QuadrosDaAnimacao(form, (AnimacaoDeRecolher)typeof(NavBar).GetField("_anim", AuditFlags).GetValue(nav), pasta, (int)(zoom * 100) + "-anim-lateral-fechando");
                nav.Collapsed = false;
                QuadrosDaAnimacao(form, (AnimacaoDeRecolher)typeof(NavBar).GetField("_anim", AuditFlags).GetValue(nav), pasta, (int)(zoom * 100) + "-anim-lateral-abrindo");
                form.Close();
            }
            Console.WriteLine("capturas em " + pasta);
            return 0;
        }
    }
}
