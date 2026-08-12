using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MhiagosControl
{
    /// <summary>
    /// Identificacao da maquina para a barra lateral: processador, placa de
    /// video e memoria.
    ///
    /// Os dois primeiros saem da propria lista de sensores, que ja tem o nome
    /// que a fonte publica ("AMD Ryzen 5 5600X", "NVIDIA GeForce RTX 3060") e a
    /// categoria de cada leitura. Nao vale abrir uma consulta WMI para isso: a
    /// informacao ja passou por aqui, e WMI custa centenas de milissegundos no
    /// arranque - justamente onde o aplicativo ja demora.
    ///
    /// A memoria e a excecao. Ela apareceria nos sensores como "usada" e
    /// "disponivel" em GB, e somar duas leituras que mudam a cada ciclo para
    /// exibir um total fixo daria um numero que oscila. GlobalMemoryStatusEx
    /// devolve o total instalado direto, e nao muda enquanto a maquina estiver
    /// ligada.
    /// </summary>
    public class SystemInfo
    {
        public string Cpu;
        public string Gpu;
        public string Ram;

        // Detalhe da tela de bordo. A barra lateral nao usa nada disto - la
        // cabem tres linhas e o que importa e o modelo.
        public string CpuNucleos;   // "6 nucleos - 12 threads"
        public string GpuMemoria;   // "8 GB"
        public string Placa;        // nulo quando a fonte nao publica a placa
        public string Sistema;      // "Windows 11 Pro - 25H2 (26200.8973)"

        /// <summary>Ha pelo menos uma linha para mostrar?</summary>
        public bool Any
        {
            get
            {
                return !string.IsNullOrEmpty(Cpu) || !string.IsNullOrEmpty(Gpu) ||
                       !string.IsNullOrEmpty(Ram);
            }
        }

        public static SystemInfo From(List<SensorEntry> sensores)
        {
            SystemInfo s = new SystemInfo();
            try
            {
                s.Cpu = Limpar(PrimeiroDaCategoria(sensores, "CPU"));
                s.Gpu = Limpar(PrimeiroDaCategoria(sensores, "GPU"));
                s.Ram = MemoriaInstalada();

                s.CpuNucleos = ContagemDeNucleos(sensores);
                s.GpuMemoria = MemoriaDeVideo(sensores);
                s.Placa = NomeDePlaca(Limpar(PrimeiroDaCategoria(sensores, "Placa-mãe")));
                s.Sistema = VersaoDoWindows();
            }
            catch (Exception ex) { Log.Error("resumo do sistema", ex); }
            return s;
        }

        /// <summary>
        /// Nucleos e threads, tirados do que ja esta na lista.
        ///
        /// Os threads o proprio .NET responde. Os nucleos FISICOS nao: seriam
        /// uma consulta WMI, que custa centenas de milissegundos. Mas a contagem
        /// ja passou por aqui - o Condense junta as leituras por nucleo num
        /// agregado e guarda quantas eram, e a de clock e publicada por nucleo
        /// fisico. Num 5600X da 6, com 12 threads, que e o que a peca e.
        ///
        /// Quando as duas batem, so os threads sao mostrados: "12 nucleos - 12
        /// threads" nao informa nada que "12 threads" ja nao diga.
        /// </summary>
        private static string ContagemDeNucleos(List<SensorEntry> sensores)
        {
            int threads = Environment.ProcessorCount;
            if (threads <= 0) return null;

            int nucleos = 0;
            if (sensores != null)
                foreach (SensorEntry e in sensores)
                {
                    if (e == null || e.Category != "CPU" || e.Members <= 1) continue;
                    if (e.Type != LibreHardwareMonitor.Hardware.SensorType.Clock) continue;
                    if (e.Members > threads) continue;   // agregado que nao e por nucleo
                    nucleos = e.Members;
                    break;
                }

            if (nucleos > 1 && nucleos < threads)
                return T.CoresAndThreads(nucleos, threads);
            return T.ThreadsOnly(threads);
        }

        /// <summary>
        /// Aceita o nome da placa-mae so quando ele identifica alguma placa.
        ///
        /// Sem o filtro, a linha saia "Placa-mãe: ACPI" - que e o nome do
        /// BARRAMENTO onde a LibreHardwareMonitor achou o sensor, nao o da peca.
        /// O mesmo vale para o que a propria placa grava quando o montador nao
        /// preencheu nada: "To Be Filled By O.E.M." e literalmente o texto de
        /// exemplo do formulario. Um rotulo apontando para isso e pior que a
        /// ausencia da linha, porque parece informacao.
        /// </summary>
        internal static string NomeDePlaca(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return null;

            string[] lixo =
            {
                "ACPI", "Motherboard", "Generic", "Unknown", "System",
                "Default string", "To Be Filled By O.E.M.", "None", "N/A",
                "SMBIOS", "LPC", "Chipset",
            };
            foreach (string x in lixo)
                if (string.Equals(nome, x, StringComparison.OrdinalIgnoreCase)) return null;

            // Um nome de placa tem fabricante e modelo: "B550M Steel Legend",
            // "PRIME X570-P". Menos de cinco caracteres nunca e um deles.
            return nome.Trim().Length < 5 ? null : nome;
        }

        /// <summary>
        /// Memoria da placa de video, em GB.
        ///
        /// Cada fonte batiza esse total de um jeito, e exigir um nome so fazia a
        /// informacao existir ou nao conforme quem tivesse respondido primeiro.
        /// A lista vai do mais especifico ao mais generico.
        ///
        /// O ultimo recurso e somar o usado com o livre: quando nenhuma publica o
        /// total, as duas metades quase sempre estao la, e a soma delas E o total
        /// - com erro de arredondamento de alguns megabytes, que desaparece na
        /// conversao para gigabytes inteiros.
        /// </summary>
        private static string MemoriaDeVideo(List<SensorEntry> sensores)
        {
            if (sensores == null) return null;

            string[] nomes =
            {
                "GPU Memory Total", "GPU D3D Memory Total", "D3D Dedicated Memory Total",
                "Memory Total", "GPU Memory Size", "VRAM",
            };

            foreach (string alvo in nomes)
                foreach (SensorEntry e in sensores)
                {
                    if (!DaGpu(e) || e.Name == null) continue;
                    if (!e.Name.Equals(alvo, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!e.Value.HasValue || e.Value.Value <= 0) continue;
                    return EmGigabytes(e.Value.Value);
                }

            float usado = 0, livre = 0;
            foreach (SensorEntry e in sensores)
            {
                if (!DaGpu(e) || e.Name == null || !e.Value.HasValue) continue;
                if (e.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)) usado = e.Value.Value;
                else if (e.Name.Equals("GPU Memory Free", StringComparison.OrdinalIgnoreCase)) livre = e.Value.Value;
            }
            if (usado > 0 && livre > 0) return EmGigabytes(usado + livre);

            return null;
        }

        private static bool DaGpu(SensorEntry e)
        {
            return e != null && e.Category == "GPU";
        }

        /// <summary>
        /// Converte o total publicado em MB para gigabytes redondos.
        ///
        /// Acima de 64 GB o numero nao era megabyte: alguma fonte publica em
        /// bytes, e uma RTX de 12 GB apareceria com cinco digitos de gigabyte.
        /// </summary>
        internal static string EmGigabytes(double mb)
        {
            double gb = mb / 1024.0;
            if (gb > 1024) gb = mb / 1073741824.0;   // vinha em bytes
            if (gb <= 0) return null;
            return (gb >= 1 ? Math.Round(gb).ToString("0") : gb.ToString("0.0")) + " GB";
        }

        /// <summary>
        /// Nome e versao do Windows.
        ///
        /// O ProductName do registro MENTE: numa maquina com build 26200 ele
        /// responde "Windows 10 Pro". A Microsoft nunca o atualizou na virada
        /// para o 11, e quem le so ele mostra a versao errada. O numero da
        /// compilacao e que decide - 22000 e a primeira do Windows 11.
        ///
        /// Environment.OSVersion tambem nao serve: sem entrada de compatibilidade
        /// no manifesto ele para em 6.2 (Windows 8), que e a resposta que o
        /// Windows da a quem nao declarou conhecer nada mais novo.
        /// </summary>
        private static string VersaoDoWindows()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k == null) return null;

                    string nome = Convert.ToString(k.GetValue("ProductName"));
                    if (string.IsNullOrEmpty(nome)) return null;

                    int build = 0;
                    int.TryParse(Convert.ToString(k.GetValue("CurrentBuild")), out build);
                    if (build >= 22000) nome = nome.Replace("Windows 10", "Windows 11");

                    string versao = Convert.ToString(k.GetValue("DisplayVersion"));
                    if (!string.IsNullOrEmpty(versao)) nome += "  ·  " + versao;

                    if (build > 0)
                    {
                        string ubr = Convert.ToString(k.GetValue("UBR"));
                        nome += " (" + build + (string.IsNullOrEmpty(ubr) ? "" : "." + ubr) + ")";
                    }
                    return nome;
                }
            }
            catch (Exception ex) { Log.Error("versao do Windows", ex); return null; }
        }

        private static string PrimeiroDaCategoria(List<SensorEntry> sensores, string categoria)
        {
            if (sensores == null) return null;
            foreach (SensorEntry e in sensores)
            {
                if (e == null || e.Category != categoria) continue;
                if (!string.IsNullOrEmpty(e.Hardware)) return e.Hardware;
            }
            return null;
        }

        /// <summary>
        /// Encurta o nome ate sobrar o modelo, que e o que identifica a peca.
        ///
        /// O HWiNFO publica "CPU [#0]: AMD Ryzen 5 5600X" e a LibreHardwareMonitor
        /// publica "AMD Ryzen 5 5600X 6-Core Processor". Nos 140 px da coluna,
        /// os dois eram cortados exatamente sobre o modelo - sobrava
        /// "CPU [#0]: AMD Ryzen 5 ...", que gasta a linha inteira dizendo o que
        /// o rotulo ao lado ja diz.
        ///
        /// A ordem importa: o prefixo de enumeracao sai primeiro, senao o nome
        /// do fabricante nunca esta no comeco e a remocao seguinte nao encontra
        /// nada. Foi exatamente esse o defeito.
        /// </summary>
        internal static string Limpar(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return null;
            string s = nome.Trim();

            // "CPU [#0]: ", "GPU [#1]: " - enumeracao do HWiNFO
            s = System.Text.RegularExpressions.Regex.Replace(
                    s, @"^[A-Za-z0-9 ]{1,12}\[#\d+\]\s*:\s*", "").Trim();

            string[] prefixos = { "AMD ", "Intel(R) ", "Intel ", "NVIDIA ",
                                  "Advanced Micro Devices ", "Radeon(TM) " };
            foreach (string p in prefixos)
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(p.Length).Trim(); break; }

            // "6-Core Processor", "with Radeon Graphics", "CPU @ 3.70GHz"
            s = System.Text.RegularExpressions.Regex.Replace(
                    s, @"\s+\d{1,2}-Core Processor$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(
                    s, @"\s+(CPU\s*)?@\s*[\d.,]+\s*[GM]Hz$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string[] sufixos = { " Processor", " Graphics Card", " with Radeon Graphics", " CPU" };
            foreach (string x in sufixos)
                if (s.EndsWith(x, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(0, s.Length - x.Length).Trim(); break; }

            // "Radeon RX 580: Sapphire RX 580 Pulse" - o HWiNFO junta o chip e a
            // placa do parceiro, e cada metade repete o modelo. Fica o chip, que
            // e o que responde "que placa e essa"; o nome comercial da montadora
            // gasta a linha para repetir o numero que ja esta ali.
            int dp = s.IndexOf(':');
            if (dp > 0) s = s.Substring(0, dp).Trim();

            return s.Length == 0 ? null : s;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys;
            public ulong ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX s);

        private static string MemoriaInstalada()
        {
            try
            {
                MEMORYSTATUSEX m = new MEMORYSTATUSEX();
                m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (!GlobalMemoryStatusEx(ref m)) return null;

                // O Windows reserva uma fatia antes de reportar, entao 32 GB
                // instalados aparecem como 31,8. Arredondar para o inteiro mais
                // proximo devolve o numero que a pessoa comprou.
                double gb = m.ullTotalPhys / 1073741824.0;
                if (gb <= 0) return null;
                return Math.Round(gb).ToString("0") + " GB";
            }
            catch (Exception ex) { Log.Error("memoria instalada", ex); return null; }
        }
    }
}
