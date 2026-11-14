using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace MhiagosControl
{
    /// <summary>
    /// Inicio com o Windows e a tarefa do nucleo.
    ///
    /// Desde a 3.0 o app roda sem administrador: ele entra pela chave Run do
    /// usuario, como qualquer programa. Quem precisa de administrador e o nucleo
    /// (os drivers dos sensores), e ele sobe por uma tarefa agendada com
    /// privilegio mais alto, criada uma vez com o UAC. Ate a 2.x a tarefa
    /// "MhiagosControl" abria o proprio app elevado; ela e migrada quando ha
    /// elevacao para isso (instalador ou --instalar-nucleo).
    /// </summary>
    public static class Autostart
    {
        /// <summary>A tarefa da 2.x, que abria o app inteiro como administrador.</summary>
        public const string TaskName = "MhiagosControl";
        public const string TarefaDoNucleo = "MhiagosControl Nucleo";
        internal const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValorRun = "MhiagosControl";

        private static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>O que vai na chave Run: o executavel entre aspas.</summary>
        internal static string LinhaDoRun(string exe) { return "\"" + exe + "\""; }

        /// <summary>Os argumentos do schtasks que criam a tarefa do nucleo.</summary>
        internal static string CriacaoDoNucleo(string exe)
        {
            return "/create /tn \"" + TarefaDoNucleo + "\" /tr \"\\\"" + exe + "\\\" " + Nucleo.Argumento +
                   "\" /sc onlogon /rl highest /f";
        }

        private static bool TarefaExiste(string nome)
        {
            int code;
            string output = Run("/query /tn \"" + nome + "\"", out code);
            return code == 0 && output.IndexOf(nome, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string LerRun()
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ChaveRun))
                return k == null ? null : k.GetValue(ValorRun) as string;
        }

        /// <summary>Liga pela chave Run, ou pela tarefa antiga que ainda nao foi migrada.</summary>
        public static bool IsEnabled()
        {
            try
            {
                if (!string.IsNullOrEmpty(LerRun())) return true;
                return TarefaExiste(TaskName);
            }
            catch (Exception ex) { Log.Error("consulta do inicio automatico", ex); return false; }
        }

        /// <summary>
        /// Mensagem pronta da ultima recusa do Enable, ou null quando a falha
        /// esta no log.
        /// </summary>
        public static string UltimaRecusa { get; private set; }

        public static bool Enable()
        {
            UltimaRecusa = null;
            try
            {
                // Sem elevacao: a chave Run nao da administrador a ninguem, entao a
                // pasta do executavel nao precisa ser protegida para isto.
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ChaveRun))
                    k.SetValue(ValorRun, LinhaDoRun(ExePath));
                Log.Write("autostart habilitado (chave Run)");
                return true;
            }
            catch (Exception ex) { Log.Error("inicio automatico pela chave Run", ex); return false; }
        }

        public static bool Disable()
        {
            try
            {
                // A tarefa antiga abriria o app no proximo logon do mesmo jeito. Ela sai
                // antes da chave Run: a migracao recria a chave quando a acha.
                if (TarefaExiste(TaskName))
                {
                    if (Nucleo.Elevado()) ApagarTarefa(TaskName);
                    else Nucleo.Elevar(Nucleo.ArgInstalar, 60000);   // a migracao apaga a antiga
                    if (TarefaExiste(TaskName)) { Log.Write("autostart: a tarefa antiga continua (sem elevacao)"); return false; }
                }
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ChaveRun, true))
                    if (k != null && k.GetValue(ValorRun) != null) k.DeleteValue(ValorRun, false);
                Log.Write("autostart desabilitado");
                return true;
            }
            catch (Exception ex) { Log.Error("desligar o inicio automatico", ex); return false; }
        }

        private static bool ApagarTarefa(string nome)
        {
            int code;
            Run("/delete /tn \"" + nome + "\" /f", out code);
            return code == 0;
        }

        // ---------------- tarefa do nucleo ----------------

        public static bool NucleoInstalado()
        {
            try { return TarefaExiste(TarefaDoNucleo); }
            catch (Exception ex) { Log.Error("consulta da tarefa do nucleo", ex); return false; }
        }

        /// <summary>Dispara a tarefa: funciona sem elevacao, e o Windows nao pergunta (a tarefa ja foi autorizada).</summary>
        public static bool RodarNucleo()
        {
            try
            {
                int code;
                Run("/run /tn \"" + TarefaDoNucleo + "\"", out code);
                if (code != 0) Log.Write("nucleo: a tarefa nao rodou (codigo " + code + ")");
                return code == 0;
            }
            catch (Exception ex) { Log.Error("rodar a tarefa do nucleo", ex); return false; }
        }

        /// <summary>
        /// Cria a tarefa do nucleo. Exige elevacao e uma pasta que so administrador
        /// grava: a tarefa roda este executavel como administrador e sem UAC.
        /// </summary>
        public static bool CriarTarefaDoNucleo(out string erro)
        {
            erro = null;
            string pasta = System.IO.Path.GetDirectoryName(ExePath);
            if (!PastaProtegida(pasta))
            {
                erro = string.Format(T.AutostartUnsafeFolder, pasta);
                return false;
            }
            int code;
            Run(CriacaoDoNucleo(ExePath), out code);
            if (code != 0) erro = "schtasks " + code;
            Log.Write(code == 0 ? "tarefa do nucleo criada" : "falha ao criar a tarefa do nucleo (codigo " + code + ")");
            return code == 0;
        }

        /// <summary>A tarefa da 2.x sai; se ela existia, o inicio com o Windows passa para a chave Run.</summary>
        public static void MigrarTarefaAntiga()
        {
            try
            {
                if (!TarefaExiste(TaskName)) return;
                if (ApagarTarefa(TaskName))
                {
                    Log.Write("tarefa antiga migrada para a chave Run");
                    Enable();
                }
            }
            catch (Exception ex) { Log.Error("migrar a tarefa antiga", ex); }
        }

        public static bool RemoverTarefaDoNucleo()
        {
            try { return !TarefaExiste(TarefaDoNucleo) || ApagarTarefa(TarefaDoNucleo); }
            catch (Exception ex) { Log.Error("remover a tarefa do nucleo", ex); return false; }
        }

        private const string OriginalTask = "CPU TEMP Monitor StartupTask";

        /// <summary>
        /// A tarefa do software de fabrica disputaria o painel conosco.
        ///
        /// Le o XML da tarefa, e nao o relatorio em texto: o campo de estado
        /// vem traduzido, e a versao anterior procurava os literais
        /// "Desabilitado" e "Disabled". Num Windows em qualquer outro idioma
        /// uma tarefa desativada era lida como ativa, e o aplicativo cobrava o
        /// usuario para desativar o que ja estava desativado.
        ///
        /// No XML, &lt;Enabled&gt; so aparece quando vale false - habilitado e o
        /// padrao do esquema e fica implicito. Conferido nesta maquina: das 45
        /// ocorrencias do elemento em todas as tarefas do sistema, 45 eram
        /// false.
        /// </summary>
        public static bool OriginalAppTaskEnabled()
        {
            try
            {
                int code;
                string xml = Run("/query /tn \"" + OriginalTask + "\" /xml ONE", out code);
                if (code != 0) return false;              // nao existe: nada a disputar
                return !DesabilitadaNoXml(xml);
            }
            catch (Exception ex) { Log.Error("consulta da tarefa original", ex); return false; }
        }

        /// <summary>
        /// Procura o desligamento apenas dentro de &lt;Settings&gt;.
        ///
        /// Um gatilho tambem pode trazer &lt;Enabled&gt;false&lt;/Enabled&gt;, e uma
        /// tarefa ativa com um gatilho desligado seria lida como desativada se
        /// a busca fosse no documento inteiro.
        ///
        /// Sem XmlDocument de proposito: exigiria referenciar System.Xml no
        /// build por uma unica consulta, e o recorte por marcador resolve o
        /// mesmo problema. O schtasks escreve ASCII no cano de saida, apesar de
        /// a declaracao do XML dizer UTF-16 - medido, nao suposto.
        /// </summary>
        private static bool DesabilitadaNoXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return false;

            int ini = xml.IndexOf("<Settings>", StringComparison.OrdinalIgnoreCase);
            if (ini < 0) return false;
            int fim = xml.IndexOf("</Settings>", ini, StringComparison.OrdinalIgnoreCase);
            if (fim < 0) fim = xml.Length;

            string settings = xml.Substring(ini, fim - ini);
            return settings.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool DisableOriginalAppTask()
        {
            try
            {
                int code;
                Run("/change /tn \"" + OriginalTask + "\" /disable", out code);
                bool ok = (code == 0);
                Log.Write(ok ? "tarefa do app original desabilitada" : "falha ao desabilitar tarefa original (codigo " + code + ")");
                return ok;
            }
            catch (Exception ex) { Log.Error("desabilitar tarefa original", ex); return false; }
        }

        /// <summary>
        /// Executa o schtasks e devolve saida e codigo.
        ///
        /// As duas saidas sao lidas por evento, e nao com ReadToEnd em
        /// sequencia. Ler uma ate o fim e so depois a outra trava se o processo
        /// filho encher o buffer da que ficou esperando: ele bloqueia na
        /// escrita, nos bloqueamos na leitura da outra, e ninguem sai do lugar.
        /// A saida do schtasks e curta demais para isso acontecer hoje, o que
        /// so torna a falha pior - apareceria numa maquina qualquer, um dia,
        /// sem jeito de reproduzir.
        /// </summary>
        /// <summary>
        /// Verdadeiro quando nenhum usuario comum consegue alterar a pasta do
        /// executavel, a subpasta engine\, o proprio executavel ou trocar a
        /// pasta inteira pela de cima.
        /// </summary>
        internal static bool PastaProtegida(string pasta)
        {
            try
            {
                if (string.IsNullOrEmpty(pasta) || !System.IO.Directory.Exists(pasta)) return false;
                const FileSystemRights Escrita =
                    FileSystemRights.WriteData | FileSystemRights.AppendData |
                    FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
                const FileSystemRights TrocaDeFilho =
                    FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

                if (UsuarioComumPode(System.IO.Directory.GetAccessControl(pasta), Escrita)) return false;

                string engine = System.IO.Path.Combine(pasta, "engine");
                if (System.IO.Directory.Exists(engine) &&
                    UsuarioComumPode(System.IO.Directory.GetAccessControl(engine), Escrita)) return false;

                string exe = System.IO.Path.Combine(pasta, System.IO.Path.GetFileName(ExePath));
                if (System.IO.File.Exists(exe) &&
                    UsuarioComumPode(System.IO.File.GetAccessControl(exe), Escrita)) return false;

                System.IO.DirectoryInfo pai = System.IO.Directory.GetParent(pasta);
                if (pai != null && UsuarioComumPode(pai.GetAccessControl(), TrocaDeFilho)) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("verificacao da pasta do executavel", ex);
                return false;
            }
        }

        private static bool UsuarioComumPode(FileSystemSecurity acl, FileSystemRights direitos)
        {
            SecurityIdentifier eu = WindowsIdentity.GetCurrent().User;
            SecurityIdentifier[] comuns =
            {
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                eu,
            };

            // O dono muda as permissoes quando quiser, com ou sem regra explicita.
            IdentityReference dono = acl.GetOwner(typeof(SecurityIdentifier));
            if (dono != null && eu != null && dono.Equals(eu)) return true;

            foreach (FileSystemAccessRule regra in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (regra.AccessControlType != AccessControlType.Allow) continue;
                if ((regra.FileSystemRights & direitos) == 0) continue;
                // Regra que so vale para os filhos nao da escrita nesta pasta.
                if ((regra.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                foreach (SecurityIdentifier sid in comuns)
                    if (sid != null && sid.Equals(regra.IdentityReference)) return true;
            }
            return false;
        }

        private static string Run(string args, out int exitCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            StringBuilder sb = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                DataReceivedEventHandler colher = delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;              // null marca o fim do fluxo
                    lock (sb) sb.AppendLine(e.Data);
                };
                p.OutputDataReceived += colher;
                p.ErrorDataReceived += colher;

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(15000))
                {
                    Log.Write("schtasks nao respondeu em 15 s: " + args);
                    try { p.Kill(); } catch { }
                    exitCode = -1;
                    lock (sb) return sb.ToString();
                }

                // Sem argumento, espera tambem o esvaziamento dos dois fluxos -
                // com timeout, WaitForExit nao garante isso.
                p.WaitForExit();
                exitCode = p.ExitCode;
                lock (sb) return sb.ToString();
            }
        }
    }
}
