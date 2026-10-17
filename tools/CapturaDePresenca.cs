using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Fotografa a faixa da Visao geral e o widget durante a entrada e a saida
    /// de um aparelho Bluetooth: Tests.exe --captura-presenca C:\pasta
    /// Para olhar, nao e conferencia automatica.
    /// </summary>
    public static partial class Tests
    {
        private static int CapturaDePresenca(string[] args)
        {
            string pasta = args[1];
            Directory.CreateDirectory(pasta);
            Ui.AnimacoesForcadas = true;
            Periferico g = Periferico.Lido("G325", TipoPeriferico.Headset, FontePeriferico.GHub, 72, false);
            Periferico x = Periferico.Lido("X11", TipoPeriferico.Mouse, FontePeriferico.AttackShark, 46, false);
            Periferico marcador = Periferico.Sem("Bluetooth", TipoPeriferico.Outro, FontePeriferico.Bluetooth, EstadoPeriferico.NenhumConectado);
            Periferico bt = Periferico.Lido("WH-1000XM4", TipoPeriferico.Outro, FontePeriferico.Bluetooth, 18, false);

            using (Form f = new Form())
            {
                f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-20000, -20000);
                f.ClientSize = new Size(760, 110);
                PeripheralStrip s = new PeripheralStrip();
                s.SetBounds(0, 0, 760, 110);
                f.Controls.Add(s);
                f.Show();
                s.Atualizar(new Periferico[] { g, x, marcador });
                PresencaAnimada p = (PresencaAnimada)typeof(PeripheralStrip).GetField("_entradas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
                s.Atualizar(new Periferico[] { g, x, bt });
                Quadros(f, p, pasta, "faixa-conecta");
                s.Atualizar(new Periferico[] { g, x, marcador });
                Quadros(f, p, pasta, "faixa-desconecta");
            }
            return 0;
        }

        private static void Quadros(Form f, PresencaAnimada p, string pasta, string nome)
        {
            int n = 0;
            Bitmap folha = new Bitmap(760, 110 * 6);
            using (Graphics g = Graphics.FromImage(folha))
                foreach (int ms in new int[] { 0, 40, 80, 120, 170, 260 })
                {
                    p.Avancar(ms);
                    using (Bitmap b = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                    {
                        f.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size));
                        g.DrawImage(b, 0, 110 * n++);
                    }
                }
            folha.Save(Path.Combine(pasta, nome + ".png"), ImageFormat.Png);
            folha.Dispose();
        }
    }
}
