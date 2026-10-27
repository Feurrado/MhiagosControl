using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    public static partial class Tests
    {
        /// <summary>
        /// Tests.exe --anim-real pasta: abre a janela de verdade e o widget de
        /// verdade NA TELA, com a casa simulada (nenhum comando sai para a nuvem),
        /// clica em ligar e em + pelo mesmo caminho do clique real e fotografa a
        /// tela (CopyFromScreen) a cada ~40 ms. Usa a opcao de animacoes do
        /// proprio Windows, como o app instalado.
        /// </summary>
        private static int CapturaDaAnimacaoReal(string[] args)
        {
            string pasta = args[1];
            Directory.CreateDirectory(pasta);
            string tmp = Path.Combine(Path.GetTempPath(), "mhiagos-anim-real-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            PreferenciasCasa.Arquivo = Path.Combine(tmp, "casa.ini");
            HistoricoDoAr.Arquivo = Path.Combine(tmp, "casa-historico.txt");
            CiclosDaLavadora.Arquivo = Path.Combine(tmp, "casa-ciclos.txt");
            ConfigDoWidget.Caminho = Path.Combine(tmp, "widget.ini");
            CasaNoWidget.CaminhoAntigo = Path.Combine(tmp, "widget-casa.ini");
            PaginaCasa.IgnorarCofre = true;
            Ui.AnimacoesForcadas = null;   // a opcao do Windows, como no app
            using (Control ancora = new Control()) { IntPtr h = ancora.Handle; }
            Motion.PrenderNaInterface();
            Console.WriteLine("efeitos do Windows: " + (Ui.Animacoes ? "ligados" : "desligados"));

            List<Aparelho> l = Exemplo();
            l[1].Estado["t_power"] = "0";
            ServicoCasa.Simular(l);
            Config config = AuditConfig(tmp, "anim-real");
            FreshData dados = new FreshData();
            using (SettingsForm form = new SettingsForm(config, dados.Sensors, dados))
            {
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(40, 40);
                form.TopMost = true;
                form.Show();
                ((Timer)Field(form, "_tick")).Stop();
                NavBar nav = (NavBar)Field(form, "_nav");
                for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == "visao") nav.SelectedIndex = i;
                EsperarQuadros(400);
                CartaoDoArVisao arv = (CartaoDoArVisao)Field(form, "_vgAr");
                Gravar(pasta, "janela-ligar", arv, delegate { InvokePrivate(arv.BotaoLiga, "OnClick", EventArgs.Empty); });
                EsperarQuadros(900);
                Gravar(pasta, "janela-mais", arv, delegate { InvokePrivate(arv.BotaoMais, "OnClick", EventArgs.Empty); });
                EsperarQuadros(300);
                form.Close();
            }

            l = Exemplo();
            l[1].Estado["t_power"] = "0";
            ServicoCasa.Simular(l);
            ConfigDoWidget cw = new ConfigDoWidget();
            cw.Ativo = true; cw.Casa = true;
            using (WidgetDeBateria w = new WidgetDeBateria(cw))
            {
                w.Location = new Point(420, 260);
                w.Show();
                w.TopMost = true;
                EsperarQuadros(1200);
                Gravar(pasta, "widget-ligar", w, delegate { w.Casa.Clicar(2); });
                EsperarQuadros(900);
                Gravar(pasta, "widget-mais", w, delegate { w.Casa.Clicar(1); });
                EsperarQuadros(300);
                w.Close();
            }
            ServicoCasa.PararSimulacao();
            return 0;
        }

        /// <summary>Clica e fotografa a regiao do controle na tela: um quadro antes e doze a cada ~40 ms.</summary>
        private static void Gravar(string pasta, string nome, Control c, MethodInvoker clicar)
        {
            HashSet<long> vistos = new HashSet<long>();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = -1; i < 12; i++)
            {
                if (i == 0) { clicar(); sw.Restart(); }
                if (i > 0) EsperarQuadros(40);
                // o widget cresce ao ganhar itens: a regiao e lida a cada quadro
                Rectangle r = c is Form ? c.Bounds : c.RectangleToScreen(c.ClientRectangle);
                // o widget e uma janela em camadas: o quadro dele e o bitmap que ele entrega ao Windows (_quadro), sem o fundo da tela atras
                Bitmap doWidget = c is WidgetDeBateria ? (Bitmap)Field(c, "_quadro") : null;
                using (Bitmap b = doWidget != null ? new Bitmap(doWidget) : new Bitmap(r.Width, r.Height))
                {
                    if (doWidget == null) using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    vistos.Add(Assinatura(b));
                    b.Save(Path.Combine(pasta, nome + "-" + (i + 1).ToString("00") + "-" + (i < 0 ? "antes" : sw.ElapsedMilliseconds + "ms") + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine(nome + ": " + vistos.Count + " quadros diferentes de 13");
        }
    }
}
