using System;
using System.IO;
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

            RegistrarNome();

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

        internal const string NomeVisivel = "Mhiagos Control";

        internal static string ChaveDoRegistro { get { return @"Software\Classes\AppUserModelId\" + AppId; } }

        /// <summary>
        /// Nome e icone da identidade do processo, para as notificacoes.
        ///
        /// Com a AUMID explicita e sem atalho que a carregue, o Windows nao
        /// sabe como chamar o app e poe a propria AUMID no cabecalho do aviso
        /// ("Feurrado.MhiagosControl"). A chave AppUserModelId do usuario, com
        /// DisplayName e IconUri, e o jeito documentado de nomear um app sem
        /// pacote; fica no HKCU, sem pedir administrador.
        /// </summary>
        internal static void RegistrarNome()
        {
            try
            {
                string ico = Path.Combine(Paths.DataDir, "notificacao.ico");
                byte[] bytes = Assets.AppIconBytes();
                if (bytes != null && (!File.Exists(ico) || new FileInfo(ico).Length != bytes.Length))
                {
                    Directory.CreateDirectory(Paths.DataDir);
                    File.WriteAllBytes(ico, bytes);
                }
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ChaveDoRegistro))
                {
                    if (k == null) return;
                    if (!NomeVisivel.Equals(k.GetValue("DisplayName"))) k.SetValue("DisplayName", NomeVisivel);
                    if (bytes != null && !ico.Equals(k.GetValue("IconUri"))) k.SetValue("IconUri", ico);
                }
            }
            catch (Exception ex) { Log.Error("nome do app nas notificacoes", ex); }
        }
    }
}
