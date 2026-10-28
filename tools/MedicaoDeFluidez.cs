using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    public static partial class Tests
    {
        /// <summary>
        /// Tests.exe --medir-fluidez: mede, com a casa simulada e sensores de
        /// teste, o custo das coisas que rodam na thread da interface - troca
        /// de pagina, pintura da janela, o tique da Visao geral, um
        /// quadro do widget - e a CPU do proprio processo parado com a janela
        /// e o widget abertos. Imprime em ms (mediana de varias rodadas).
        /// </summary>
        private static int MedicaoDeFluidez(string[] args)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "mhiagos-fluidez-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            PreferenciasCasa.Arquivo = Path.Combine(tmp, "casa.ini");
            HistoricoDoAr.Arquivo = Path.Combine(tmp, "casa-historico.txt");
            CiclosDaLavadora.Arquivo = Path.Combine(tmp, "casa-ciclos.txt");
            ConfigDoWidget.Caminho = Path.Combine(tmp, "widget.ini");
            CasaNoWidget.CaminhoAntigo = Path.Combine(tmp, "widget-casa.ini");
            PaginaCasa.IgnorarCofre = true;
            Ui.AnimacoesForcadas = null;
            ServicoCasa.Simular(Exemplo());
            Config config = AuditConfig(tmp, "fluidez");
            FreshData dados = new FreshData();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ClientSize = SettingsForm.TamanhoPadrao;
                form.Show();
                ((Timer)Field(form, "_tick")).Stop();
                NavBar nav = (NavBar)Field(form, "_nav");
                EsperarQuadros(300);

                // troca de pagina: selecionar e pintar a janela inteira
                foreach (NavItem it in nav.Items)
                {
                    List<double> ms = new List<double>();
                    for (int k = 0; k < 6; k++)
                    {
                        nav.SelectedIndex = 0; Application.DoEvents();
                        int alvo = nav.Items.IndexOf(it);
                        if (alvo == 0) nav.SelectedIndex = 1;
                        Application.DoEvents();
                        Stopwatch sw = Stopwatch.StartNew();
                        nav.SelectedIndex = alvo;
                        using (Bitmap b = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(b, new Rectangle(0, 0, form.Width, form.Height));
                        sw.Stop();
                        ms.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Console.WriteLine("troca para " + it.Key + ": " + Mediana(ms).ToString("0.0") + " ms (troca + pintura da janela)");
                }

                // pintura da Visao geral sozinha e o tique de 1 s
                for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == "visao") nav.SelectedIndex = i;
                Application.DoEvents();
                List<double> pint = new List<double>(), tique = new List<double>(), arr = new List<double>();
                Control pg = (Control)Field(form, "_pgVisao");
                Dictionary<string, float> snap = new Dictionary<string, float>();
                foreach (SensorEntry s in dados.Sensors) snap[s.Id] = 40f;
                for (int k = 0; k < 15; k++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    using (Bitmap b = new Bitmap(pg.Width, pg.Height)) pg.DrawToBitmap(b, new Rectangle(0, 0, pg.Width, pg.Height));
                    pint.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart();
                    InvokePrivate(form, "AtualizarVisaoGeral", snap);
                    Application.DoEvents();
                    tique.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart();
                    InvokePrivate(form, "ArranjarVisaoGeral");
                    arr.Add(sw.Elapsed.TotalMilliseconds);
                }
                Console.WriteLine("pintura da Visao geral: " + Mediana(pint).ToString("0.0") + " ms");
                Console.WriteLine("tique da Visao geral (AtualizarVisaoGeral + repinturas pendentes): " + Mediana(tique).ToString("0.0") + " ms");
                Console.WriteLine("arranjo da Visao geral (a cada aviso da casa): " + Mediana(arr).ToString("0.0") + " ms");

                // CPU do processo parado com a janela na Visao geral (timer de 1 s ligado)
                ((Timer)Field(form, "_tick")).Start();
                Process me = Process.GetCurrentProcess();
                TimeSpan c0 = me.TotalProcessorTime;
                EsperarQuadros(10000);
                me.Refresh();
                Console.WriteLine("CPU parado, janela na Visao geral (10 s): " + ((me.TotalProcessorTime - c0).TotalMilliseconds / 100.0).ToString("0.0") + "% de um nucleo");
                ((Timer)Field(form, "_tick")).Stop();
                form.Close();
            }

            ConfigDoWidget cw = new ConfigDoWidget();
            cw.Ativo = true; cw.Casa = true;
            using (WidgetDeBateria w = new WidgetDeBateria(cw))
            {
                w.Show(); w.Location = new Point(-30000, -30000);
                EsperarQuadros(500);
                List<double> q = new List<double>();
                for (int k = 0; k < 30; k++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    w.Renderizar();
                    q.Add(sw.Elapsed.TotalMilliseconds);
                }
                Console.WriteLine("quadro do widget (Renderizar): " + Mediana(q).ToString("0.0") + " ms; a 25 qps enquanto anima = " + (Mediana(q) * 25 / 10).ToString("0.0") + "% de um nucleo");
                Process me = Process.GetCurrentProcess();
                TimeSpan c0 = me.TotalProcessorTime;
                EsperarQuadros(10000);
                me.Refresh();
                Console.WriteLine("CPU parado, so o widget (10 s): " + ((me.TotalProcessorTime - c0).TotalMilliseconds / 100.0).ToString("0.0") + "% de um nucleo");
                w.Close();
            }
            ServicoCasa.PararSimulacao();
            return 0;
        }

        private static double Mediana(List<double> v)
        {
            List<double> c = new List<double>(v);
            c.Sort();
            return c.Count == 0 ? 0 : c[c.Count / 2];
        }
    }
}
