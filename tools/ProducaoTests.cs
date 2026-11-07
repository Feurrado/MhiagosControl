using System;
using System.Collections.Generic;
using System.Reflection;

namespace MhiagosControl
{
    /// <summary>
    /// Revisao para producao (2.31.0), parte que vale para a edicao enxuta:
    /// thread da interface, reinicio, navegador sem elevacao, PawnIO e versao.
    /// Roda dentro de RegressoesAuditoria, com o log desviado para a pasta temporaria.
    /// </summary>
    public static partial class Tests
    {
        private static void RevisaoDeProducao(string root)
        {
            Secao("revisao para producao: thread da interface, reinicio, navegador e PawnIO");

            MethodInfo main = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            Verdade(main != null && main.GetCustomAttributes(typeof(STAThreadAttribute), false).Length == 1,
                "producao: o Main do aplicativo tem [STAThread]");

            DateTime t0 = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
            List<DateTime> l = new List<DateTime> { t0.AddMinutes(-9), t0.AddMinutes(-5) };
            Verdade(Reinicio.PodeReiniciar(l, t0), "reinicio: dois em 10 min ainda deixa reabrir");
            l.Add(t0.AddMinutes(-1));
            Verdade(!Reinicio.PodeReiniciar(l, t0), "reinicio: tres em 10 min param (sem laco de queda)");

            Uri u;
            Verdade(Navegador.Seguro("https://github.com/x", out u) && !Navegador.Seguro("file:///C:/x.exe", out u) &&
                    !Navegador.Seguro("ms-msdt:/id x", out u) && !Navegador.Seguro("", out u) && !Navegador.Seguro(null, out u),
                "navegador: so http e https com host");
            List<string> abertos = new List<string>();
            Navegador.Interceptar = delegate(string url) { abertos.Add(url); };
            try
            {
                Verdade(Navegador.Abrir("https://github.com/Feurrado/MhiagosControl") && !Navegador.Abrir("file:///C:/x.exe") && abertos.Count == 1,
                    "navegador: endereco bom abre, ruim nao");
            }
            finally { Navegador.Interceptar = null; }

            List<SensorEntry> sem = new List<SensorEntry>();
            Verdade(TrayContext.FaltaPawnIo(false, false, sem) && !TrayContext.FaltaPawnIo(true, false, sem) && !TrayContext.FaltaPawnIo(false, true, sem),
                "pawnio: avisa so sem HWiNFO, sem PawnIO e sem temperatura de CPU");
            Verdade(T.AvisoDoMotor(false) != T.AvisoDoMotor(true) &&
                    T.AvisoDoMotor(false).IndexOf("instalador oficial", StringComparison.OrdinalIgnoreCase) < 0,
                "pawnio: aviso do Sobre diferente com e sem o PawnIO");

            Igual(AppInfo.Versao, MhiagosSetup.Setup.Versao, "versao: instalador e app dizem a mesma");
            Igual(AppInfo.Versao + ".0", typeof(Program).Assembly.GetName().Version.ToString(), "versao: o executavel leva a versao do app");
            Verdade(!Log.Path.StartsWith(Paths.DataDir, StringComparison.OrdinalIgnoreCase), "testes: o log nao vai para o log.txt de quem usa o app");
        }
    }
}
