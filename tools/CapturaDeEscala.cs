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
        private sealed class DadosDeCaptura : ISettingsData, IReadingStatusData, IControleDoWidget
        {
            public readonly List<SensorEntry> Sensors = new List<SensorEntry> {
                AuditSensor("cpu", "CPU", SensorType.Temperature, "°C", 47),
                AuditSensor("load", "CPU", SensorType.Load, "%", 23),
                AuditSensor("gpu", "GPU", SensorType.Temperature, "°C", 52),
                AuditSensor("fan", "CPU", SensorType.Fan, "RPM", 1240) };
            public readonly ConfigDoWidget Widget = new ConfigDoWidget();
            public Dictionary<string, float> CurrentSnapshot()
            {
                Dictionary<string, float> d = new Dictionary<string, float>();
                foreach (SensorEntry s in Sensors) d[s.Id] = s.Value.Value;
                return d;
            }
            public List<SensorEntry> RefreshSensorList() { return Sensors; }
            public void SetShowAllSensors(bool value) { }
            public ReadingState CurrentReadingState(string id) { return ReadingState.Live; }
            public ConfigDoWidget ConfigDoWidget { get { return Widget; } }
            public void LigarWidget(bool ligar) { Widget.Ativo = ligar; }
            public void WidgetMudou() { }
            public void WidgetAoCanto() { }
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

        /// <summary>
        /// Quadros por segundo de verdade, com a janela na tela e o relogio
        /// real: Tests.exe --medir-animacao 1.0 pasta
        /// </summary>
        private static void MedirAnimacoes(SettingsForm form, NavBar nav)
        {
            Ui.AnimacoesForcadas = true;
            for (int i = 0; i < nav.Items.Count; i++)
                if (nav.Items[i].Key == "baterias") nav.SelectedIndex = i;
            Esperar(600);
            BotaoDoWidget botao = (BotaoDoWidget)Field(form, "_btWidget");
            CollapsiblePanel painel = (CollapsiblePanel)Field(form, "_painelDoWidget");
            List<CartaoDeBateria> cartoes = (List<CartaoDeBateria>)Field(form, "_cartoesDeBateria");
            for (int r = 0; r < 3; r++)
            {
                Medir("widget", MotorDe(painel, "_animacao"), delegate { InvokePrivate(botao, "OnClick", EventArgs.Empty); });
                if (cartoes.Count > 0)
                    Medir("detalhes", MotorDe(cartoes[0], "_detalhes"), delegate { cartoes[0].AlternarDetalhes(); });
                Medir("lateral", null, delegate { nav.Collapsed = !nav.Collapsed; }, nav);
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

        private static int CapturaDeEscala(string[] args)
        {
            float zoom = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            string pasta = args[2];
            bool medir = args[0] == "--medir-animacao";
            // Antes de qualquer toque no Ui: as fontes nascem com esta escala.
            AppDomain.CurrentDomain.SetData("MhiagosControl.Zoom", zoom);
            Directory.CreateDirectory(pasta);
            string root = Path.Combine(Path.GetTempPath(), "MhiagosControl-captura-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            typeof(Log).GetField("_path", AuditFlags).SetValue(null, Path.Combine(root, "log.txt"));
            DetalhesDaBateria._caminho = Path.Combine(root, "detalhes.txt");
            DetalhesDaBateria.Recarregar();
            HistoricoDeBateria.Caminho = Path.Combine(root, "baterias.dat");
            HistoricoDeBateria.Zerar();
            MetricHistory.Arquivo = Path.Combine(root, "history.dat");
            T.Language = args.Length > 3 ? args[3] : T.PtBr;

            // Dois aparelhos com historico, para a pagina Baterias ter cartoes.
            DateTime t = DateTime.Now.AddHours(-30);
            Periferico g = Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 100, false);
            Periferico x = Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 100, false);
            for (int m = 0; m < 30 * 60; m += 5)
            {
                int pg = 100 - m / 30, px = 100 - m / 45;
                g = Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, pg, false);
                x = Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, px, false);
                HistoricoDeBateria.Registrar(new Periferico[] { g, x }, t.AddMinutes(m));
            }
            Perifericos.PublicarLogitech(new Periferico[] { g });
            Perifericos.PublicarShark(x);
            DetalhesDaBateria.Alternar("GHub|G325");
            DetalhesDaBateria.Alternar(SettingsForm.ChaveDoPainelDoWidget);

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
                form.Location = medir ? new Point(20, 20) : new Point(-30000, -30000);
                form.Show(); ((Timer)Field(form, "_tick")).Stop();
                NavBar nav = (NavBar)Field(form, "_nav");
                if (medir) { MedirAnimacoes(form, nav); form.Close(); return 0; }
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
                BotaoDoWidget botao = (BotaoDoWidget)Field(form, "_btWidget");
                CollapsiblePanel painel = (CollapsiblePanel)Field(form, "_painelDoWidget");
                List<CartaoDeBateria> cartoes = (List<CartaoDeBateria>)Field(form, "_cartoesDeBateria");
                if (painel.Expandido) InvokePrivate(botao, "OnClick", EventArgs.Empty);
                MotorDe(painel, "_animacao").Avancar(AnimacaoDeRecolher.DuracaoMs);
                InvokePrivate(botao, "OnClick", EventArgs.Empty);
                QuadrosDaAnimacao(form, MotorDe(painel, "_animacao"), pasta, (int)(zoom * 100) + "-anim-widget-abrindo");
                if (cartoes.Count > 0)
                {
                    if (cartoes[0].DetalhesAbertos)
                    {
                        cartoes[0].AlternarDetalhes();
                        MotorDe(cartoes[0], "_detalhes").Avancar(AnimacaoDeRecolher.DuracaoMs);
                    }
                    cartoes[0].AlternarDetalhes();
                    QuadrosDaAnimacao(form, MotorDe(cartoes[0], "_detalhes"), pasta, (int)(zoom * 100) + "-anim-detalhes");
                }
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
