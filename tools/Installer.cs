using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        public const string Versao = "2.12.0";
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

        /// <summary>Caminho do winget, ou nulo quando ele nao existe na maquina.</summary>
        public static string Winget()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path)) return null;

                foreach (string dir in path.Split(';'))
                {
                    string d = dir.Trim();
                    if (d.Length == 0) continue;
                    try
                    {
                        string alvo = Path.Combine(d, "winget.exe");
                        if (File.Exists(alvo)) return alvo;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
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
            new string[] { "payload.engine.dll",                  @"engine\api-ms-win-core-sysinfo-825-64.dll" },
        };

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
            Directory.CreateDirectory(destino);
            Directory.CreateDirectory(Path.Combine(destino, "engine"));

            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string[] item in Payload)
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

            diz("Apagando arquivos");
            foreach (string[] item in Payload) Apagar(Path.Combine(destino, item[1]));
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
            AgendarAutoRemocao(destino);
        }

        /// <summary>
        /// Remove o proprio desinstalador depois que ele sair.
        ///
        /// Um executavel em uso nao pode se apagar, entao quem apaga e um cmd
        /// que espera. O "rd" vai sem /s e sem /q de proposito: ele so remove a
        /// pasta se ela ja estiver vazia, e uma apagada recursiva com caminho
        /// errado aqui destruiria o que estivesse do outro lado.
        /// </summary>
        private static void AgendarAutoRemocao(string destino)
        {
            try
            {
                string eu = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo p = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + eu + "\" & rd \"" + destino + "\"");
                p.CreateNoWindow = true;
                p.UseShellExecute = false;
                p.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(p);
            }
            catch { }
        }

        // ---------------- utilidades ----------------

        private static void Encerrar()
        {
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
                using (Process p = Process.Start(psi))
                {
                    string saida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    codigo = p.HasExited ? p.ExitCode : -1;
                    return saida;
                }
            }
            catch { return ""; }
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

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/s /k \"" + linha + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
                Diz("RTSS: instalacao entregue ao winget, numa janela a parte.");
            }
            catch (Exception ex) { Diz("Nao deu para chamar o winget: " + ex.Message); }
        }
    }
}
