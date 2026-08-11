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
            }
            catch (Exception ex) { Log.Error("resumo do sistema", ex); }
            return s;
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
