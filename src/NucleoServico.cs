using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace MhiagosControl
{
    /// <summary>
    /// O app roda sem administrador; so o nucleo (--nucleo) precisa dele, para os
    /// drivers dos sensores. Quem o deixa de pe e uma tarefa agendada com
    /// privilegio mais alto, criada uma vez com o UAC (pelo instalador ou por
    /// --instalar-nucleo): ela sobe no logon e, se o nucleo cair, o app a dispara
    /// de novo sem perguntar nada.
    /// </summary>
    internal static partial class Nucleo
    {
        /// <summary>Cria a tarefa do nucleo e o inicia. So faz sentido elevado.</summary>
        public const string ArgInstalar = "--instalar-nucleo";
        /// <summary>Desliga a tarefa do software de fabrica, que exige administrador.</summary>
        public const string ArgDesligarOriginal = "--desligar-original";
        /// <summary>Evento que o nucleo espera para sair (o instalador usa antes de trocar os arquivos).</summary>
        public const string EventoDeSaida = "Local\\MhiagosControl_Nucleo_Sair";

        /// <summary>Os sensores deste processo vem do nucleo (so a CriarSensores liga; os testes nao).</summary>
        public static bool EmUso;

        private static string Exe { get { return Assembly.GetExecutingAssembly().Location; } }

        /// <summary>
        /// O nucleo desta sessao esta de pe? O mutex dele e criado elevado: abrir
        /// sem elevacao pode ser recusado, o que tambem quer dizer que existe.
        /// </summary>
        public static bool Rodando
        {
            get
            {
                try { using (Mutex m = Mutex.OpenExisting(MutexDoNucleo)) return true; }
                catch (UnauthorizedAccessException) { return true; }
                catch (WaitHandleCannotBeOpenedException) { return false; }
                catch (Exception ex) { Log.Error("nucleo: consulta", ex); return false; }
            }
        }

        /// <summary>
        /// Se o app vai ler pelo nucleo e ele nao esta de pe, dispara a tarefa e
        /// espera ate o limite. Falso quando nao ha nucleo (sem tarefa ou ela falhou).
        /// </summary>
        public static bool Garantir(int esperaMs)
        {
            if (!UsarRemoto || Rodando) return true;
            if (!Autostart.NucleoInstalado())
            {
                Log.Write("nucleo: tarefa nao existe; os sensores de baixo nivel ficam fora ate ativar");
                return false;
            }
            if (!Autostart.RodarNucleo()) return false;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < esperaMs)
            {
                if (Rodando) { Log.Write("nucleo: de pe em " + sw.ElapsedMilliseconds + " ms"); return true; }
                Thread.Sleep(100);
            }
            Log.Write("nucleo: a tarefa rodou, mas o nucleo nao respondeu em " + esperaMs + " ms");
            return Rodando;
        }

        /// <summary>
        /// Roda este executavel elevado com um argumento (o Windows pergunta) e
        /// espera ele terminar. Falso se a pessoa recusou ou se falhou.
        /// </summary>
        public static bool Elevar(string argumento, int esperaMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Exe, argumento);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(esperaMs)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch (Win32Exception ex)
            {
                // 1223: a pessoa disse nao no UAC.
                Log.Write("elevacao recusada (" + argumento + "): " + ex.NativeErrorCode);
                return false;
            }
            catch (Exception ex) { Log.Error("elevacao: " + argumento, ex); return false; }
        }

        /// <summary>--instalar-nucleo: ja elevado, cria a tarefa, migra a tarefa antiga e sobe o nucleo.</summary>
        public static int Instalar()
        {
            if (!Elevado()) { Log.Write("instalar nucleo: sem elevacao"); return 2; }
            string erro;
            if (!Autostart.CriarTarefaDoNucleo(out erro))
            {
                Log.Write("instalar nucleo: " + erro);
                return 1;
            }
            Autostart.MigrarTarefaAntiga();
            Autostart.RodarNucleo();
            return 0;
        }
    }

    public static partial class T
    {
        public static string NucleoFaltando
        {
            get
            {
                return P("Os sensores de temperatura e potência precisam do núcleo do Mhiagos, que roda como administrador. Clique para ativar; o Windows pergunta uma vez.",
                         "Temperature and power sensors need the Mhiagos core, which runs as administrator. Click to enable; Windows asks once.");
            }
        }
        public static string NucleoAtivado { get { return P("Núcleo ativado. Os sensores voltam em instantes.", "Core enabled. Sensors come back in a moment."); } }
        public static string NucleoRecusado { get { return P("O núcleo não foi ativado. Os sensores de baixo nível ficam fora.", "The core was not enabled. Low-level sensors stay off."); } }
    }
}
