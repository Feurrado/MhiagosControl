using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MhiagosControl
{
    internal static class TaskbarIdentity
    {
        internal const string AppId = "Feurrado.MhiagosControl";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item, IntPtr unused);

        // Antes de criar janelas: a bandeja e o inicio automatico nao devem
        // deixar a identidade do botao depender do processo que abriu o app.
        internal static void Initialize()
        {
            try { Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId)); }
            catch (Exception ex) { Log.Error("identidade da barra de tarefas", ex); }

            try
            {
                // Atualiza somente este executavel, que pode ter sido substituido
                // pelo instalador. Nao limpa o cache global nem reinicia o Explorer.
                const int SHCNE_UPDATEITEM = 0x2000;
                const uint SHCNF_PATHW_FLUSHNOWAIT = 0x2005;
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW_FLUSHNOWAIT,
                    Assembly.GetExecutingAssembly().Location, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Error("atualizacao do icone no Windows", ex); }
        }
    }
}
