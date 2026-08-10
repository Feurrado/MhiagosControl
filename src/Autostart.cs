using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;

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
