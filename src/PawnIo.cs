using System;
using Microsoft.Win32;

namespace MhiagosControl
{
    /// <summary>
    /// O PawnIO, o driver que a LibreHardwareMonitor usa desde a 0.9.5 para
    /// ler os sensores de baixo nivel (temperatura e potencia da CPU, Super
    /// I/O). Na edicao publica, sem a biblioteca do HWiNFO, ele e o que
    /// separa um mostrador com a temperatura da CPU de um vazio - e nada
    /// dizia isso a quem instalava.
    /// </summary>
    internal static class PawnIo
    {
        /// <summary>Pacote do winget (conferido em 07/11/2026: namazso.PawnIO 2.2.0).</summary>
        public const string PacoteWinget = "namazso.PawnIO";

        /// <summary>Testes: forca a resposta sem olhar o registro.</summary>
        internal static bool? Forcado { get; set; }

        /// <summary>O servico do driver esta registrado (instalado) nesta maquina.</summary>
        public static bool Instalado
        {
            get
            {
                if (Forcado.HasValue) return Forcado.Value;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"))
                        return k != null;
                }
                catch (Exception ex) { Log.Error("conferir o PawnIO", ex); return false; }
            }
        }
    }
}
