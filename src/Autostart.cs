using System;
using System.Diagnostics;
using System.Reflection;

namespace MhiagosControl
{
    /// <summary>
    /// Inicializacao automatica via Tarefa Agendada.
    ///
    /// A chave Run do registro nao serve aqui: o aplicativo exige elevacao
    /// (driver de kernel para MSR) e o Windows bloqueia entradas elevadas do
    /// Run. Tarefa agendada com /RL HIGHEST e o mesmo mecanismo que o software
    /// de fabrica usava, e pelo mesmo motivo.
    /// </summary>
    public static class Autostart
    {
        public const string TaskName = "MhiagosControl";

        private static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>
        /// O aplicativo se chamava RiseModePanel. Remove a tarefa antiga, que
        /// apontaria para um executavel que nao existe mais e disputaria o painel.
        /// </summary>
        public static void RemoveLegacyTask()
        {
            try
            {
                int code;
                string output = Run("/query /tn \"RiseModePanel\"", out code);
                if (code != 0) return;
                Run("/delete /tn \"RiseModePanel\" /f", out code);
                Log.Write("tarefa agendada antiga (RiseModePanel) removida");
            }
            catch (Exception ex) { Log.Error("remocao da tarefa antiga", ex); }
        }

        public static bool IsEnabled()
        {
            try
            {
                int code;
                string output = Run("/query /tn \"" + TaskName + "\"", out code);
                return code == 0 && output.IndexOf(TaskName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) { Log.Error("consulta da tarefa agendada", ex); return false; }
        }

        public static bool Enable()
        {
            try
            {
                int code;
                Run("/create /tn \"" + TaskName + "\" /tr \"\\\"" + ExePath + "\\\"\" /sc onlogon /rl highest /f", out code);
                bool ok = (code == 0);
                Log.Write(ok ? "autostart habilitado" : "falha ao habilitar autostart (codigo " + code + ")");
                return ok;
            }
            catch (Exception ex) { Log.Error("criacao da tarefa agendada", ex); return false; }
        }

        public static bool Disable()
        {
            try
            {
                int code;
                Run("/delete /tn \"" + TaskName + "\" /f", out code);
                bool ok = (code == 0);
                Log.Write(ok ? "autostart desabilitado" : "falha ao desabilitar autostart (codigo " + code + ")");
                return ok;
            }
            catch (Exception ex) { Log.Error("remocao da tarefa agendada", ex); return false; }
        }

        /// <summary>A tarefa do software de fabrica disputaria o painel conosco.</summary>
        public static bool OriginalAppTaskEnabled()
        {
            try
            {
                int code;
                string output = Run("/query /tn \"CPU TEMP Monitor StartupTask\" /fo list", out code);
                if (code != 0) return false;
                return output.IndexOf("Desabilitado", StringComparison.OrdinalIgnoreCase) < 0 &&
                       output.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        public static bool DisableOriginalAppTask()
        {
            try
            {
                int code;
                Run("/change /tn \"CPU TEMP Monitor StartupTask\" /disable", out code);
                bool ok = (code == 0);
                Log.Write(ok ? "tarefa do app original desabilitada" : "falha ao desabilitar tarefa original (codigo " + code + ")");
                return ok;
            }
            catch (Exception ex) { Log.Error("desabilitar tarefa original", ex); return false; }
        }

        private static string Run(string args, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                exitCode = p.HasExited ? p.ExitCode : -1;
                return outp;
            }
        }
    }
}
