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

        private static int CapturaDeEscala(string[] args)
        {
            float zoom = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            string pasta = args[2];
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
                form.Location = new Point(-30000, -30000);
                form.Show(); ((Timer)Field(form, "_tick")).Stop();
                NavBar nav = (NavBar)Field(form, "_nav");
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
                form.Close();
            }
            Console.WriteLine("capturas em " + pasta);
            return 0;
        }
    }
}
