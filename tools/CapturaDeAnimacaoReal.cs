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
            // Nao abre janela por cima de quem esta usando a tela: com um programa em tela
            // cheia em primeiro plano (um jogo), desiste.
            if (TelaOcupada()) { Console.WriteLine("tela ocupada por um programa em tela cheia: captura cancelada"); return 2; }
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

            // uma cidade e uma leitura de exemplo para a temperatura de fora (nada sai para a rede)
            ServicoCasa.Pref.ClimaCidade = "Recife"; ServicoCasa.Pref.ClimaLat = -8.05; ServicoCasa.Pref.ClimaLon = -34.88;
            ClimaAgora exemploClima = new ClimaAgora(); exemploClima.Temperatura = 28.4; exemploClima.Codigo = 2; exemploClima.EmUtc = DateTime.UtcNow;
            Clima.Definir(exemploClima);
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
                // pintura real da Visao geral na tela (Refresh = WM_PAINT de todos os filhos, sincrono)
                Control pgv = (Control)Field(form, "_pgVisao");
                List<double> reais = new List<double>();
                for (int k = 0; k < 15; k++) { System.Diagnostics.Stopwatch swp = System.Diagnostics.Stopwatch.StartNew(); pgv.Refresh(); reais.Add(swp.Elapsed.TotalMilliseconds); EsperarQuadros(20); }
                Console.WriteLine("pintura real da Visao geral na tela (Refresh): " + Mediana(reais).ToString("0.0") + " ms");
                // troca de pagina real na tela, com o fade: do clique ate a pagina nova pintada
                foreach (bool comFade in new bool[] { false, true })
                {
                Ui.AnimacoesForcadas = comFade ? (bool?)null : false;
                foreach (string chave in new string[] { "casa", "metricas", "visao" })
                {
                    int alvo = -1;
                    for (int i = 0; i < nav.Items.Count; i++) if (nav.Items[i].Key == chave) alvo = i;
                    System.Diagnostics.Stopwatch swt = System.Diagnostics.Stopwatch.StartNew();
                    nav.SelectedIndex = alvo;
                    form.Update();
                    double ate = swt.Elapsed.TotalMilliseconds;
                    if (comFade) GravarRapido(pasta, "troca-" + chave, form);
                    Console.WriteLine("troca real para " + chave + (comFade ? " (com fade): " : " (sem fade): ") + ate.ToString("0.0") + " ms ate a pagina na tela");
                    EsperarQuadros(400);
                }
                }
                Ui.AnimacoesForcadas = null;
                Ampliar(arv, Path.Combine(Path.GetDirectoryName(pasta.TrimEnd(Path.DirectorySeparatorChar)), "sinais"), "cartao-do-ar-x3");
                MedirAnimacao("janela: ligar", delegate { InvokePrivate(arv.BotaoLiga, "OnClick", EventArgs.Empty); }, false);
                EsperarQuadros(600);
                MedirAnimacao("janela: desligar", delegate { InvokePrivate(arv.BotaoLiga, "OnClick", EventArgs.Empty); }, false);
                EsperarQuadros(600);
                MedirAnimacao("janela: +1", delegate { InvokePrivate(arv.BotaoMais, "OnClick", EventArgs.Empty); }, false);
                arv.MandarPendente();
                EsperarQuadros(600);
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
                Ampliar(w, Path.Combine(Path.GetDirectoryName(pasta.TrimEnd(Path.DirectorySeparatorChar)), "sinais"), "widget-x3");
                MedirAnimacao("widget: ligar", delegate { w.Casa.Clicar(2); }, true);
                EsperarQuadros(600);
                MedirAnimacao("widget: desligar", delegate { w.Casa.Clicar(2); }, true);
                EsperarQuadros(600);
                Gravar(pasta, "widget-ligar", w, delegate { w.Casa.Clicar(2); });
                EsperarQuadros(900);
                Gravar(pasta, "widget-mais", w, delegate { w.Casa.Clicar(1); });
                EsperarQuadros(300);
                w.Close();
            }
            ServicoCasa.PararSimulacao();
            return 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RetanguloW r);
        private struct RetanguloW { public int L, T, R, B; }

        private static bool TelaOcupada()
        {
            IntPtr fg = GetForegroundWindow();
            RetanguloW r;
            if (fg == IntPtr.Zero || !GetWindowRect(fg, out r)) return false;
            foreach (Screen s in Screen.AllScreens)
                if (r.L <= s.Bounds.Left && r.T <= s.Bounds.Top && r.R >= s.Bounds.Right && r.B >= s.Bounds.Bottom) return true;
            return false;
        }

        /// <summary>Foto da tela (ou do quadro do widget) ampliada 3x sem suavizar, para conferir o centro dos sinais.</summary>
        private static void Ampliar(Control c, string pasta, string nome)
        {
            Directory.CreateDirectory(pasta);
            Bitmap doWidget = c is WidgetDeBateria ? (Bitmap)Field(c, "_quadro") : null;
            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
            using (Bitmap b = doWidget != null ? new Bitmap(doWidget) : new Bitmap(r.Width, r.Height))
            {
                if (doWidget == null) using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                using (Bitmap z = new Bitmap(b.Width * 3, b.Height * 3))
                {
                    using (Graphics g = Graphics.FromImage(z))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g.DrawImage(b, new Rectangle(0, 0, z.Width, z.Height));
                    }
                    z.Save(Path.Combine(pasta, nome + ".png"), ImageFormat.Png);
                }
            }
        }

        /// <summary>
        /// Clica e, por 800 ms, so mede (sem fotografar, que travaria a thread):
        /// o intervalo entre batidas do relogio de animacao (ou entre quadros do
        /// widget) e quanto cada pintura custou.
        /// </summary>
        private static void MedirAnimacao(string nome, MethodInvoker clicar, bool widget)
        {
            List<long> em = new List<long>();
            List<double> custo = new List<double>();
            if (widget) { WidgetDeBateria.QuadrosEm = em; WidgetDeBateria.QuadrosMs = custo; }
            else { Motion.Tiques = em; CartaoDoArVisao.PinturasMs = custo; }
            clicar();
            EsperarQuadros(800);
            Motion.Tiques = null; CartaoDoArVisao.PinturasMs = null; WidgetDeBateria.QuadrosEm = null; WidgetDeBateria.QuadrosMs = null;
            List<double> dt = new List<double>();
            for (int i = 1; i < em.Count; i++) dt.Add(em[i] - em[i - 1]);
            double max = 0, soma = 0; int longos = 0;
            foreach (double d in dt) { soma += d; if (d > max) max = d; if (d > 40) longos++; }
            double cmax = 0, csoma = 0;
            foreach (double c in custo) { csoma += c; if (c > cmax) cmax = c; }
            Console.WriteLine(nome + ": " + em.Count + " batidas, intervalo medio " + (dt.Count > 0 ? (soma / dt.Count).ToString("0.0") : "-") + " ms, maior " + max.ToString("0") +
                " ms, " + longos + " acima de 40 ms; " + custo.Count + " pinturas, media " + (custo.Count > 0 ? (csoma / custo.Count).ToString("0.0") : "-") + " ms, maior " + cmax.ToString("0.0") + " ms");
        }

        /// <summary>Quadros seguidos da janela (so copia da tela, sem conferir), para pegar o fade de 150 ms.</summary>
        private static void GravarRapido(string pasta, string nome, Control c)
        {
            Rectangle r = c.Bounds;
            List<Bitmap> qs = new List<Bitmap>();
            List<long> ts = new List<long>();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 260)
            {
                Bitmap b = new Bitmap(r.Width, r.Height);
                using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                qs.Add(b); ts.Add(sw.ElapsedMilliseconds);
                Application.DoEvents();
            }
            for (int i = 0; i < qs.Count; i++)
            {
                qs[i].Save(Path.Combine(pasta, nome + "-" + i.ToString("00") + "-" + ts[i] + "ms.png"), ImageFormat.Png);
                qs[i].Dispose();
            }
            Console.WriteLine(nome + ": " + qs.Count + " quadros em 260 ms");
        }

        /// <summary>Clica e fotografa a regiao do controle na tela: um quadro antes e doze a cada ~40 ms.</summary>
        private static void Gravar(string pasta, string nome, Control c, MethodInvoker clicar)
        {
            HashSet<long> vistos = new HashSet<long>();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = -1; i < 12; i++)
            {
                if (i == 0) { if (clicar != null) clicar(); sw.Restart(); }
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
