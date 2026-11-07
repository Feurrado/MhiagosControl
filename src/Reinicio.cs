using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    /// <summary>
    /// Reabre o app depois de uma excecao que derruba o processo.
    ///
    /// Uma excecao solta em thread de fundo termina o .NET, e com ele o envio
    /// ao cooler: o mostrador apagava ate o proximo logon, porque a tarefa
    /// agendada so roda ao entrar no Windows. O processo novo herda a elevacao
    /// (sem UAC) e espera o mutex do antigo soltar.
    ///
    /// Com freio: no maximo MaxReinicios em JanelaMin minutos. Uma falha que
    /// se repete no arranque nao vira laco.
    /// </summary>
    internal static class Reinicio
    {
        public const string Argumento = "--reiniciado";
        internal const int MaxReinicios = 3;
        internal const int JanelaMin = 10;

        /// <summary>Arquivo com os instantes dos reinicios recentes; os testes trocam.</summary>
        internal static string Arquivo;

        private static string Caminho { get { return Arquivo ?? Path.Combine(Paths.DataDir, "reinicios.txt"); } }

        internal static bool PodeReiniciar(IList<DateTime> anteriores, DateTime agoraUtc)
        {
            int recentes = 0;
            foreach (DateTime t in anteriores)
                if ((agoraUtc - t).TotalMinutes < JanelaMin) recentes++;
            return recentes < MaxReinicios;
        }

        /// <summary>Chamado no UnhandledException terminal. Nunca lanca.</summary>
        public static void Pedir()
        {
            try
            {
                DateTime agora = DateTime.UtcNow;
                List<DateTime> anteriores = Ler();
                if (!PodeReiniciar(anteriores, agora))
                {
                    Log.Write("reinicio automatico suspenso: " + MaxReinicios + " em " + JanelaMin + " min");
                    return;
                }
                anteriores.Add(agora);
                Gravar(anteriores, agora);

                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, Argumento);
                psi.UseShellExecute = false;   // herda o token elevado, sem UAC
                Process.Start(psi);
                Log.Write("reinicio automatico pedido");
            }
            catch (Exception ex) { Log.Error("reinicio automatico", ex); }
        }

        private static List<DateTime> Ler()
        {
            List<DateTime> l = new List<DateTime>();
            try
            {
                if (!File.Exists(Caminho)) return l;
                foreach (string linha in File.ReadAllLines(Caminho))
                {
                    long ticks;
                    if (long.TryParse(linha.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks > 0)
                        l.Add(new DateTime(ticks, DateTimeKind.Utc));
                }
            }
            catch (Exception ex) { Log.Error("ler reinicios", ex); }
            return l;
        }

        private static void Gravar(List<DateTime> l, DateTime agoraUtc)
        {
            List<string> linhas = new List<string>();
            foreach (DateTime t in l)
                if ((agoraUtc - t).TotalMinutes < JanelaMin * 6) linhas.Add(t.Ticks.ToString(CultureInfo.InvariantCulture));
            File.WriteAllLines(Caminho, linhas.ToArray());
        }
    }
}
