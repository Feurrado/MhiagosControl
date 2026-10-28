using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    public static partial class Tests
    {
        /// <summary>
        /// Tests.exe --clima-fluxo "cidade": o fluxo do app de verdade, com rede de
        /// verdade - contexto da interface preso como na bandeja (antes de qualquer
        /// controle), aba Ajustes da Casa, busca, escolha do primeiro resultado e a
        /// leitura da temperatura. casa.ini vai para uma pasta temporaria.
        /// </summary>
        private static int FluxoDoClima(string cidade)
        {
            Motion.PrenderNaInterface();   // como TrayContext: antes de qualquer controle
            string tmp = Path.Combine(Path.GetTempPath(), "mhiagos-clima-fluxo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            PreferenciasCasa.Arquivo = Path.Combine(tmp, "casa.ini");
            ServicoCasa.Pref = new PreferenciasCasa();
            int erros = 0;
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Console.WriteLine("EXCECAO: " + e.ExceptionObject); };
            using (Form f = new Form())
            {
                f.ShowInTaskbar = false; f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-30000, -30000); f.Size = new Size(900, 800);
                AjustesDaCasa aj = new AjustesDaCasa();
                aj.Bounds = new Rectangle(0, 0, 860, 760);
                f.Controls.Add(aj);
                f.Show();
                aj.Arranjar(860);
                SecaoDoClima sc = aj.SecaoClima;
                ((CampoDeTexto)Field(sc, "_busca")).Texto = cidade;
                sc.Buscar();
                for (int k = 0; k < 500 && sc.Ocupado; k++) EsperarQuadros(20);
                EsperarQuadros(100);
                Console.WriteLine("resultados: " + sc.Lugares.Count);
                foreach (LugarDoClima l in sc.Lugares) Console.WriteLine("  " + l.Descricao);
                if (sc.Lugares.Count == 0) { erros++; }
                else
                {
                    sc.Escolher(sc.Lugares[0]);
                    for (int k = 0; k < 500 && Clima.Atual == null; k++) EsperarQuadros(20);
                    Console.WriteLine("cidade: " + Clima.Cidade + "  agora: " + Clima.Texto() + "  (" + Clima.Origem() + ")");
                    if (Clima.Atual == null) erros++;
                }
                f.Close();
            }
            return erros;
        }
    }
}
