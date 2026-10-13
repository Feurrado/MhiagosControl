using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Abre o widget de bateria sozinho, sem o resto do aplicativo e sem
    /// elevacao, para ajustar o vidro olhando a tela. O desfoque vem do
    /// Windows e so aparece numa janela de verdade sobre um papel de parede de
    /// verdade; o bitmap dos testes mostra o desenho, nao o vidro.
    ///
    ///   powershell -ExecutionPolicy Bypass -File .\tools\build-widget-preview.ps1
    ///   bin\WidgetPreview.exe [--real] [--lista] [--fosco|--desfoque] [--escuro|--claro]
    ///                         [--fundo] [--listras]
    ///                         [--segundos N] [--foto arquivo.png] [--alternar-carga N]
    ///
    /// Sem --real os aparelhos sao de mentira (um carregando, um com pouca
    /// carga), e os leitores nao disputam o receptor com o Mhiagos aberto. Por
    /// padrao o widget fica na frente das janelas, para a foto sair; --fundo
    /// testa o lugar dele de verdade, logo acima dos icones da area de trabalho.
    /// As preferencias vao para uma pasta temporaria, nunca para o widget.ini.
    /// --alternar-carga liga e desliga o cabo dos aparelhos de mentira a cada
    /// N segundos (3 sem numero), para ver a transicao de entrada e saida.
    /// </summary>
    public static class WidgetPreview
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        private const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        /// <summary>
        /// A tela como o DWM compoe, com as janelas em camadas. O CopyFromScreen
        /// do .NET recusa SourceCopy junto com CaptureBlt, e sem o CaptureBlt a
        /// janela em camadas nao sai na foto.
        /// </summary>
        private static Bitmap Fotografar(Rectangle r)
        {
            Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            IntPtr tela = GetDC(IntPtr.Zero);
            try
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    IntPtr hdc = g.GetHdc();
                    try { BitBlt(hdc, 0, 0, r.Width, r.Height, tela, r.X, r.Y, SRCCOPY | CAPTUREBLT); }
                    finally { g.ReleaseHdc(hdc); }
                }
            }
            finally { ReleaseDC(IntPtr.Zero, tela); }
            return b;
        }

        /// <summary>
        /// Um quadro de listras coloridas e letras grandes logo atras do widget:
        /// com isso a foto mostra se o desfoque existe, qualquer que seja o
        /// papel de parede ou a janela que estiver ali.
        /// </summary>
        private static void Listras(Form widget)
        {
            Form f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Bounds = Rectangle.Inflate(widget.Bounds, 30, 30);
            f.Paint += delegate(object s, PaintEventArgs e)
            {
                Color[] cores = { Color.Crimson, Color.Gold, Color.DodgerBlue, Color.LimeGreen, Color.White, Color.Black };
                for (int i = 0; i * 18 < f.Width + f.Height; i++)
                    using (Pen p = new Pen(cores[i % cores.Length], 9))
                        e.Graphics.DrawLine(p, i * 18, 0, i * 18 - f.Height, f.Height);
                using (Font fonte = new Font("Segoe UI", 28f, FontStyle.Bold))
                    e.Graphics.DrawString("MHIAGOS 123", fonte, Brushes.Black, 36, f.Height / 2 - 24);
            };
            widget.FormClosed += delegate { f.Close(); };
            // sem dono: janela com dono fica sempre acima dele, e as listras
            // tem de ficar atras do widget. Sempre na frente tambem, senao o
            // bloqueio de primeiro plano deixa o quadro atras da janela ativa;
            // depois o widget sobe acima dele.
            f.TopMost = true;
            f.Show();
            SetWindowPos(widget.Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            T.Language = T.Detect();

            ConfigDoWidget.Caminho = Path.Combine(Path.GetTempPath(), "MhiagosControl-widget-preview.ini");
            // o log da previa nao se mistura com o do aplicativo
            typeof(Log).GetField("_path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .SetValue(null, Path.Combine(Path.GetTempPath(), "MhiagosControl-widget-preview.log"));
            ConfigDoWidget cfg = new ConfigDoWidget();
            cfg.Ativo = true;
            cfg.NaFrente = true;
            int segundos = 4;
            string foto = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "widget-preview.png");
            bool real = false, listras = false;
            int alternar = 0;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--real") real = true;
                else if (a == "--lista") cfg.Layout = LayoutDoWidget.Lista;
                else if (a == "--fosco") { cfg.Fosco = true; cfg.Nitido = false; }
                else if (a == "--desfoque") cfg.Nitido = false;
                else if (a == "--escuro") cfg.Cor = CorDoVidro.Escura;
                else if (a == "--claro") cfg.Cor = CorDoVidro.Clara;
                else if (a == "--fundo") cfg.NaFrente = false;
                else if (a == "--listras") listras = true;
                else if (a == "--segundos" && i + 1 < args.Length) segundos = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--foto" && i + 1 < args.Length) foto = Path.GetFullPath(args[++i]);
                else if (a == "--alternar-carga")
                {
                    alternar = 3;
                    int n;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { alternar = n; i++; }
                }
            }

            ManualResetEvent parar = new ManualResetEvent(false);
            if (real) Perifericos.Iniciar(parar);
            else
            {
                Perifericos.PublicarLogitech(new Periferico[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, false, 48) });
                Perifericos.PublicarShark(Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 46, true));
                Perifericos.PublicarBluetooth(new Periferico[] {
                    Periferico.Lido("WH-1000XM4", TipoPeriferico.Outro, FontePeriferico.Bluetooth, 18, false) });
            }

            WidgetDeBateria w = new WidgetDeBateria(cfg);
            w.BateriasPedidas += delegate { Console.WriteLine("clique: abriria a pagina Baterias"); };
            w.OcultarPedido += delegate { w.Close(); };
            w.FormClosed += delegate { Application.ExitThread(); };

            System.Windows.Forms.Timer foto1 = new System.Windows.Forms.Timer();
            foto1.Interval = real ? 3000 : 1500;
            foto1.Tick += delegate
            {
                foto1.Stop();
                try
                {
                    Rectangle r = Rectangle.Inflate(w.Bounds, 40, 40);
                    Directory.CreateDirectory(Path.GetDirectoryName(foto));
                    using (Bitmap b = Fotografar(r)) b.Save(foto, ImageFormat.Png);
                    Console.WriteLine("foto: " + foto);
                }
                catch (Exception ex) { Console.WriteLine("foto falhou: " + ex.Message); }
                Console.WriteLine(w.Diagnostico());
            };

            System.Windows.Forms.Timer fim = new System.Windows.Forms.Timer();
            fim.Interval = Math.Max(2, segundos) * 1000;
            fim.Tick += delegate
            {
                fim.Stop();
                Console.WriteLine("no fim: " + w.Diagnostico());
                w.Close();
            };

            bool cabo = true;
            System.Windows.Forms.Timer carga = new System.Windows.Forms.Timer();
            carga.Interval = Math.Max(1, alternar) * 1000;
            carga.Tick += delegate
            {
                // o headset vai ao contrario do mouse: uma entrada e uma saida por vez
                cabo = !cabo;
                Perifericos.PublicarLogitech(new Periferico[] {
                    Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, !cabo, 48) });
                Perifericos.PublicarShark(Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 46, cabo));
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " cabo: X11 " + (cabo ? "sim" : "nao") + ", G325 " + (cabo ? "nao" : "sim"));
            };

            w.Show();
            if (listras) Listras(w);
            if (alternar > 0 && !real) carga.Start();
            foto1.Start();
            fim.Start();
            Application.Run();

            parar.Set();
            Perifericos.Esperar(2000);
            string log = Log.Path;
            Console.WriteLine("log: " + log);
            return 0;
        }
    }
}
