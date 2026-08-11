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
        /// Encurta o nome do fabricante para caber nos 210 px da lateral.
        ///
        /// "AMD Ryzen 5 5600X 6-Core Processor" vira "Ryzen 5 5600X". Sem isso o
        /// texto e cortado com reticencias justamente no modelo, que e a parte
        /// que identifica a peca - sobraria "AMD Ryzen 5 56...".
        /// </summary>
        private static string Limpar(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return null;
            string s = nome.Trim();

            string[] prefixos = { "AMD ", "Intel ", "NVIDIA ", "Advanced Micro Devices " };
            foreach (string p in prefixos)
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(p.Length); break; }

            string[] sufixos = { " 6-Core Processor", " 8-Core Processor", " 12-Core Processor",
                                 " 16-Core Processor", " Processor", " CPU" };
            foreach (string x in sufixos)
                if (s.EndsWith(x, StringComparison.OrdinalIgnoreCase))
                { s = s.Substring(0, s.Length - x.Length); break; }

            return s.Trim();
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
