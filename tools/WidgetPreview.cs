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
    ///   bin\WidgetPreview.exe [--real] [--lista] [--fosco] [--escuro|--claro]
    ///                         [--fundo] [--segundos N] [--foto arquivo.png]
    ///
    /// Sem --real os aparelhos sao de mentira (um carregando, um com pouca
    /// carga), e os leitores nao disputam o receptor com o Mhiagos aberto. Por
    /// padrao o widget fica na frente das janelas, para a foto sair; --fundo
    /// testa o lugar dele de verdade, logo acima dos icones da area de trabalho.
    /// As preferencias vao para uma pasta temporaria, nunca para o widget.ini.
    /// </summary>
    public static class WidgetPreview
    {
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
            bool real = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--real") real = true;
                else if (a == "--lista") cfg.Layout = LayoutDoWidget.Lista;
                else if (a == "--fosco") cfg.Fosco = true;
                else if (a == "--escuro") cfg.Cor = CorDoVidro.Escura;
                else if (a == "--claro") cfg.Cor = CorDoVidro.Clara;
                else if (a == "--fundo") cfg.NaFrente = false;
                else if (a == "--segundos" && i + 1 < args.Length) segundos = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--foto" && i + 1 < args.Length) foto = Path.GetFullPath(args[++i]);
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
                    using (Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                            g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                        b.Save(foto, ImageFormat.Png);
                    }
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

            w.Show();
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
