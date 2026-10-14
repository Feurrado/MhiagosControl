using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MhiagosSetup
{
    /// <summary>
    /// Instalador do Mhiagos Control.
    ///
    /// Um executavel so, com os arquivos do aplicativo embutidos como recursos.
    /// Compila com o csc.exe que vem no Windows, como o resto do projeto - a
    /// alternativa seria depender do Inno Setup ou do WiX, e o projeto inteiro
    /// existe sem baixar ferramenta nenhuma.
    ///
    /// O MESMO executavel desinstala: durante a instalacao ele se copia para a
    /// pasta de destino como uninstall.exe, e e esse caminho que vai para a
    /// chave de desinstalacao do Windows.
    ///
    /// ATENCAO ao que ele carrega dentro: engine\api-ms-win-core-sysinfo-825-64.dll
    /// e a biblioteca cliente do HWiNFO, comercial, licenciada ao fabricante do
    /// cooler e nao a este projeto. Um instalador gerado com ela embutida serve
    /// a uso pessoal na propria maquina e NAO pode ser publicado nem
    /// redistribuido. Veja build-installer.ps1.
    /// </summary>
    public static class Setup
    {
        public const string AppName = "Mhiagos Control";
        public const string Versao = "2.21.0";
        public const string TaskName = "MhiagosControl";
        public const string ProcName = "MhiagosControl";
        private const string UninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MhiagosControl";

        /// <summary>Pacote oficial do RivaTuner Statistics Server no winget.</summary>
        public const string PacoteRtss = "Guru3D.RTSS";

        /// <summary>
        /// Runtime do Visual C++, exigido pelo RTSS e nao declarado no pacote dele.
        ///
        /// Sem ele o RTSSHooksLoader64 para em "VCRUNTIME140_1.dll nao foi
        /// encontrado" e o RTSS instalado nunca publica nada.
        /// </summary>
        public const string PacoteRuntime = "Microsoft.VCRedist.2015+.x64";

        /// <summary>
        /// Se o RTSS ja esta publicando a memoria compartilhada.
        ///
        /// A pergunta e essa, e nao "esta instalado": o que o aplicativo le e a
        /// memoria, e um RTSS instalado mas parado nao entrega leitura nenhuma.
        /// </summary>
        public static bool RtssPresente()
        {
            try
            {
                using (System.IO.MemoryMappedFiles.MemoryMappedFile m =
                       System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(
                           "RTSSSharedMemoryV2",
                           System.IO.MemoryMappedFiles.MemoryMappedFileRights.Read))
                    return m != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Caminho do winget, ou nulo quando ele nao existe na maquina. So o
        /// atalho do App Installer, e nao o PATH: o instalador roda elevado, e
        /// o PATH do usuario e gravavel sem privilegio. Mesma regra do Rtss.Winget.
        /// </summary>
        public static string Winget()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) return null;
                string alvo = Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
                if (!File.Exists(alvo)) return null;
                if ((File.GetAttributes(alvo) & FileAttributes.ReparsePoint) == 0) return null;
                return alvo;
            }
            catch { return null; }
        }

        /// <summary>Recurso embutido -> caminho relativo ao destino.</summary>
        private static readonly string[][] Payload = new string[][]
        {
            new string[] { "payload.MhiagosControl.exe",         "MhiagosControl.exe" },
            new string[] { "payload.LibreHardwareMonitorLib.dll", "LibreHardwareMonitorLib.dll" },
            // A MPL 2.0 exige que o texto da licenca acompanhe a distribuicao
            // do binario, e nao so do codigo. O repositorio ja o tinha; o
            // instalador nao levava, e passou a levar quando virou Release.
            new string[] { "payload.lhm-license.txt",             "LibreHardwareMonitor-LICENSE.txt" },
            new string[] { "payload.notices.txt",                 "THIRD-PARTY-NOTICES.txt" },
            new string[] { "payload.engine.dll",                  @"engine\api-ms-win-core-sysinfo-825-64.dll" },
        };

        /// <summary>
        /// O Payload fixo mais as dependencias da LibreHardwareMonitor, que
        /// entram como payload.lib.&lt;arquivo&gt;. A lista delas muda a cada versao
        /// da biblioteca; ler dos recursos evita mante-la em dois lugares.
        /// </summary>
        private static List<string[]> Itens()
        {
            List<string[]> itens = new List<string[]>(Payload);
            const string Prefixo = "payload.lib.";
            foreach (string nome in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (nome.StartsWith(Prefixo, StringComparison.Ordinal))
                    itens.Add(new string[] { nome, nome.Substring(Prefixo.Length) });
            return itens;
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool desinstalar = args.Length > 0 &&
                (args[0] == "/uninstall" || args[0] == "-uninstall" || args[0] == "/u");

            Application.Run(new Janela(desinstalar));
        }

        // ---------------- instalar ----------------

        public static void Instalar(string destino, bool atalhoArea, bool autostart, Action<string> diz)
        {
            bool atualizando = File.Exists(Path.Combine(destino, "MhiagosControl.exe"));

            diz("Encerrando o aplicativo, se estiver em execução...");
            Encerrar();

            diz("Criando " + destino);
            bool nossa = !Directory.Exists(destino) || atualizando ||
                         Directory.GetFileSystemEntries(destino).Length == 0;
            Directory.CreateDirectory(destino);
            // O aplicativo roda como administrador, e a tarefa de inicio
            // automatico o abre sem UAC. Fora de Program Files a pasta herdaria
            // escrita para usuarios comuns, e qualquer programa trocaria o exe
            // ou as DLLs. So mexe em pasta que e nossa: escolher D:\Jogos como
            // destino nao pode trancar o resto do disco.
            if (nossa) Proteger(destino, diz);
            Directory.CreateDirectory(Path.Combine(destino, "engine"));

            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string[] item in Itens())
            {
                string alvo = Path.Combine(destino, item[1]);
                using (Stream s = asm.GetManifestResourceStream(item[0]))
                {
                    if (s == null)
                    {
                        // So o motor do HWiNFO pode faltar: sem ele o aplicativo
                        // sobe na fonte de reserva e avisa na aba Sobre.
                        diz("  (ausente no instalador: " + item[1] + ")");
                        continue;
                    }
                    diz("  " + item[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(alvo));
                    using (FileStream f = File.Create(alvo)) s.CopyTo(f);
                }
            }

            string exe = Path.Combine(destino, "MhiagosControl.exe");

            diz("Copiando o desinstalador");
            string desinst = Path.Combine(destino, "uninstall.exe");
            string eu = Assembly.GetExecutingAssembly().Location;
            // Numa atualizacao rodada de dentro da propria pasta instalada a
            // origem e o destino seriam o mesmo arquivo, e File.Copy lanca.
            if (!string.Equals(Path.GetFullPath(eu), Path.GetFullPath(desinst),
                               StringComparison.OrdinalIgnoreCase))
                File.Copy(eu, desinst, true);

            // Os dois itens abaixo seguem a caixa marcada nos dois sentidos.
            // Numa atualizacao a caixa vem preenchida com o estado atual, entao
            // desmarcar tem de remover de verdade - senao a caixa mente.
            diz("Criando atalhos");
            string menu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk");
            Atalho(menu, exe, destino);
            string naArea = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                AppName + ".lnk");
            if (atalhoArea) Atalho(naArea, exe, destino);
            else Apagar(naArea);
            // avisa o Explorer para recarregar os icones ja em cache, sem esperar
            // o proximo logon
            AvisarIcones();

            diz("Registrando em Aplicativos Instalados");
            Registrar(destino, exe, desinst);

            int rc;
            if (autostart)
            {
                diz("Configurando o início automático");
                // Mesma tarefa que o proprio aplicativo cria e remove pelo menu.
                // Recria-la aqui tambem conserta o caso de ela ter ficado
                // apontando para um caminho antigo.
                Schtasks("/create /tn \"" + TaskName + "\" /tr \"\\\"" + exe +
                         "\\\"\" /sc onlogon /rl highest /f", out rc);
                if (rc != 0) diz("  aviso: a tarefa agendada não foi criada (código " + rc + ")");
            }
            else if (TemAutostart())
            {
                diz("Removendo o início automático");
                Schtasks("/delete /tn \"" + TaskName + "\" /f", out rc);
            }

            diz("");
            diz((atualizando ? "Atualizado em " : "Instalado em ") + destino);
            if (atualizando) diz("Os perfis e as configurações foram conservados.");
        }

        /// <summary>
        /// Deixa a pasta como a de Program Files: administradores e sistema
        /// gravam, usuarios comuns so leem e executam.
        /// </summary>
        internal static void Proteger(string destino, Action<string> diz)
        {
            try
            {
                var acl = new System.Security.AccessControl.DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                var heranca = System.Security.AccessControl.InheritanceFlags.ContainerInherit |
                              System.Security.AccessControl.InheritanceFlags.ObjectInherit;
                var nenhuma = System.Security.AccessControl.PropagationFlags.None;
                var permitir = System.Security.AccessControl.AccessControlType.Allow;
                var admins = new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(admins,
                    System.Security.AccessControl.FileSystemRights.FullControl, heranca, nenhuma, permitir));
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                    System.Security.AccessControl.FileSystemRights.FullControl, heranca, nenhuma, permitir));
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                    System.Security.AccessControl.FileSystemRights.ReadAndExecute, heranca, nenhuma, permitir));
                acl.SetOwner(admins);
                Directory.SetAccessControl(destino, acl);
            }
            catch (Exception ex)
            {
                diz("  aviso: não foi possível proteger a pasta (" + ex.Message + ")");
            }
        }

        // ---------------- desinstalar ----------------

        public static void Desinstalar(bool apagarDados, Action<string> diz)
        {
            string destino = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            diz("Encerrando o aplicativo...");
            Encerrar();

            diz("Removendo o início automático");
            int rc;
            Schtasks("/delete /tn \"" + TaskName + "\" /f", out rc);

            diz("Removendo atalhos");
            Apagar(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk"));
            Apagar(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"));

            diz("Removendo o registro");
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); }
            catch (Exception ex) { diz("  aviso: " + ex.Message); }
            // nome e icone das notificacoes, que o app grava no HKCU de quem o usa
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\Feurrado.MhiagosControl", false); }
            catch (Exception ex) { diz("  aviso: " + ex.Message); }

            diz("Apagando arquivos");
            foreach (string[] item in Itens()) Apagar(Path.Combine(destino, item[1]));
            Apagar(Path.Combine(destino, "engine"), true);

            if (apagarDados)
            {
                string dados = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MhiagosControl");
                diz("Apagando perfis e registro em " + dados);
                try { if (Directory.Exists(dados)) Directory.Delete(dados, true); }
                catch (Exception ex) { diz("  aviso: " + ex.Message); }
            }

            diz("");
            diz("Desinstalado. O desinstalador se remove ao fechar esta janela.");
        }

        /// <summary>
        /// Remove o proprio desinstalador depois que ele sair.
        ///
        /// Aguarda o processo terminar; a pasta so e removida se estiver vazia.
        /// </summary>
        public static void AgendarAutoRemocao()
        {
            try
            {
                string eu = Assembly.GetExecutingAssembly().Location;
                string script = CleanupScript(eu, Process.GetCurrentProcess().Id);
                string powershell = Path.Combine(Environment.SystemDirectory,
                    @"WindowsPowerShell\v1.0\powershell.exe");
                ProcessStartInfo p = new ProcessStartInfo(powershell,
                    "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                p.CreateNoWindow = true;
                p.UseShellExecute = false;
                p.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(p);
            }
            catch { }
        }

        internal static string CleanupScript(string executable, int processId)
        {
            string file = Path.GetFullPath(executable);
            string folder = Path.GetDirectoryName(file);
            // Caminhos sao dados literais: aspas, %, $ e acentos nao viram codigo.
            return "$cleanupFile = '" + file.Replace("'", "''") + "'\r\n" +
                "$cleanupFolder = '" + folder.Replace("'", "''") + "'\r\n" +
                "Wait-Process -Id " + processId + " -ErrorAction SilentlyContinue\r\n" +
                "for ($cleanupAttempt = 0; $cleanupAttempt -lt 40; $cleanupAttempt++) {\r\n" +
                "  try { Remove-Item -LiteralPath $cleanupFile -Force -ErrorAction Stop; break }\r\n" +
                "  catch { if (-not (Test-Path -LiteralPath $cleanupFile)) { break }; Start-Sleep -Milliseconds 250 }\r\n" +
                "}\r\n" +
                "try { [IO.Directory]::Delete($cleanupFolder, $false) } catch {}\r\n";
        }

        // ---------------- utilidades ----------------

        private static void Encerrar()
        {
            // Primeiro pede para o app fechar sozinho: assim ele grava o
            // historico das metricas e das baterias. Matar direto perdia ate
            // cinco minutos de curva a cada atualizacao. Versoes antigas nao
            // tem o evento, e ai so resta o Kill.
            try
            {
                using (System.Threading.EventWaitHandle saida =
                    System.Threading.EventWaitHandle.OpenExisting("Local\\MhiagosControl_Exit"))
                    saida.Set();
                foreach (Process p in Process.GetProcessesByName(ProcName))
                {
                    try { p.WaitForExit(10000); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            try
            {
                foreach (Process p in Process.GetProcessesByName(ProcName))
                {
                    try { p.Kill(); p.WaitForExit(5000); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        private static void Apagar(string caminho, bool pasta = false)
        {
            try
            {
                if (pasta) { if (Directory.Exists(caminho)) Directory.Delete(caminho, true); }
                else if (File.Exists(caminho)) File.Delete(caminho);
            }
            catch { }
        }

        private static void Registrar(string destino, string exe, string desinst)
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                if (k == null) return;
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Versao);
                k.SetValue("Publisher", "Feurrado");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", destino);
                k.SetValue("UninstallString", "\"" + desinst + "\" /uninstall");
                k.SetValue("URLInfoAbout", "https://github.com/Feurrado/MhiagosControl");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    long total = 0;
                    foreach (string f in Directory.GetFiles(destino, "*", SearchOption.AllDirectories))
                        total += new FileInfo(f).Length;
                    k.SetValue("EstimatedSize", (int)(total / 1024), RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        /// <summary>Atalho por COM tardio, para nao depender do IWshRuntimeLibrary.</summary>
        private static void Atalho(string lnk, string alvo, string dirTrabalho)
        {
            // Sempre do zero: o CreateShortcut sobre um .lnk existente reaproveita
            // a lista de itens antiga, com tamanho e datas de um exe de versoes
            // atras, e o Windows passa a mostrar o atalho (e o botao da barra de
            // tarefas, que o acha pela AUMID) com o icone de arquivo em branco.
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            object shell = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                shell = Activator.CreateInstance(t);
                object atalho = t.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type ta = atalho.GetType();
                ta.InvokeMember("TargetPath", BindingFlags.SetProperty, null, atalho, new object[] { alvo });
                ta.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, atalho, new object[] { dirTrabalho });
                ta.InvokeMember("Description", BindingFlags.SetProperty, null, atalho,
                    new object[] { "Driver alternativo para o painel do cooler" });
                ta.InvokeMember("IconLocation", BindingFlags.SetProperty, null, atalho, new object[] { alvo + ",0" });
                ta.InvokeMember("Save", BindingFlags.InvokeMethod, null, atalho, null);
            }
            catch { }
            finally { if (shell != null) try { Marshal.ReleaseComObject(shell); } catch { } }
            CarimbarAppId(lnk);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private static void AvisarIcones()
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000;
            const uint SHCNF_IDLIST = 0x0000;
            try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        /// <summary>A mesma AUMID que o app define para si (TaskbarIdentity.AppId).</summary>
        internal const string AppUserModelId = "Feurrado.MhiagosControl";

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCom { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFileCom
        {
            void GetClassID(out Guid clsid);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string arquivo, int modo);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string arquivo, bool lembrar);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string arquivo);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string arquivo);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid Fmtid;
            public int Pid;
            public PropertyKey(Guid fmtid, int pid) { Fmtid = fmtid; Pid = pid; }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Vt;
            [FieldOffset(8)] public IntPtr Ptr;
            [FieldOffset(16)] private long _resto;   // 24 bytes: o tamanho do x64; no x86 (16) a sobra e ignorada
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStoreCom
        {
            void GetCount(out uint quantos);
            void GetAt(uint i, out PropertyKey chave);
            void GetValue(ref PropertyKey chave, out PropVariant valor);
            void SetValue(ref PropertyKey chave, ref PropVariant valor);
            void Commit();
        }

        /// <summary>A AUMID gravada no atalho, ou nulo; para os testes e o diagnostico.</summary>
        internal static string LerAppId(string lnk)
        {
            if (!File.Exists(lnk)) return null;
            object link = null;
            try
            {
                link = new ShellLinkCom();
                ((IPersistFileCom)link).Load(lnk, 0);
                PropertyKey chave = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                PropVariant v;
                ((IPropertyStoreCom)link).GetValue(ref chave, out v);
                if (v.Vt != 31 || v.Ptr == IntPtr.Zero) return null;
                string id = Marshal.PtrToStringUni(v.Ptr);
                Marshal.FreeCoTaskMem(v.Ptr);
                return id;
            }
            catch { return null; }
            finally { if (link != null) try { Marshal.ReleaseComObject(link); } catch { } }
        }

        /// <summary>Cria o atalho como na instalacao; para os testes.</summary>
        internal static void CriarAtalho(string lnk, string alvo, string dirTrabalho) { Atalho(lnk, alvo, dirTrabalho); }

        /// <summary>
        /// Grava System.AppUserModel.ID no atalho. Sem isso o Windows nao liga
        /// as notificacoes do app (que usa essa AUMID) a nome nenhum e mostra
        /// "Feurrado.MhiagosControl" no cabecalho; com o atalho do Menu Iniciar
        /// carimbado, o aviso usa o nome e o icone do atalho.
        /// </summary>
        internal static bool CarimbarAppId(string lnk)
        {
            if (!File.Exists(lnk)) return false;
            object link = null;
            IntPtr texto = IntPtr.Zero;
            try
            {
                link = new ShellLinkCom();
                IPersistFileCom arquivo = (IPersistFileCom)link;
                arquivo.Load(lnk, 2);   // STGM_READWRITE
                IPropertyStoreCom props = (IPropertyStoreCom)link;
                PropertyKey chave = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                PropVariant v = new PropVariant();
                v.Vt = 31;              // VT_LPWSTR
                texto = Marshal.StringToCoTaskMemUni(AppUserModelId);
                v.Ptr = texto;
                props.SetValue(ref chave, ref v);
                props.Commit();
                arquivo.Save(lnk, true);
                return true;
            }
            catch { return false; }
            finally
            {
                if (texto != IntPtr.Zero) Marshal.FreeCoTaskMem(texto);
                if (link != null) try { Marshal.ReleaseComObject(link); } catch { }
            }
        }

        private static string Schtasks(string argumentos, out int codigo)
        {
            codigo = -1;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", argumentos);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                return RunProcess(psi, 15000, out codigo);
            }
            catch { return ""; }
        }

        internal static string RunProcess(ProcessStartInfo psi, int timeoutMs, out int code)
        {
            code = -1;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            Stopwatch elapsed = Stopwatch.StartNew();
            using (Process process = Process.Start(psi))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(Math.Max(0, timeoutMs)))
                {
                    try { process.Kill(); } catch { }
                    return "processo excedeu o prazo de " + timeoutMs + " ms";
                }
                // Drenagem tambem tem prazo: um descendente pode herdar os pipes.
                if (!Task.WaitAll(new Task[] { output, error },
                    Math.Max(0, timeoutMs - (int)elapsed.ElapsedMilliseconds)))
                    return "saida do processo excedeu o prazo";
                code = process.ExitCode;
                return output.Result + error.Result;
            }
        }

        public static string DestinoPadrao
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
            }
        }

        public static string JaInstaladoEm()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                    return k == null ? null : k.GetValue("InstallLocation") as string;
            }
            catch { return null; }
        }

        public static string VersaoInstalada()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
                    return k == null ? null : k.GetValue("DisplayVersion") as string;
            }
            catch { return null; }
        }

        public static bool TemAtalhoNaArea()
        {
            try
            {
                return File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    AppName + ".lnk"));
            }
            catch { return false; }
        }

        /// <summary>A tarefa de inicio automatico existe? Codigo 0 do schtasks.</summary>
        public static bool TemAutostart()
        {
            int rc;
            Schtasks("/query /tn \"" + TaskName + "\"", out rc);
            return rc == 0;
        }
    }

    /// <summary>Janela unica: instala ou desinstala, conforme o argumento.</summary>
    public class Janela : Form
    {
        private static readonly Color Fundo = Color.FromArgb(0x1C, 0x1C, 0x1F);
        private static readonly Color Cartao = Color.FromArgb(0x26, 0x26, 0x2A);
        private static readonly Color Texto = Color.FromArgb(0xF0, 0xF0, 0xF3);
        private static readonly Color Fraco = Color.FromArgb(0x96, 0x99, 0xA3);
        private static readonly Color Acento = Color.FromArgb(0x2D, 0x7D, 0xF6);

        private readonly bool _desinstalar;
        private readonly bool _atualizar;
        private readonly string _jaEm;
        private TextBox _dir;
        private CheckBox _area, _auto, _abrir, _dados, _rtss;
        private Button _acao, _fechar;
        private TextBox _log;
        private bool _pronto = false;

        public Janela(bool desinstalar)
        {
            _desinstalar = desinstalar;
            _jaEm = Setup.JaInstaladoEm();
            _atualizar = !desinstalar && !string.IsNullOrEmpty(_jaEm) &&
                         File.Exists(Path.Combine(_jaEm, "MhiagosControl.exe"));

            Text = Setup.AppName +
                   (desinstalar ? " - desinstalar" : _atualizar ? " - atualizar" : " - instalar");
            ClientSize = new Size(600, desinstalar ? 400 : 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Fundo;
            ForeColor = Texto;
            Font = new Font("Segoe UI", 9f);

            int y = 18;
            Add(new Label
            {
                Text = Setup.AppName,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = Texto,
                Bounds = new Rectangle(20, y, 560, 30)
            });
            y += 34;

            Add(new Label
            {
                Text = desinstalar
                    ? "Remove o aplicativo, os atalhos e o início automático."
                    : _atualizar
                        ? "Atualização · " + Descrever(Setup.VersaoInstalada()) + "  →  versão " +
                          Setup.Versao + ". Os perfis são conservados."
                        : "Versão " + Setup.Versao + " · driver alternativo para o painel do cooler.",
                ForeColor = Fraco,
                Bounds = new Rectangle(20, y, 560, 20)
            });
            y += 32;

            if (!desinstalar)
            {
                Add(new Label
                {
                    Text = _atualizar ? "Atualizar a instalação em:" : "Instalar em:",
                    ForeColor = Fraco,
                    Bounds = new Rectangle(20, y, 560, 18)
                });
                y += 20;

                _dir = new TextBox
                {
                    Text = _atualizar ? _jaEm : Setup.DestinoPadrao,
                    Bounds = new Rectangle(20, y, 460, 26),
                    BackColor = Cartao,
                    ForeColor = Texto,
                    BorderStyle = BorderStyle.FixedSingle
                };
                Add(_dir);

                Button procurar = Botao("Procurar", new Rectangle(490, y - 1, 90, 28), false);
                procurar.Click += delegate
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "Onde instalar o " + Setup.AppName;
                        d.SelectedPath = _dir.Text;
                        if (d.ShowDialog(this) == DialogResult.OK)
                            _dir.Text = Path.Combine(d.SelectedPath, Setup.AppName);
                    }
                };
                Add(procurar);
                y += 38;

                // Numa atualizacao as caixas vem com o estado atual da maquina,
                // e nao com o padrao - marca-las de novo apagaria a escolha que
                // o usuario ja tinha feito. Instalar() honra os dois sentidos.
                _area = Check("Criar atalho na área de trabalho", 20, y,
                              _atualizar ? Setup.TemAtalhoNaArea() : true); y += 26;
                _auto = Check("Iniciar junto com o Windows", 20, y,
                              _atualizar ? Setup.TemAutostart() : true); y += 26;
                _abrir = Check("Executar ao terminar", 20, y, true); y += 26;

                // DESMARCADA, e so aparece quando faz sentido: e software de
                // outra gente, e ninguem deve descobrir depois que instalou algo
                // que nao pediu. Some quando o RTSS ja esta publicando, e quando
                // nao ha winget para instalar por ele.
                if (!Setup.RtssPresente() && Setup.Winget() != null)
                {
                    _rtss = Check("Instalar o RivaTuner Statistics Server (FPS) e iniciá-lo com o Windows, minimizado",
                                  20, y, false); y += 26;
                }
                y += 6;
            }
            else
            {
                // Desmarcado de proposito, e com o texto dizendo o que se perde:
                // sao os perfis, e reconstrui-los custa reescolher dois sensores
                // por perfil.
                _dados = Check("Apagar também os perfis e o registro de diagnóstico", 20, y, false);
                _dados.ForeColor = Color.FromArgb(0xEB, 0x5A, 0x4B);
                y += 26;
                Add(new Label
                {
                    Text = "Deixe desmarcado para conservar os perfis caso reinstale.",
                    ForeColor = Fraco,
                    Bounds = new Rectangle(40, y, 540, 18)
                });
                y += 30;
            }

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Cartao,
                ForeColor = Fraco,
                BorderStyle = BorderStyle.FixedSingle,
                Bounds = new Rectangle(20, y, 560, ClientSize.Height - y - 70)
            };
            Add(_log);

            _acao = Botao(desinstalar ? "Desinstalar" : _atualizar ? "Atualizar" : "Instalar",
                          new Rectangle(360, ClientSize.Height - 46, 110, 32), true);
            _acao.Click += OnAcao;
            Add(_acao);

            _fechar = Botao("Fechar", new Rectangle(480, ClientSize.Height - 46, 100, 32), false);
            _fechar.Click += delegate { Close(); };
            Add(_fechar);
        }

        private void Add(Control c) { Controls.Add(c); }

        /// <summary>A versao instalada, ou um texto neutro se ela nao constar.</summary>
        private static string Descrever(string versao)
        {
            return string.IsNullOrEmpty(versao) ? "versão instalada" : "versão " + versao;
        }

        private CheckBox Check(string texto, int x, int y, bool marcado)
        {
            CheckBox c = new CheckBox
            {
                Text = texto,
                Checked = marcado,
                ForeColor = Texto,
                Bounds = new Rectangle(x, y, 560, 22),
                FlatStyle = FlatStyle.Standard
            };
            Add(c);
            return c;
        }

        private Button Botao(string texto, Rectangle r, bool primario)
        {
            Button b = new Button
            {
                Text = texto,
                Bounds = r,
                FlatStyle = FlatStyle.Flat,
                BackColor = primario ? Acento : Cartao,
                ForeColor = primario ? Color.White : Texto,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primario ? Acento : Color.FromArgb(0x38, 0x38, 0x3E);
            return b;
        }

        private void Diz(string linha)
        {
            _log.AppendText(linha + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
            Application.DoEvents();
        }

        private void OnAcao(object sender, EventArgs e)
        {
            if (_pronto) { Close(); return; }

            _acao.Enabled = false;
            _fechar.Enabled = false;
            try
            {
                if (_desinstalar) Setup.Desinstalar(_dados.Checked, Diz);
                else Setup.Instalar(_dir.Text.Trim(), _area.Checked, _auto.Checked, Diz);

                _pronto = true;
                _acao.Text = "Concluir";
                _acao.Enabled = true;

                if (!_desinstalar && _rtss != null && _rtss.Checked) InstalarRtss();

                if (!_desinstalar && _abrir.Checked)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(
                            Path.Combine(_dir.Text.Trim(), "MhiagosControl.exe"));
                        psi.UseShellExecute = true;
                        psi.WorkingDirectory = _dir.Text.Trim();
                        Process.Start(psi);
                    }
                    catch (Exception ex) { Diz("Não deu para abrir o aplicativo: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Diz("");
                Diz("FALHOU: " + ex.Message);
                _acao.Enabled = true;
            }
            finally { _fechar.Enabled = true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_desinstalar && _pronto) Setup.AgendarAutoRemocao();
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Entrega o RTSS ao winget, numa janela visivel.
        ///
        /// Nao embutimos o instalador dele: e freeware de outra pessoa, e a
        /// licenca nao nos da direito de redistribuir. Baixar de um espelho
        /// durante a nossa instalacao seria pior ainda - sem URL estavel e sem
        /// soma de verificacao publicada, seria executar o que vier. Pelo winget
        /// a origem e o repositorio da Microsoft, o pacote e o oficial e a
        /// janela mostra o que aconteceu.
        /// </summary>
        private static string Chamada(string exe, string pacote)
        {
            return "\"" + exe + "\" install --id " + pacote +
                   " -e --source winget --accept-source-agreements";
        }

        private void InstalarRtss()
        {
            string winget = Setup.Winget();
            if (winget == null) { Diz("winget nao encontrado - o RTSS nao foi instalado."); return; }

            try
            {
                // "/s /k": com /s o cmd tira so a primeira e a ultima aspa e leva
                // o resto ao pe da letra, que e o que permite duas chamadas com
                // o caminho entre aspas na mesma linha.
                // Runtime, RTSS e por fim o aplicativo recem-instalado ligando o
                // "iniciar com o Windows" do RTSS. Encadeado na linha de comando
                // porque o winget roda numa janela a parte e daqui nao ha como
                // saber quando ele terminou - o "&" do cmd sabe.
                string app = Path.Combine(_dir.Text.Trim(), "MhiagosControl.exe");
                string linha = Chamada(winget, Setup.PacoteRuntime) + " & " +
                               Chamada(winget, Setup.PacoteRtss) + " & " +
                               "\"" + app + "\" --config-rtss";

                ProcessStartInfo psi = new ProcessStartInfo(
                    Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/s /k \"" + linha + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
                Diz("RTSS: instalacao entregue ao winget, numa janela a parte.");
            }
            catch (Exception ex) { Diz("Nao deu para chamar o winget: " + ex.Message); }
        }
    }
}
